using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WebApp.Blazor.UnitTests.Integration;

public sealed partial class RosterPanelIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public RosterPanelIntegrationTests()
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
    [InlineData("/roster")]
    [InlineData("/roster/panel")]
    public async Task RosterRoutes_WhenUnauthenticated_ShouldRedirectToLogin(string route)
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
    [InlineData("/roster")]
    [InlineData("/roster/panel")]
    public async Task RosterRoutes_WhenAuthenticatedAsEmployee_ShouldDenyAccess(string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "colaborador-escala@casa.com.br", "Colaborador", "SenhaForte123", ApplicationRoles.Employee);
        var client = await CreateAuthenticatedClientAsync("colaborador-escala@casa.com.br", "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        GetLocationPath(response.Headers.Location).Should().Be("/Account/AccessDenied");
    }

    [Theory]
    [InlineData(ApplicationRoles.Owner, "/roster")]
    [InlineData(ApplicationRoles.Manager, "/roster/panel")]
    public async Task RosterRoutes_WhenAuthenticatedAsOwnerOrManager_ShouldReturnOk(string role, string route)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var email = $"{role.ToLowerInvariant()}-escala@casa.com.br";
        await SeedUserAsync(tenantId, email, $"Usuário {role}", "SenhaForte123", role);
        var client = await CreateAuthenticatedClientAsync(email, "SenhaForte123");

        // Act
        var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Painel de Escala Interativo");
    }

    [Fact]
    public async Task ExportEndpoint_WhenRequestedByOwningTenantVsAnotherTenant_ShouldEnforceTenantIsolation()
    {
        // Arrange
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await SeedUserAsync(ownerTenantId, "owner-export@casa.com.br", "Owner", "SenhaForte123", ApplicationRoles.Owner);
        await SeedUserAsync(otherTenantId, "other-export@casa.com.br", "Other", "SenhaForte123", ApplicationRoles.Owner);

        var rosterId = await SeedRosterAsync(ownerTenantId);

        var ownerClient = await CreateAuthenticatedClientAsync("owner-export@casa.com.br", "SenhaForte123");
        var otherClient = await CreateAuthenticatedClientAsync("other-export@casa.com.br", "SenhaForte123");

        // Act
        var ownerResponse = await ownerClient.GetAsync($"/rosters/{rosterId}/export");
        var otherResponse = await otherClient.GetAsync($"/rosters/{rosterId}/export");

        // Assert
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ownerResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        var csv = await ownerResponse.Content.ReadAsStringAsync();
        csv.Should().Contain("Ana Cozinheira");

        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> SeedRosterAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(TenantClaimTypes.TenantId, tenantId.ToString())
                ],
                "SeedTest"))
        };

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var company = Company.Create(
                tenantId,
                "Restaurante Export",
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(23, 0),
                ScheduleModel.RotatingDaysOff).Value;
            var employee = Employee.Create(tenantId, company.Id, "Ana Cozinheira", StationRole.Kitchen).Value;
            var weekStart = new DateOnly(2026, 11, 16);
            var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
            roster.AssignShift(employee, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);

            db.Companies.Add(company);
            db.Employees.Add(employee);
            db.Rosters.Add(roster);
            await db.SaveChangesAsync();

            return roster.Id;
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }
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

    private async Task SeedUserAsync(
        Guid tenantId,
        string email,
        string fullName,
        string password,
        string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new ApplicationRole(role));
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName,
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        createResult.Succeeded.Should().BeTrue();

        var roleResult = await userManager.AddToRoleAsync(user, role);
        roleResult.Succeeded.Should().BeTrue();
    }

    private static string ExtractRequestVerificationToken(string html)
    {
        var match = AntiForgeryTokenRegex().Match(html);
        match.Success.Should().BeTrue("a página de login deve renderizar o token antifalsificação");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string? GetLocationPath(Uri? location) =>
        location is null ? null : (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0]);

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiForgeryTokenRegex();
}

