using System.Net;
using System.Text.Json;
using Application.Common.Interfaces;
using Domain.Common;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
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
        var client = _factory.CreateAnonymousClient(allowAutoRedirect: false);
        var getHtml = await (await client.GetAsync("/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=lancamento")).Content.ReadAsStringAsync();
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
        var postResponse = await client.PostAsync("/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=lancamento", form);

        // Assert
        postResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        postResponse.Headers.Location?.ToString().Should().Contain("/onboarding");

        var onboardingResponse = await client.GetAsync("/onboarding");
        onboardingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await userManager.FindByEmailAsync("marina-ebook@bistro5x2.com.br");
        created.Should().NotBeNull();
        created!.TenantId.Should().NotBe(Guid.Empty);
        (await userManager.IsInRoleAsync(created, ApplicationRoles.Owner)).Should().BeTrue();
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

    public void Dispose() => _factory.Dispose();
}

