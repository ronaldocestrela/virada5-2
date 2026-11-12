using System.Net;
using System.Text.RegularExpressions;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WebApp.Blazor.UnitTests.Integration;

public sealed partial class CompanyOnboardingIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public CompanyOnboardingIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    [Theory]
    [InlineData("/onboarding")]
    [InlineData("/settings/company")]
    public async Task CompanyRoutes_WhenUnauthenticated_ShouldRedirectToLogin(string route)
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        GetLocationPath(response.Headers.Location).Should().Be("/Account/Login");
    }

    [Theory]
    [InlineData("/onboarding")]
    [InlineData("/settings/company")]
    public async Task CompanyRoutes_WhenAuthenticatedAsEmployee_ShouldDenyAccess(string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "colaborador@casa.com.br", "Colaborador", "SenhaForte123", ApplicationRoles.Employee);
        var client = await CreateAuthenticatedClientAsync("colaborador@casa.com.br", "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        GetLocationPath(response.Headers.Location).Should().Be("/Account/AccessDenied");
    }

    [Theory]
    [InlineData(ApplicationRoles.Owner, "/onboarding")]
    [InlineData(ApplicationRoles.Manager, "/settings/company")]
    public async Task CompanyRoutes_WhenAuthenticatedAsOwnerOrManager_ShouldReturnOk(string role, string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = $"{role.ToLowerInvariant()}@casa.com.br";
        await SeedUserAsync(tenantId, email, $"Usuário {role}", "SenhaForte123", role);
        var client = await CreateAuthenticatedClientAsync(email, "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Configuração");
    }

    [Theory]
    [InlineData("/onboarding")]
    [InlineData("/settings/company")]
    public async Task OnboardingForm_WhenPrerenderedAndSubmittedViaPost_ShouldIncludeFormHandlerNameAndPersistCompany(string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "dono-form@casa.com.br", "Dono Form", "SenhaForte123", ApplicationRoles.Owner);
        var client = await CreateAuthenticatedClientAsync("dono-form@casa.com.br", "SenhaForte123");

        var getResponse = await client.GetAsync(route);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawHtml = await getResponse.Content.ReadAsStringAsync();
        rawHtml.Should().Contain("name=\"_handler\" value=\"company-settings\"");
        rawHtml.Should().NotContain("<button type=\"submit\" class=\"app-btn app-btn--primary app-btn--lg\" disabled");

        var verificationToken = ExtractRequestVerificationToken(rawHtml);
        using var formContent = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("_handler", "company-settings"),
            new KeyValuePair<string, string>("__RequestVerificationToken", verificationToken),
            new KeyValuePair<string, string>("Form.Name", "Bistrô SSR Teste"),
            new KeyValuePair<string, string>("Form.BusinessType", "Restaurant"),
            new KeyValuePair<string, string>("Form.OpeningTimeText", "11:00"),
            new KeyValuePair<string, string>("Form.ClosingTimeText", "23:00"),
            new KeyValuePair<string, string>("Form.ScheduleModel", "RotatingDaysOff")
        ]);

        // Act
        var postResponse = await client.PostAsync(route, formContent);

        // Assert
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var postHtml = WebUtility.HtmlDecode(await postResponse.Content.ReadAsStringAsync());
        postHtml.Should().NotContain("does not specify which form is being submitted");
        postHtml.Should().Contain("Configurações da Casa");
        postHtml.Should().Contain("Bistrô SSR Teste");
    }

    [Fact]
    public async Task OnboardingRoute_WhenPostedWithFixedClosureOrUnboundPayload_ShouldNotThrowNullReferenceException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "dono-nre@casa.com.br", "Dono NRE", "SenhaForte123", ApplicationRoles.Owner);
        var client = await CreateAuthenticatedClientAsync("dono-nre@casa.com.br", "SenhaForte123");

        var getResponse = await client.GetAsync("/onboarding");
        var rawHtml = await getResponse.Content.ReadAsStringAsync();
        var verificationToken = ExtractRequestVerificationToken(rawHtml);

        // Act 1: POST sem chaves Form.* (simula SupplyParameterFromForm fornecendo null)
        using var emptyForm = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("_handler", "company-settings"),
            new KeyValuePair<string, string>("__RequestVerificationToken", verificationToken)
        ]);
        var emptyPostResponse = await client.PostAsync("/onboarding", emptyForm);

        // Assert 1
        emptyPostResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act 2: POST com FixedClosure e SelectedClosedDays preenchidos
        using var fixedForm = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("_handler", "company-settings"),
            new KeyValuePair<string, string>("__RequestVerificationToken", verificationToken),
            new KeyValuePair<string, string>("Form.Name", "Bistrô Fechamento Fixo"),
            new KeyValuePair<string, string>("Form.BusinessType", "Restaurant"),
            new KeyValuePair<string, string>("Form.OpeningTimeText", "11:00"),
            new KeyValuePair<string, string>("Form.ClosingTimeText", "23:00"),
            new KeyValuePair<string, string>("Form.ScheduleModel", "FixedClosure"),
            new KeyValuePair<string, string>("Form.SelectedClosedDays", "Monday")
        ]);
        var fixedPostResponse = await client.PostAsync("/onboarding", fixedForm);

        // Assert 2
        fixedPostResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fixedHtml = WebUtility.HtmlDecode(await fixedPostResponse.Content.ReadAsStringAsync());
        fixedHtml.Should().Contain("Configurações da Casa");
        fixedHtml.Should().Contain("Bistrô Fechamento Fixo");
    }

    [Fact]
    public async Task DashboardRoute_WhenAuthenticatedAsOwnerWithoutCompany_ShouldDisplayOnboardingCallToAction()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "dono-novo@casa.com.br", "Dono Novo", "SenhaForte123", ApplicationRoles.Owner);
        var client = await CreateAuthenticatedClientAsync("dono-novo@casa.com.br", "SenhaForte123");

        // Act
        var response = await client.GetAsync("/dashboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Complete o Onboarding da Casa");
        html.Should().Contain("/onboarding");
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginHtml = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var verificationToken = ExtractRequestVerificationToken(loginHtml);

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = verificationToken,
            ["Input.Email"] = email,
            ["Input.Password"] = password
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginContent);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private async Task SeedUserAsync(Guid tenantId, string email, string fullName, string password, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new ApplicationRole(roleName));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create(tenantId, email, fullName).Value;
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();
    }

    private static string? GetLocationPath(Uri? location) =>
        location is null
            ? null
            : location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;

    private static string ExtractRequestVerificationToken(string html)
    {
        var match = RequestVerificationTokenRegex().Match(html);
        match.Success.Should().BeTrue("o formulário SSR deve conter __RequestVerificationToken");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RequestVerificationTokenRegex();

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }
}

