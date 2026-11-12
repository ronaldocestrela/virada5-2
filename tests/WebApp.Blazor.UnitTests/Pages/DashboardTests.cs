using System.Security.Claims;
using Application.Common.Interfaces;
using Application.UseCases.Companies;
using Application.UseCases.Rosters;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using FluentAssertions;
using Infrastructure.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Dashboard;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class DashboardTests : BunitContext
{
    private static readonly DateOnly TargetMonday = new(2026, 11, 16);

    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly List<ShiftSwapRequest> _swapRequests = [];
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly ShiftSwapNotificationService _notificationService = new();

    public DashboardTests()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("gestor@casa.com.br");
        authContext.SetRoles(ApplicationRoles.Manager);
        authContext.SetClaims(new Claim(TenantClaimTypes.TenantId, _currentUserService.TenantId.ToString("D")));

        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<IRosterRepository>(_rosterRepository);
        Services.AddSingleton<IShiftSwapRequestRepository>(new InMemoryShiftSwapRequestRepository(_currentUserService, _swapRequests));
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IShiftSwapNotificationService>(_notificationService);
        Services.AddScoped<GetCurrentCompanySettingsUseCase>();
        Services.AddScoped<GetCurrentRosterWorkspaceUseCase>();
        Services.AddScoped<GetPendingShiftSwapsUseCase>();
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingCard()
    {
        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid=\"dashboard-onboarding-card\"]");
            card.TextContent.Should().Contain("Complete o Onboarding da Casa");
            card.InnerHtml.Should().Contain("/onboarding");
        });
    }

    [Fact]
    public void Render_WhenCompanyExistsWithoutRoster_ShouldDisplayMetricsAndEmptyRosterState()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Dashboard",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=\"dashboard-metrics-grid\"]").Should().NotBeNull();
            cut.Find("[data-testid=\"dashboard-roster-summary-card\"]").Should().NotBeNull();
            var emptyNotice = cut.Find("[data-testid=\"dashboard-roster-empty\"]");
            emptyNotice.TextContent.Should().Contain("Nenhuma escala gerada para a próxima semana");
            var cta = cut.Find("[data-testid=\"dashboard-roster-cta\"]");
            cta.TextContent.Should().Contain("Gerar Escala 5x2 Agora");
            cta.GetAttribute("href").Should().Be("/roster");
        });
    }

    [Fact]
    public void Render_WhenActiveRosterExists_ShouldDisplayRosterSummaryData()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa com Escala",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var employee = Employee.Create(
            company.TenantId,
            company.Id,
            "Carlos Garçom",
            StationRole.Waiter).Value;
        _employeeRepository.Seed(employee);

        var roster = Roster.Create(
            company.TenantId,
            company.Id,
            TargetMonday).Value;
        roster.AssignShift(employee, TargetMonday, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        roster.Publish().IsSuccess.Should().BeTrue();

        _rosterRepository.Seed(roster);

        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var summaryCard = cut.Find("[data-testid=\"dashboard-roster-summary-card\"]");
            summaryCard.TextContent.Should().Contain("Resumo da Escala 5x2 da Semana");
            summaryCard.TextContent.Should().Contain("Publicada");
            summaryCard.TextContent.Should().Contain("100% Conforme CLT");
            summaryCard.TextContent.Should().Contain("1 colaboradores");

            var cta = cut.Find("[data-testid=\"dashboard-roster-cta\"]");
            cta.TextContent.Should().Contain("Abrir Painel da Escala Completa");
            cta.GetAttribute("href").Should().Be("/roster");
        });
    }

    [Fact]
    public async Task Render_WhenNotificationArrivesForSameTenant_ShouldUpdatePendingSwapsCardAndIgnoreOtherTenant()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Home",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var cut = Render<Dashboard>();
        cut.FindAll("[data-testid=\"dashboard-pending-swaps-card\"]").Should().BeEmpty();

        // Outro tenant não deve vazar notificação
        var otherTenantSwap = ShiftSwapRequest.Create(
            Guid.NewGuid(), company.Id, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRequests.Add(otherTenantSwap);
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(otherTenantSwap.TenantId));
        cut.FindAll("[data-testid=\"dashboard-pending-swaps-card\"]").Should().BeEmpty();

        // Mesma casa/tenant cria troca pronta para o gestor
        var tenantSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRequests.Add(tenantSwap);

        // Act
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(_currentUserService.TenantId));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid=\"dashboard-pending-swaps-card\"]");
            card.TextContent.Should().Contain("1 pendente(s)");
            card.InnerHtml.Should().Contain("/roster#swap-approvals");
        });
    }

    [Fact]
    public void Render_WhenUserIsEmployee_ShouldDisplayEmployeeCard()
    {
        // Arrange
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("colaborador@casa.com.br");
        authContext.SetRoles(ApplicationRoles.Employee);
        authContext.SetClaims(new Claim(TenantClaimTypes.TenantId, _currentUserService.TenantId.ToString("D")));

        // Act
        var cut = Render<Dashboard>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid=\"dashboard-employee-card\"]");
            card.TextContent.Should().Contain("Área do Colaborador");
            card.InnerHtml.Should().Contain("/roster");
        });
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies = [];
        public void Seed(Company company) => _companies.Add(company);
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_companies.FirstOrDefault(c => c.Id == id));
        public Task<Company?> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(_companies.FirstOrDefault());
        public Task AddAsync(Company company, CancellationToken ct = default) { _companies.Add(company); return Task.CompletedTask; }
        public void Update(Company company) { }
    }

    private sealed class InMemoryEmployeeRepository : IEmployeeRepository
    {
        private readonly List<Employee> _employees = [];
        public void Seed(Employee employee) => _employees.Add(employee);
        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));
        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId).ToList());
        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId && e.IsActive).ToList());
        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => ids.Contains(e.Id)).ToList());
        public Task AddAsync(Employee employee, CancellationToken ct = default) { _employees.Add(employee); return Task.CompletedTask; }
        public void Update(Employee employee) { }
    }

    private sealed class InMemoryDemandSlotRepository : IDemandSlotRepository
    {
        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<DemandSlot?>(null);
        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(Guid companyId, DayOfWeek dayOfWeek, int hour, CancellationToken ct = default) => Task.FromResult<DemandSlot?>(null);
        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DemandSlot>>([]);
        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(DemandSlot demandSlot) { }
        public void RemoveRange(IEnumerable<DemandSlot> demandSlots) { }
    }

    private sealed class InMemoryRosterRepository : IRosterRepository
    {
        private readonly List<Roster> _rosters = [];
        public void Seed(Roster roster) => _rosters.Add(roster);
        public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_rosters.FirstOrDefault(r => r.Id == id));
        public Task<Roster?> GetByCompanyAndWeekStartAsync(Guid companyId, DateOnly weekStartDate, CancellationToken ct = default) => Task.FromResult(_rosters.FirstOrDefault(r => r.CompanyId == companyId && r.WeekStartDate == weekStartDate));
        public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(Guid companyId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Roster>>(_rosters.Where(r => r.CompanyId == companyId && r.WeekStartDate >= startDate && r.WeekStartDate <= endDate).ToList());
        public Task AddAsync(Roster roster, CancellationToken ct = default) { _rosters.Add(roster); return Task.CompletedTask; }
        public void Update(Roster roster) { }
    }

    private sealed class InMemoryShiftSwapRequestRepository(
        ICurrentUserService currentUserService,
        List<ShiftSwapRequest> requests) : IShiftSwapRequestRepository
    {
        public Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(requests.FirstOrDefault(r => r.Id == id && r.TenantId == currentUserService.TenantId));
        public Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>([]);
        public Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(requests
                .Where(r => r.TenantId == currentUserService.TenantId && r.CompanyId == companyId && r.IsPending)
                .ToList());
        public Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default) { requests.Add(request); return Task.CompletedTask; }
        public void Update(ShiftSwapRequest request) { }
    }
}
