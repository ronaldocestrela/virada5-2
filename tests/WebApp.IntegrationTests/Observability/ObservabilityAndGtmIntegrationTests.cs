using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.UseCases.Employees;
using Domain.Common;
using Domain.Enums;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.IntegrationTests.Fixtures;

namespace WebApp.IntegrationTests.Observability;

public sealed class ObservabilityAndGtmIntegrationTests : IDisposable
{
    private readonly TenantIsolationWebApplicationFactory _factory = new();

    [Fact]
    public async Task HealthEndpoints_WhenRequestedAnonymously_ShouldReturnHealthyLiveAndReady()
    {
        // Arrange
        var client = _factory.CreateAnonymousClient();

        // Act
        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");

        // Assert
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var readyJson = await readyResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(readyJson);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        doc.RootElement.GetProperty("checks").GetProperty("database").GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task EbookLanding_WhenAccessedWithUtmParameters_ShouldRenderQrCodeAndPreserveUtmInRegisterCta()
    {
        // Arrange
        var client = _factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=cap7_gtm&utm_content=cta_final");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Transforme sua escala 6x1 em 5x2");
        html.Should().Contain("/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=cap7_gtm&utm_content=cta_final");
        html.Should().Contain("<svg");
    }

    [Fact]
    public async Task RegisterRoute_WhenSubmittedWithValidOwner_ShouldCreateIsolatedTenantAndAuthenticateOnboarding()
    {
        // Arrange
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }

            recordedMeasurements.Enqueue((instrument.Name, dict));
        });
        meterListener.Start();

        var client = _factory.CreateAnonymousClient(allowAutoRedirect: false);
        var getHtml = await (await client.GetAsync("/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=lancamento&utm_content=cap4_simulador")).Content.ReadAsStringAsync();
        var token = TenantIsolationWebApplicationFactory.ExtractRequestVerificationToken(getHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "register",
            ["__RequestVerificationToken"] = token,
            ["Input.FullName"] = "Marina Silva",
            ["Input.Email"] = "marina-ebook@bistro5x2.com.br",
            ["Input.Password"] = "SenhaForte123"
        });

        // Act
        var postResponse = await client.PostAsync("/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=lancamento&utm_content=cap4_simulador", form);

        // Assert
        postResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        postResponse.Headers.Location?.ToString().Should().Contain("/onboarding");

        var onboardingResponse = await client.GetAsync("/onboarding");
        onboardingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var registerWhenAuthenticated = await client.GetAsync("/Account/Register");
        registerWhenAuthenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await registerWhenAuthenticated.Content.ReadAsStringAsync()).Should().Contain("Sessão já conectada");

        var gtmKitResponse = await client.GetAsync("/gtm/ebook-kit");
        gtmKitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var gtmKitHtml = await gtmKitResponse.Content.ReadAsStringAsync();
        gtmKitHtml.Should().Contain("prefacio_qr");
        gtmKitHtml.Should().Contain("contracapa_qr");

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.GtmConversionsCounterName &&
            Equals(m.Tags["event"], "tenant_owner_registered") &&
            Equals(m.Tags["utm_content"], "cap4_simulador"));

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await userManager.FindByEmailAsync("marina-ebook@bistro5x2.com.br");
        created.Should().NotBeNull();
        created!.TenantId.Should().NotBe(Guid.Empty);
        (await userManager.IsInRoleAsync(created, ApplicationRoles.Owner)).Should().BeTrue();
    }

    [Fact]
    public async Task GtmEbookKitRoute_WhenRequestedAnonymously_ShouldRedirectToLogin()
    {
        // Arrange
        var client = _factory.CreateAnonymousClient(allowAutoRedirect: false);

        // Act
        var response = await client.GetAsync("/gtm/ebook-kit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public void StructuredResultTelemetry_WhenRegisteredInDi_ShouldResolveAndHandleSuccessAndFailure()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<IResultTelemetry>();

        // Act
        Action act = () =>
        {
            telemetry.RecordUseCaseOutcome("GenerateRoster5x2", Result.Success(), 12, Guid.NewGuid());
            telemetry.RecordUseCaseOutcome(
                "GenerateRoster5x2",
                Result.Failure(Error.Validation("RosterGeneration.NoActiveEmployees", "Sem colaboradores")),
                4,
                Guid.NewGuid(),
                "Validation");
            telemetry.RecordUseCaseOutcome(
                "UnexpectedOp",
                Result.Failure(Error.Failure("Unexpected.Failure", "Erro interno")),
                19,
                null,
                "Failure");
            telemetry.RecordConversionEvent("ebook_landing_view", "ebook", "qrcode", "lancamento");
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseCaseResolvedFromDi_WhenReturningValidationFailure_ShouldEmitOtelMetricsAndCorrelatedActivityWithTenantId()
    {
        // Arrange
        var seeded = await _factory.SeedFullTenantAsync("Casa Telemetria", "telemetria@casa.com.br");
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, Dictionary<string, object?> Tags)>();
        var recordedActivities = new ConcurrentQueue<Activity>();

        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }

            recordedMeasurements.Enqueue((instrument.Name, dict));
        });
        meterListener.Start();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => recordedActivities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = TenantIsolationWebApplicationFactory.CreateAuthenticatedHttpContext(seeded.TenantId, seeded.OwnerUserId);

        var useCase = scope.ServiceProvider.GetRequiredService<CreateEmployeeUseCase>();

        // Act
        var result = await useCase.ExecuteAsync(new CreateEmployeeCommand("   ", StationRole.Waiter, CompanyId: seeded.CompanyId));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyName");

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.ResultFailuresCounterName &&
            Equals(m.Tags["usecase"], "CreateEmployee") &&
            Equals(m.Tags["error.code"], "Employee.EmptyName") &&
            Equals(m.Tags["error.category"], "Validation"));

        recordedActivities.Should().Contain(a =>
            a.OperationName == "UseCase.CreateEmployee" &&
            Equals(a.GetTagItem("virada5x2.tenant_id"), seeded.TenantId.ToString()) &&
            Equals(a.GetTagItem("virada5x2.result.is_success"), false) &&
            Equals(a.GetTagItem("virada5x2.error.code"), "Employee.EmptyName"));
    }

    public void Dispose() => _factory.Dispose();
}

