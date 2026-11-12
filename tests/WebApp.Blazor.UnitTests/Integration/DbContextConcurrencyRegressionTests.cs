using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using Bunit;
using Infrastructure.Data;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebApp.Blazor.Components.Layout;
using WebApp.Blazor.Components.Pages.Company;
using WebApp.Blazor.Extensions;

namespace WebApp.Blazor.UnitTests.Integration;

public sealed partial class DbContextConcurrencyRegressionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public DbContextConcurrencyRegressionTests()
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
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlite(_connection)
                               .AddInterceptors(new AsyncDelayDbCommandInterceptor(TimeSpan.FromMilliseconds(35))));
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task OnboardingRoute_WithAsyncDatabaseQueries_ShouldRenderWithoutConcurrentDbContextError()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (_, email) = await SeedOwnerAsync(tenantId, "dono-concurrency@casa.com.br");
        var client = await CreateAuthenticatedClientAsync(email, "SenhaForte123");

        // Act
        var response = await client.GetAsync("/onboarding");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Onboarding da Casa");
    }

    [Fact]
    public async Task SingleCircuit_WhenManagerBadgeAndCompanySettingsRunConcurrentlyDuringOnboarding_ShouldNotThrowDbContextConcurrencyException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (userId, email) = await SeedOwnerAsync(tenantId, "dono-circuito@casa.com.br");
        var principal = CreateOwnerPrincipal(userId, tenantId, email);

        using var circuitCtx = new BunitContext();
        circuitCtx.Services.AddLogging();
        circuitCtx.Services.AddDataProtection();
        circuitCtx.Services.AddInfrastructureData(connectionString: null);
        circuitCtx.Services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        circuitCtx.Services.RemoveAll<DbContextOptions<AppDbContext>>();
        circuitCtx.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(_connection)
                   .AddInterceptors(new AsyncDelayDbCommandInterceptor(TimeSpan.FromMilliseconds(35))));
        circuitCtx.Services.AddApplicationServices();
        circuitCtx.Services.AddBlazorIdentityServices();

        var authStateProvider = circuitCtx.Services.GetRequiredService<AuthenticationStateProvider>();
        if (authStateProvider is IHostEnvironmentAuthenticationStateProvider hostAuth)
        {
            hostAuth.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
        }

        // Act: inicializa ManagerPendingSwapsBadge e CompanySettings concorrentemente no mesmo circuito Blazor
        var renderBadgeTask = circuitCtx.Renderer.Dispatcher.InvokeAsync(() => circuitCtx.Render<ManagerPendingSwapsBadge>());
        var renderSettingsTask = circuitCtx.Renderer.Dispatcher.InvokeAsync(() => circuitCtx.Render<CompanySettings>());

        await Task.WhenAll(renderBadgeTask, renderSettingsTask);

        var settingsCut = await renderSettingsTask;
        settingsCut.WaitForAssertion(() =>
            settingsCut.Find("button[type=\"submit\"]").HasAttribute("disabled").Should().BeFalse());

        settingsCut.Find("#company-name").Change("Bistrô Concorrência");

        // Simula conclusão do onboarding em paralelo com LocationChanged e OnTenantSwapsChanged no badge
        var nav = circuitCtx.Services.GetRequiredService<NavigationManager>();
        var notificationService = circuitCtx.Services.GetRequiredService<IShiftSwapNotificationService>();

        var submitTask = settingsCut.InvokeAsync(() => settingsCut.Find("form").SubmitAsync());
        var navTask = circuitCtx.Renderer.Dispatcher.InvokeAsync(() => nav.NavigateTo("/demand"));
        var notifyTask = circuitCtx.Renderer.Dispatcher.InvokeAsync(() => notificationService.NotifyTenantSwapsChanged(tenantId));

        await Task.WhenAll(submitTask, navTask, notifyTask);

        // Assert
        settingsCut.WaitForAssertion(() =>
            settingsCut.Find("h1").TextContent.Should().Contain("Configurações da Casa"));
    }

    private static ClaimsPrincipal CreateOwnerPrincipal(Guid userId, Guid tenantId, string email)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, ApplicationRoles.Owner),
            new Claim(TenantClaimTypes.TenantId, tenantId.ToString("D"))
        ],
        authenticationType: "Identity.Application");

        return new ClaimsPrincipal(identity);
    }

    private async Task<(Guid UserId, string Email)> SeedOwnerAsync(Guid tenantId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(ApplicationRoles.Owner))
        {
            await roleManager.CreateAsync(new ApplicationRole(ApplicationRoles.Owner));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create(tenantId, email, "Proprietário Teste").Value;
        (await userManager.CreateAsync(user, "SenhaForte123")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, ApplicationRoles.Owner)).Succeeded.Should().BeTrue();
        return (user.Id, email);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginHtml = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var match = RequestVerificationTokenRegex().Match(loginHtml);
        match.Success.Should().BeTrue();

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = match.Groups[1].Value,
            ["Input.Email"] = email,
            ["Input.Password"] = password
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginContent);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RequestVerificationTokenRegex();

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    private sealed class AsyncDelayDbCommandInterceptor(TimeSpan delay) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return result;
        }
    }
}
