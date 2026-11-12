using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using Application.UseCases.Rosters;
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

public sealed partial class SharedRosterAndSwapIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;

    public SharedRosterAndSwapIntegrationTests()
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
    public async Task SharedRosterRoute_WhenAccessedAnonymouslyWithValidToken_ShouldRenderScopedTenantRosterOnly()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var seedA = await SeedTenantRosterAsync(tenantA, "Casa Alpha");
        await SeedTenantRosterAsync(tenantB, "Casa Beta Secreta");

        string tokenA;
        using (var scope = _factory.Services.CreateScope())
        {
            var shareSvc = scope.ServiceProvider.GetRequiredService<IRosterShareLinkService>();
            tokenA = shareSvc.CreateShareLink(tenantA, seedA.CompanyId, seedA.RosterId, seedA.WeekStart).Value.Token;
        }

        var anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await anonymousClient.GetAsync(
            $"/shared/roster?token={Uri.EscapeDataString(tokenA)}&employeeId={seedA.AnaId:D}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Casa Alpha");
        html.Should().Contain("Ana Garçom");
        html.Should().Contain("Folga 5x2");
        html.Should().NotContain("Casa Beta Secreta");
    }

    [Fact]
    public async Task SharedRosterRoute_WhenTokenTampered_ShouldReturnErrorWithoutLeakingTenantData()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        await SeedTenantRosterAsync(tenant, "Casa Protegida");

        var anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var response = await anonymousClient.GetAsync("/shared/roster?token=token-adulterado");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Link indisponível");
        html.Should().NotContain("Casa Protegida");
    }

    [Fact]
    public async Task EndToEndSwapFlow_WhenRequestedAndPeerAcceptedViaToken_ShouldNotifyManagerAndApproveAtomically()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var seed = await SeedTenantRosterAsync(tenantId, "Restaurante Fluxo");
        await SeedUserAsync(tenantId, "gestor-fluxo@casa.com.br", "Gestor Fluxo", "SenhaForte123", ApplicationRoles.Manager);

        string token;
        var notifiedTenants = new List<Guid>();
        using (var scope = _factory.Services.CreateScope())
        {
            var notificationSvc = scope.ServiceProvider.GetRequiredService<IShiftSwapNotificationService>();
            notificationSvc.OnTenantSwapsChanged += notifiedTenants.Add;

            var shareSvc = scope.ServiceProvider.GetRequiredService<IRosterShareLinkService>();
            token = shareSvc.CreateShareLink(tenantId, seed.CompanyId, seed.RosterId, seed.WeekStart).Value.Token;

            var optionsUseCase = scope.ServiceProvider.GetRequiredService<GetCompatibleSwapOptionsUseCase>();
            var reqUseCase = scope.ServiceProvider.GetRequiredService<RequestSharedShiftSwapUseCase>();
            var peerUseCase = scope.ServiceProvider.GetRequiredService<RespondSharedShiftSwapPeerUseCase>();

            var optionsResult = await optionsUseCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
                token, seed.AnaId, seed.AnaShiftId));
            optionsResult.IsSuccess.Should().BeTrue();
            var mutualOption = optionsResult.Value.Should().Contain(o => o.TargetEmployeeId == seed.BrunoId && o.IsMutualSwap).Subject;

            var reqResult = await reqUseCase.ExecuteAsync(new RequestSharedShiftSwapCommand(
                token, seed.AnaId, seed.AnaShiftId, mutualOption.TargetEmployeeId, mutualOption.TargetShiftId, "Troca de fim de semana"));
            reqResult.IsSuccess.Should().BeTrue();
            notifiedTenants.Should().BeEmpty();

            var peerResult = await peerUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
                token, reqResult.Value.SwapRequest.Id, seed.BrunoId, Accept: true));
            peerResult.IsSuccess.Should().BeTrue();
            notifiedTenants.Should().ContainSingle().Which.Should().Be(tenantId);

            notificationSvc.OnTenantSwapsChanged -= notifiedTenants.Add;
        }

        // Act 1: Gestor autenticado visualiza notificação no Dashboard e no Painel
        var managerClient = await CreateAuthenticatedClientAsync("gestor-fluxo@casa.com.br", "SenhaForte123");
        var dashboardHtml = WebUtility.HtmlDecode(await (await managerClient.GetAsync("/dashboard")).Content.ReadAsStringAsync());
        var rosterHtml = WebUtility.HtmlDecode(await (await managerClient.GetAsync($"/roster?week={seed.WeekStart:yyyy-MM-dd}")).Content.ReadAsStringAsync());

        // Assert 1
        dashboardHtml.Should().Contain("Trocas de Turno Aguardando Aprovação");
        dashboardHtml.Should().Contain("nav-pending-swaps-badge");
        rosterHtml.Should().Contain("Solicitações de Troca Pendentes");
        rosterHtml.Should().Contain("Aprovar em 1 Clique");

        // Act 2: Gestor aprova em 1 clique (simulando chamada autenticada do UseCase no contexto do tenant)
        using (var scope = _factory.Services.CreateScope())
        {
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(TenantClaimTypes.TenantId, tenantId.ToString())
                    ],
                    "ManagerApproveTest"))
            };

            try
            {
                var queueUseCase = scope.ServiceProvider.GetRequiredService<GetPendingShiftSwapsUseCase>();
                var approveUseCase = scope.ServiceProvider.GetRequiredService<ApproveShiftSwapUseCase>();
                var rosterRepo = scope.ServiceProvider.GetRequiredService<Domain.Repositories.IRosterRepository>();

                var queue = await queueUseCase.ExecuteAsync(new GetPendingShiftSwapsCommand());
                queue.IsSuccess.Should().BeTrue();
                queue.Value.PendingManagerApprovalCount.Should().Be(1);

                var swapId = queue.Value.Items.Single().SwapRequest.Id;
                var approveResult = await approveUseCase.ExecuteAsync(new ApproveShiftSwapCommand(swapId, "OK"));
                approveResult.IsSuccess.Should().BeTrue();

                var updatedRoster = await rosterRepo.GetByIdWithShiftsAsync(seed.RosterId);
                updatedRoster!.Shifts.Single(s => s.Id == seed.AnaShiftId).EmployeeId.Should().Be(seed.BrunoId);
                updatedRoster.Shifts.Single(s => s.Id == seed.BrunoShiftId).EmployeeId.Should().Be(seed.AnaId);
            }
            finally
            {
                httpContextAccessor.HttpContext = null;
            }
        }
    }

    [Fact]
    public async Task OneWayCoverageAndManagerReject_WhenExecutedEndToEnd_ShouldRespectCltAndRejectCleanly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var seed = await SeedTenantRosterAsync(tenantId, "Bistrô Cobertura");
        var carlaId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateAuthenticatedContext(tenantId);
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var carla = Employee.Create(tenantId, seed.CompanyId, "Carla Salão", StationRole.Waiter).Value;
                carlaId = carla.Id;
                db.Employees.Add(carla);
                await db.SaveChangesAsync();

                var shareSvc = scope.ServiceProvider.GetRequiredService<IRosterShareLinkService>();
                var token = shareSvc.CreateShareLink(tenantId, seed.CompanyId, seed.RosterId, seed.WeekStart).Value.Token;

                var reqUseCase = scope.ServiceProvider.GetRequiredService<RequestSharedShiftSwapUseCase>();
                var peerUseCase = scope.ServiceProvider.GetRequiredService<RespondSharedShiftSwapPeerUseCase>();
                var rejectUseCase = scope.ServiceProvider.GetRequiredService<RejectShiftSwapUseCase>();
                var approveUseCase = scope.ServiceProvider.GetRequiredService<ApproveShiftSwapUseCase>();
                var rosterRepo = scope.ServiceProvider.GetRequiredService<Domain.Repositories.IRosterRepository>();

                // 1) Cobertura unilateral Ana -> Carla (aprovada pelo colega e depois recusada pelo gestor)
                var reqReject = await reqUseCase.ExecuteAsync(new RequestSharedShiftSwapCommand(
                    token, seed.AnaId, seed.AnaShiftId, carlaId, TargetShiftId: null, "Folga extra"));
                reqReject.IsSuccess.Should().BeTrue();

                (await peerUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
                    token, reqReject.Value.SwapRequest.Id, carlaId, Accept: true))).IsSuccess.Should().BeTrue();

                var rejected = await rejectUseCase.ExecuteAsync(new RejectShiftSwapCommand(
                    reqReject.Value.SwapRequest.Id, "Cobertura não autorizada"));
                rejected.IsSuccess.Should().BeTrue();
                rejected.Value.Status.Should().Be(ShiftSwapStatus.Rejected);

                var rosterAfterReject = await rosterRepo.GetByIdWithShiftsAsync(seed.RosterId);
                rosterAfterReject!.Shifts.Single(s => s.Id == seed.AnaShiftId).EmployeeId.Should().Be(seed.AnaId);

                // 2) Nova cobertura unilateral Ana -> Carla, aprovada em 1 clique pelo gestor
                var reqApprove = await reqUseCase.ExecuteAsync(new RequestSharedShiftSwapCommand(
                    token, seed.AnaId, seed.AnaShiftId, carlaId, TargetShiftId: null, "Consulta médica"));
                reqApprove.IsSuccess.Should().BeTrue();

                (await peerUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
                    token, reqApprove.Value.SwapRequest.Id, carlaId, Accept: true))).IsSuccess.Should().BeTrue();

                var approved = await approveUseCase.ExecuteAsync(new ApproveShiftSwapCommand(
                    reqApprove.Value.SwapRequest.Id, "Aprovado em 1 clique"));
                approved.IsSuccess.Should().BeTrue();
                approved.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.Approved);

                var rosterAfterApprove = await rosterRepo.GetByIdWithShiftsAsync(seed.RosterId);
                rosterAfterApprove!.Shifts.Single(s => s.Id == seed.AnaShiftId).EmployeeId.Should().Be(carlaId);
            }
            finally
            {
                httpContextAccessor.HttpContext = null;
            }
        }
    }

    [Fact]
    public async Task ManagerSwapQueueAndActions_WhenAccessedByAnotherTenant_ShouldIsolateStrictly()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var seedA = await SeedTenantRosterAsync(tenantA, "Casa Isolada A");
        await SeedTenantRosterAsync(tenantB, "Casa Isolada B");

        Guid swapAId;
        using (var scope = _factory.Services.CreateScope())
        {
            var shareSvc = scope.ServiceProvider.GetRequiredService<IRosterShareLinkService>();
            var tokenA = shareSvc.CreateShareLink(tenantA, seedA.CompanyId, seedA.RosterId, seedA.WeekStart).Value.Token;

            var reqUseCase = scope.ServiceProvider.GetRequiredService<RequestSharedShiftSwapUseCase>();
            var peerUseCase = scope.ServiceProvider.GetRequiredService<RespondSharedShiftSwapPeerUseCase>();

            var req = await reqUseCase.ExecuteAsync(new RequestSharedShiftSwapCommand(
                tokenA, seedA.AnaId, seedA.AnaShiftId, seedA.BrunoId, seedA.BrunoShiftId));
            req.IsSuccess.Should().BeTrue();
            swapAId = req.Value.SwapRequest.Id;

            (await peerUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
                tokenA, swapAId, seedA.BrunoId, Accept: true))).IsSuccess.Should().BeTrue();
        }

        // Act: Gestor do Tenant B consulta fila e tenta aprovar/recusar solicitação do Tenant A
        using (var scope = _factory.Services.CreateScope())
        {
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateAuthenticatedContext(tenantB);
            try
            {
                var queueUseCase = scope.ServiceProvider.GetRequiredService<GetPendingShiftSwapsUseCase>();
                var approveUseCase = scope.ServiceProvider.GetRequiredService<ApproveShiftSwapUseCase>();
                var rejectUseCase = scope.ServiceProvider.GetRequiredService<RejectShiftSwapUseCase>();

                var queueB = await queueUseCase.ExecuteAsync(new GetPendingShiftSwapsCommand());
                var approveAttempt = await approveUseCase.ExecuteAsync(new ApproveShiftSwapCommand(swapAId));
                var rejectAttempt = await rejectUseCase.ExecuteAsync(new RejectShiftSwapCommand(swapAId));

                // Assert
                queueB.IsSuccess.Should().BeTrue();
                queueB.Value.PendingManagerApprovalCount.Should().Be(0);
                queueB.Value.Items.Should().BeEmpty();

                approveAttempt.IsFailure.Should().BeTrue();
                approveAttempt.Error.Code.Should().Be("ShiftSwap.NotFound");

                rejectAttempt.IsFailure.Should().BeTrue();
                rejectAttempt.Error.Code.Should().Be("ShiftSwap.NotFound");
            }
            finally
            {
                httpContextAccessor.HttpContext = null;
            }
        }
    }

    private static DefaultHttpContext CreateAuthenticatedContext(Guid tenantId) =>
        new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(TenantClaimTypes.TenantId, tenantId.ToString())
                ],
                "ManagerTest"))
        };

    private async Task<(Guid CompanyId, Guid RosterId, DateOnly WeekStart, Guid AnaId, Guid BrunoId, Guid AnaShiftId, Guid BrunoShiftId)> SeedTenantRosterAsync(
        Guid tenantId,
        string companyName)
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
                companyName,
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(23, 0),
                ScheduleModel.RotatingDaysOff).Value;
            var ana = Employee.Create(tenantId, company.Id, "Ana Garçom", StationRole.Waiter).Value;
            var bruno = Employee.Create(tenantId, company.Id, "Bruno Garçom", StationRole.Waiter).Value;
            var weekStart = new DateOnly(2026, 11, 16);
            var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
            var sAna = roster.AssignShift(ana, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
            var sBruno = roster.AssignShift(bruno, weekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
            roster.Publish().IsSuccess.Should().BeTrue();

            db.Companies.Add(company);
            db.Employees.AddRange(ana, bruno);
            db.Rosters.Add(roster);
            await db.SaveChangesAsync();

            return (company.Id, roster.Id, weekStart, ana.Id, bruno.Id, sAna.Id, sBruno.Id);
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

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiForgeryTokenRegex();
}

