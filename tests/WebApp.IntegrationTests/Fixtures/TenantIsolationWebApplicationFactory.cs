using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
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

namespace WebApp.IntegrationTests.Fixtures;

public sealed partial class TenantIsolationWebApplicationFactory : IDisposable
{
    private readonly SqliteConnection? _sqliteConnection;
    private readonly string? _sqlServerConnection;
    private readonly WebApplicationFactory<Program> _factory;

    public TenantIsolationWebApplicationFactory()
    {
        _sqlServerConnection = Environment.GetEnvironmentVariable("INTEGRATION_TEST_SQLSERVER_CONNECTION");
        if (string.IsNullOrWhiteSpace(_sqlServerConnection))
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            _sqliteConnection.Open();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();

                    if (!string.IsNullOrWhiteSpace(_sqlServerConnection))
                    {
                        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_sqlServerConnection));
                    }
                    else
                    {
                        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_sqliteConnection!));
                    }
                });
            });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateAnonymousClient(bool allowAutoRedirect = false) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = CreateAnonymousClient(allowAutoRedirect: false);
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

    public async Task<SeededTenantData> SeedFullTenantAsync(
        string companyName,
        string ownerEmail,
        string password = "SenhaForte123")
    {
        var tenantId = Guid.NewGuid();
        var userId = await SeedUserAsync(tenantId, ownerEmail, $"Owner {companyName}", password, ApplicationRoles.Owner);

        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = CreateAuthenticatedHttpContext(tenantId, userId);

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var company = Company.Create(
                tenantId,
                companyName,
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(23, 0),
                ScheduleModel.RotatingDaysOff).Value;

            var demandSlot = DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 20, 120m).Value;
            demandSlot.EvaluateRelativeDemand(120m);

            var emp1 = Employee.Create(tenantId, company.Id, $"Ana {companyName}", StationRole.Waiter).Value;
            var emp2 = Employee.Create(tenantId, company.Id, $"Bruno {companyName}", StationRole.Waiter).Value;

            var weekStart = new DateOnly(2026, 11, 16);
            var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
            var shift1 = roster.AssignShift(emp1, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
            var shift2 = roster.AssignShift(emp2, weekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
            roster.Publish().IsSuccess.Should().BeTrue();

            var swap = ShiftSwapRequest.Create(
                tenantId,
                company.Id,
                roster.Id,
                emp1.Id,
                shift1.Id,
                emp2.Id,
                shift2.Id,
                "Troca isolada",
                requirePeerApproval: true).Value;
            swap.AcceptByPeer(emp2.Id).IsSuccess.Should().BeTrue();

            db.Companies.Add(company);
            db.DemandSlots.Add(demandSlot);
            db.Employees.AddRange(emp1, emp2);
            db.Rosters.Add(roster);
            db.ShiftSwapRequests.Add(swap);
            await db.SaveChangesAsync();

            var shareSvc = scope.ServiceProvider.GetRequiredService<IRosterShareLinkService>();
            var shareToken = shareSvc.CreateShareLink(tenantId, company.Id, roster.Id, weekStart).Value.Token;

            return new SeededTenantData(
                tenantId,
                userId,
                ownerEmail,
                company.Id,
                demandSlot.Id,
                emp1.Id,
                emp2.Id,
                roster.Id,
                shift1.Id,
                shift2.Id,
                swap.Id,
                weekStart,
                shareToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }
    }

    public async Task<Guid> SeedUserAsync(
        Guid tenantId,
        string email,
        string fullName,
        string password,
        string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new ApplicationRole(roleName));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create(tenantId, email, fullName).Value;
        user.EmailConfirmed = true;

        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        return user.Id;
    }

    public async Task ExecuteAsTenantAsync(Guid tenantId, Guid? userId, Func<IServiceProvider, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = tenantId == Guid.Empty
            ? new DefaultHttpContext()
            : CreateAuthenticatedHttpContext(tenantId, userId ?? Guid.NewGuid());

        try
        {
            await action(scope.ServiceProvider);
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }
    }

    public static DefaultHttpContext CreateAuthenticatedHttpContext(Guid tenantId, Guid userId) =>
        new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(TenantClaimTypes.TenantId, tenantId.ToString())
                ],
                "IntegrationTest"))
        };

    public static string ExtractRequestVerificationToken(string html)
    {
        var match = RequestVerificationTokenRegex().Match(html);
        match.Success.Should().BeTrue("o formulário SSR deve renderizar __RequestVerificationToken");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RequestVerificationTokenRegex();

    public void Dispose()
    {
        _factory.Dispose();
        _sqliteConnection?.Dispose();
    }
}

public sealed record SeededTenantData(
    Guid TenantId,
    Guid OwnerUserId,
    string OwnerEmail,
    Guid CompanyId,
    Guid DemandSlotId,
    Guid Employee1Id,
    Guid Employee2Id,
    Guid RosterId,
    Guid Shift1Id,
    Guid Shift2Id,
    Guid SwapRequestId,
    DateOnly WeekStart,
    string ShareToken);

