using System.Net;
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WebApp.Blazor.UnitTests.Integration;

public sealed partial class IdentityIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public IdentityIntegrationTests()
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

    [Fact]
    public void ApplicationCookie_WhenConfigured_ShouldEnforceHttpOnlySlidingExpirationAndAccountPaths()
    {
        // Arrange
        var optionsMonitor = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        // Act
        var cookieOptions = optionsMonitor.Get(IdentityConstants.ApplicationScheme);

        // Assert
        cookieOptions.Cookie.Name.Should().Be("Scale5x2.Auth");
        cookieOptions.Cookie.HttpOnly.Should().BeTrue();
        cookieOptions.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookieOptions.SlidingExpiration.Should().BeTrue();
        cookieOptions.ExpireTimeSpan.Should().Be(TimeSpan.FromHours(8));
        cookieOptions.LoginPath.Value.Should().Be("/Account/Login");
        cookieOptions.LogoutPath.Value.Should().Be("/Account/Logout");
        cookieOptions.AccessDeniedPath.Value.Should().Be("/Account/AccessDenied");
    }

    [Fact]
    public async Task LoginAndLogout_WhenCredentialsValid_ShouldIssueSessionCookieAndClearOnLogout()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "gestor@restaurante.com.br", "Gestor 5x2", "SenhaForte123", ApplicationRoles.Manager);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var getLoginResponse = await client.GetAsync("/Account/Login");
        getLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginHtml = await getLoginResponse.Content.ReadAsStringAsync();
        var verificationToken = ExtractRequestVerificationToken(loginHtml);

        // Act 1: POST /Account/Login com credenciais válidas
        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = verificationToken,
            ["Input.Email"] = "gestor@restaurante.com.br",
            ["Input.Password"] = "SenhaForte123"
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginContent);

        // Assert 1: Cookie emitido e redirecionamento para /
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        GetLocationPath(loginResponse.Headers.Location).Should().Be("/");
        loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        setCookies!.Should().Contain(c => c.StartsWith("Scale5x2.Auth=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        // Act 2: Requisição autenticada ao endpoint de verificação de sessão / logout
        var homeResponse = await client.GetAsync("/");
        homeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var postLoginHtml = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var logoutAntiforgery = ExtractRequestVerificationToken(postLoginHtml);

        using var logoutContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutAntiforgery,
            ["returnUrl"] = "/Account/Login"
        });

        var logoutResponse = await client.PostAsync("/Account/Logout", logoutContent);

        // Assert 2: Redireciona e expira o cookie
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logoutResponse.Headers.Location?.ToString().Should().Be("/Account/Login");
        logoutResponse.Headers.TryGetValues("Set-Cookie", out var logoutCookies).Should().BeTrue();
        logoutCookies!.Should().Contain(c => c.StartsWith("Scale5x2.Auth=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WhenPasswordInvalid_ShouldNotIssueSessionCookie()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "dono@restaurante.com.br", "Dono 5x2", "SenhaForte123", ApplicationRoles.Owner);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginHtml = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var verificationToken = ExtractRequestVerificationToken(loginHtml);

        // Act
        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = verificationToken,
            ["Input.Email"] = "dono@restaurante.com.br",
            ["Input.Password"] = "SenhaErrada999"
        });

        var response = await client.PostAsync("/Account/Login", loginContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            setCookies.Should().NotContain(c => c.StartsWith("Scale5x2.Auth=", StringComparison.Ordinal) && !c.StartsWith("Scale5x2.Auth=;", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Login_WhenUserIsInactive_ShouldNotIssueSessionCookie()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(tenantId, "inativo@restaurante.com.br", "Inativo", "SenhaForte123", ApplicationRoles.Employee, isActive: false);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginHtml = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var verificationToken = ExtractRequestVerificationToken(loginHtml);

        // Act
        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = verificationToken,
            ["Input.Email"] = "inativo@restaurante.com.br",
            ["Input.Password"] = "SenhaForte123"
        });

        var response = await client.PostAsync("/Account/Login", loginContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            setCookies.Should().NotContain(c => c.StartsWith("Scale5x2.Auth=", StringComparison.Ordinal) && !c.StartsWith("Scale5x2.Auth=;", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task CurrentUserService_WhenResolvedInScopedRequestWithAuthenticatedCookie_ShouldIsolateTenantData()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedCompanyAndUserAsync(tenantA, "Casa Alfa", "alfa@casa.com.br", "SenhaAlfa123");
        await SeedCompanyAndUserAsync(tenantB, "Casa Beta", "beta@casa.com.br", "SenhaBeta123");

        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

        var userA = (await userManager.FindByEmailAsync("alfa@casa.com.br"))!;
        var principalA = await principalFactory.CreateAsync(userA);
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = principalA };

        var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Act
        var visibleCompanies = await db.Companies.ToListAsync();

        // Assert
        currentUserService.IsAuthenticated.Should().BeTrue();
        currentUserService.TenantId.Should().Be(tenantA);
        visibleCompanies.Should().ContainSingle(c => c.TenantId == tenantA && c.Name == "Casa Alfa");
    }

    private async Task SeedUserAsync(Guid tenantId, string email, string fullName, string password, string roleName, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new ApplicationRole(roleName));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create(tenantId, email, fullName).Value;
        user.IsActive = isActive;
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();
    }

    private async Task SeedCompanyAndUserAsync(Guid tenantId, string companyName, string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var user = ApplicationUser.Create(tenantId, email, companyName).Value;
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = await principalFactory.CreateAsync(user) };

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = Company.Create(
            tenantId,
            companyName,
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        db.Companies.Add(company);
        await db.SaveChangesAsync();
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

