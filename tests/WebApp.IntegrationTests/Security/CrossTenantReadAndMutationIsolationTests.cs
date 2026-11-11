using System.Net;
using Application.DTOs.Companies;
using Application.DTOs.Demand;
using Application.DTOs.Employees;
using Application.DTOs.Financial;
using Application.UseCases.Companies;
using Application.UseCases.Demand;
using Application.UseCases.Employees;
using Application.UseCases.Financial;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.IntegrationTests.Fixtures;

namespace WebApp.IntegrationTests.Security;

public sealed class CrossTenantReadAndMutationIsolationTests : IDisposable
{
    private readonly TenantIsolationWebApplicationFactory _factory = new();

    [Fact]
    public async Task AllTenantEntities_WhenQueriedAcrossTenantsOrUnauthenticated_ShouldNeverLeakOtherTenantData()
    {
        // Arrange
        var tenantA = await _factory.SeedFullTenantAsync("Casa Alfa", "owner-a@alfa.com.br");
        var tenantB = await _factory.SeedFullTenantAsync("Casa Beta", "owner-b@beta.com.br");

        // Act & Assert 1: Tenant B nunca enxerga entidades de Tenant A no AppDbContext nem nos Repositórios
        await _factory.ExecuteAsTenantAsync(tenantB.TenantId, tenantB.OwnerUserId, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            (await db.Companies.ToListAsync()).Should().OnlyContain(c => c.TenantId == tenantB.TenantId && c.Id == tenantB.CompanyId);
            (await db.DemandSlots.ToListAsync()).Should().OnlyContain(d => d.TenantId == tenantB.TenantId);
            (await db.Employees.ToListAsync()).Should().OnlyContain(e => e.TenantId == tenantB.TenantId);
            (await db.Rosters.ToListAsync()).Should().OnlyContain(r => r.TenantId == tenantB.TenantId);
            (await db.Shifts.ToListAsync()).Should().OnlyContain(s => s.TenantId == tenantB.TenantId);
            (await db.ShiftSwapRequests.ToListAsync()).Should().OnlyContain(r => r.TenantId == tenantB.TenantId);

            var companyRepo = sp.GetRequiredService<ICompanyRepository>();
            var demandRepo = sp.GetRequiredService<IDemandSlotRepository>();
            var employeeRepo = sp.GetRequiredService<IEmployeeRepository>();
            var rosterRepo = sp.GetRequiredService<IRosterRepository>();
            var swapRepo = sp.GetRequiredService<IShiftSwapRequestRepository>();

            (await companyRepo.GetByIdAsync(tenantA.CompanyId)).Should().BeNull();
            (await demandRepo.GetByIdAsync(tenantA.DemandSlotId)).Should().BeNull();
            (await demandRepo.GetByCompanyAsync(tenantA.CompanyId)).Should().BeEmpty();
            (await employeeRepo.GetByIdAsync(tenantA.Employee1Id)).Should().BeNull();
            (await employeeRepo.GetByCompanyAsync(tenantA.CompanyId)).Should().BeEmpty();
            (await rosterRepo.GetByIdWithShiftsAsync(tenantA.RosterId)).Should().BeNull();
            (await swapRepo.GetByIdAsync(tenantA.SwapRequestId)).Should().BeNull();
            (await swapRepo.GetPendingByCompanyAsync(tenantA.CompanyId)).Should().BeEmpty();
        });

        // Act & Assert 2: Contexto anônimo (TenantId == Guid.Empty) é estritamente fail-closed (0 registros)
        await _factory.ExecuteAsTenantAsync(Guid.Empty, null, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Companies.ToListAsync()).Should().BeEmpty();
            (await db.DemandSlots.ToListAsync()).Should().BeEmpty();
            (await db.Employees.ToListAsync()).Should().BeEmpty();
            (await db.Rosters.ToListAsync()).Should().BeEmpty();
            (await db.Shifts.ToListAsync()).Should().BeEmpty();
            (await db.ShiftSwapRequests.ToListAsync()).Should().BeEmpty();
        });
    }

    [Fact]
    public async Task AllUseCases_WhenTenantBTargetsTenantAResourceIds_ShouldFailWithoutMutatingTenantA()
    {
        // Arrange
        var tenantA = await _factory.SeedFullTenantAsync("Cantina Alfa", "cantina-a@alfa.com.br");
        var tenantB = await _factory.SeedFullTenantAsync("Cantina Beta", "cantina-b@beta.com.br");

        // Act & Assert: Tenant B tenta ler/alterar IDs do Tenant A em todos os casos de uso
        await _factory.ExecuteAsTenantAsync(tenantB.TenantId, tenantB.OwnerUserId, async sp =>
        {
            var getCompany = sp.GetRequiredService<GetCurrentCompanySettingsUseCase>();
            var saveCompany = sp.GetRequiredService<SaveCompanySettingsUseCase>();
            var getDemand = sp.GetRequiredService<GetCurrentDemandWorkspaceUseCase>();
            var genDiagnosis = sp.GetRequiredService<GenerateDemandDiagnosisUseCase>();
            var getEmployees = sp.GetRequiredService<GetCurrentEmployeesWorkspaceUseCase>();
            var updateEmployee = sp.GetRequiredService<UpdateEmployeeUseCase>();
            var toggleEmployee = sp.GetRequiredService<SetEmployeeActiveStatusUseCase>();
            var simFinancial = sp.GetRequiredService<SimulateFinancialImpactUseCase>();
            var getRoster = sp.GetRequiredService<GetCurrentRosterWorkspaceUseCase>();
            var genRoster = sp.GetRequiredService<GenerateRoster5x2UseCase>();
            var assignShift = sp.GetRequiredService<AssignManualShiftUseCase>();
            var removeShift = sp.GetRequiredService<RemoveManualShiftUseCase>();
            var reassignShift = sp.GetRequiredService<ReassignManualShiftUseCase>();
            var swapShifts = sp.GetRequiredService<SwapManualShiftsUseCase>();
            var publishRoster = sp.GetRequiredService<PublishRosterUseCase>();
            var exportCsv = sp.GetRequiredService<ExportRosterCsvUseCase>();
            var shareLink = sp.GetRequiredService<GenerateRosterShareLinkUseCase>();
            var approveSwap = sp.GetRequiredService<ApproveShiftSwapUseCase>();
            var rejectSwap = sp.GetRequiredService<RejectShiftSwapUseCase>();

            var ownCompany = await getCompany.ExecuteAsync();
            ownCompany.IsSuccess.Should().BeTrue();
            ownCompany.Value.CompanyId.Should().Be(tenantB.CompanyId);

            (await getDemand.ExecuteAsync(new GetCurrentDemandWorkspaceCommand(tenantA.CompanyId))).IsFailure.Should().BeTrue();
            (await genDiagnosis.ExecuteAsync(new GenerateDemandDiagnosisCommand(tenantA.CompanyId))).IsFailure.Should().BeTrue();
            (await getEmployees.ExecuteAsync(tenantA.CompanyId)).IsFailure.Should().BeTrue();
            (await simFinancial.ExecuteAsync(new SimulateFinancialImpactCommand(2500m, 12m, CompanyId: tenantA.CompanyId))).IsFailure.Should().BeTrue();
            (await getRoster.ExecuteAsync(new GetCurrentRosterWorkspaceCommand(tenantA.WeekStart, tenantA.CompanyId))).IsFailure.Should().BeTrue();
            (await genRoster.ExecuteAsync(new GenerateRoster5x2Command(tenantA.WeekStart.AddDays(7), tenantA.CompanyId))).IsFailure.Should().BeTrue();

            // SaveCompanySettings ignora qualquer CompanyId externo e atualiza apenas a própria casa do Tenant B
            var saveOwn = await saveCompany.ExecuteAsync(new SaveCompanySettingsCommand(
                "Cantina Beta Renomeada",
                BusinessType.CafeBakery,
                new TimeOnly(10, 0),
                new TimeOnly(22, 0),
                ScheduleModel.RotatingDaysOff));
            saveOwn.IsSuccess.Should().BeTrue();
            saveOwn.Value.CompanyId.Should().Be(tenantB.CompanyId);

            (await updateEmployee.ExecuteAsync(new UpdateEmployeeCommand(
                tenantA.Employee1Id, "Invadido", StationRole.Kitchen, [], [], []))).IsFailure.Should().BeTrue();
            (await toggleEmployee.ExecuteAsync(new SetEmployeeActiveStatusCommand(tenantA.Employee1Id, false))).IsFailure.Should().BeTrue();

            (await assignShift.ExecuteAsync(new AssignManualShiftCommand(
                tenantA.RosterId, tenantB.Employee1Id, tenantA.WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter))).IsFailure.Should().BeTrue();
            (await removeShift.ExecuteAsync(new RemoveManualShiftCommand(tenantA.RosterId, tenantA.Shift1Id))).IsFailure.Should().BeTrue();
            (await reassignShift.ExecuteAsync(new ReassignManualShiftCommand(tenantA.RosterId, tenantA.Shift1Id, tenantB.Employee1Id))).IsFailure.Should().BeTrue();
            (await swapShifts.ExecuteAsync(new SwapManualShiftsCommand(tenantA.RosterId, tenantA.Shift1Id, tenantA.Shift2Id))).IsFailure.Should().BeTrue();
            (await publishRoster.ExecuteAsync(new PublishRosterCommand(tenantA.RosterId))).IsFailure.Should().BeTrue();
            (await exportCsv.ExecuteAsync(new ExportRosterCsvCommand(tenantA.RosterId))).IsFailure.Should().BeTrue();
            (await shareLink.ExecuteAsync(new GenerateRosterShareLinkCommand(tenantA.RosterId))).IsFailure.Should().BeTrue();
            (await approveSwap.ExecuteAsync(new ApproveShiftSwapCommand(tenantA.SwapRequestId))).IsFailure.Should().BeTrue();
            (await rejectSwap.ExecuteAsync(new RejectShiftSwapCommand(tenantA.SwapRequestId))).IsFailure.Should().BeTrue();
        });

        // Assert final: Estado do Tenant A permanece 100% intacto
        await _factory.ExecuteAsTenantAsync(tenantA.TenantId, tenantA.OwnerUserId, async sp =>
        {
            var companyRepo = sp.GetRequiredService<ICompanyRepository>();
            var employeeRepo = sp.GetRequiredService<IEmployeeRepository>();
            var swapRepo = sp.GetRequiredService<IShiftSwapRequestRepository>();

            var companyA = await companyRepo.GetByIdAsync(tenantA.CompanyId);
            companyA!.Name.Should().Be("Cantina Alfa");

            var empA = await employeeRepo.GetByIdAsync(tenantA.Employee1Id);
            empA!.Name.Should().Be("Ana Cantina Alfa");
            empA.IsActive.Should().BeTrue();

            var swapA = await swapRepo.GetByIdAsync(tenantA.SwapRequestId);
            swapA!.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        });
    }

    [Fact]
    public async Task HttpEndpointsAndSharedLinks_WhenCrossTenantRequested_ShouldEnforceIsolation()
    {
        // Arrange
        var tenantA = await _factory.SeedFullTenantAsync("Pub Alfa", "pub-a@alfa.com.br");
        var tenantB = await _factory.SeedFullTenantAsync("Pub Beta", "pub-b@beta.com.br");

        var clientB = await _factory.CreateAuthenticatedClientAsync(tenantB.OwnerEmail, "SenhaForte123");
        var anonymousClient = _factory.CreateAnonymousClient();

        // Act
        var exportCrossTenant = await clientB.GetAsync($"/rosters/{tenantA.RosterId}/export");
        var sharedWithTokenA = await anonymousClient.GetAsync(
            $"/shared/roster?token={Uri.EscapeDataString(tenantA.ShareToken)}&employeeId={tenantB.Employee1Id:D}");
        var sharedHtml = WebUtility.HtmlDecode(await sharedWithTokenA.Content.ReadAsStringAsync());

        // Assert
        exportCrossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        sharedWithTokenA.StatusCode.Should().Be(HttpStatusCode.OK);
        sharedHtml.Should().Contain("Pub Alfa");
        sharedHtml.Should().NotContain("Pub Beta");
        sharedHtml.Should().NotContain($"Ana Pub Beta");
    }

    [Fact]
    public async Task AppDbContext_WhenWritingWithoutTenantOrWithMismatchedTenant_ShouldFailClosed()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Act & Assert 1: Sem tenant autenticado
        await _factory.ExecuteAsTenantAsync(Guid.Empty, null, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var company = Company.Create(
                tenantA,
                "Invasora",
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(22, 0),
                ScheduleModel.RotatingDaysOff).Value;

            db.Companies.Add(company);
            Func<Task> act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<InvalidOperationException>();
        });

        // Act & Assert 2: TenantId divergente do contexto autenticado
        await _factory.ExecuteAsTenantAsync(tenantB, Guid.NewGuid(), async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var company = Company.Create(
                tenantA,
                "Forjada",
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(22, 0),
                ScheduleModel.RotatingDaysOff).Value;

            db.Companies.Add(company);
            Func<Task> act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<InvalidOperationException>();
        });
    }

    public void Dispose() => _factory.Dispose();
}
