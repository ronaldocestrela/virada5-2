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

public sealed partial class FinancialSimulatorIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public FinancialSimulatorIntegrationTests()
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
    [InlineData("/financial")]
    [InlineData("/financial/simulator")]
    public async Task FinancialRoutes_WhenUnauthenticated_ShouldRedirectToLogin(string route)
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
    [InlineData("/financial")]
    [InlineData("/financial/simulator")]
    public async Task FinancialRoutes_WhenAuthenticatedAsEmployee_ShouldDenyAccess(string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "colaborador-financeiro@casa.com.br", "Colaborador", "SenhaForte123", ApplicationRoles.Employee);
        var client = await CreateAuthenticatedClientAsync("colaborador-financeiro@casa.com.br", "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        GetLocationPath(response.Headers.Location).Should().Be("/Account/AccessDenied");
    }

    [Theory]
    [InlineData(ApplicationRoles.Owner, "/financial")]
    [InlineData(ApplicationRoles.Manager, "/financial/simulator")]
    public async Task FinancialRoutes_WhenAuthenticatedAsOwnerOrManager_ShouldReturnOk(string role, string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = $"{role.ToLowerInvariant()}-financeiro@casa.com.br";
        await SeedUserAsync(tenantId, email, $"Usuário {role}", "SenhaForte123", role);
        var client = await CreateAuthenticatedClientAsync(email, "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Simulador de Impacto Financeiro");
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

