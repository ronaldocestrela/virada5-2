using System.Security.Claims;
using Application.Common.Interfaces;
using Application.UseCases.Companies;
using Application.UseCases.Rosters;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class HomeTests : BunitContext
{
    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly List<ShiftSwapRequest> _swapRequests = [];
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly ShiftSwapNotificationService _notificationService = new();

    public HomeTests()
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
        Services.AddScoped<GetPendingShiftSwapsUseCase>();
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

        var cut = Render<Home>();
        cut.FindAll("[data-testid=\"home-pending-swaps-card\"]").Should().BeEmpty();

        // Outro tenant não deve vazar notificação
        var otherTenantSwap = ShiftSwapRequest.Create(
            Guid.NewGuid(), company.Id, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRequests.Add(otherTenantSwap);
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(otherTenantSwap.TenantId));
        cut.FindAll("[data-testid=\"home-pending-swaps-card\"]").Should().BeEmpty();

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
            var card = cut.Find("[data-testid=\"home-pending-swaps-card\"]");
            card.TextContent.Should().Contain("1 pendente(s)");
            card.InnerHtml.Should().Contain("/roster#swap-approvals");
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
        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Employee?>(null);
        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>([]);
        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>([]);
        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Employee>>([]);
        public Task AddAsync(Employee employee, CancellationToken ct = default) => Task.CompletedTask;
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
        public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Roster?>(null);
        public Task<Roster?> GetByCompanyAndWeekStartAsync(Guid companyId, DateOnly weekStartDate, CancellationToken ct = default) => Task.FromResult<Roster?>(null);
        public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(Guid companyId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Roster>>([]);
        public Task AddAsync(Roster roster, CancellationToken ct = default) => Task.CompletedTask;
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
