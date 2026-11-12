using System.Security.Claims;
using Application.Common.Interfaces;
using Application.UseCases.Rosters;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Layout;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Layout;

public class NavMenuTests : BunitContext
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly InMemoryShiftSwapRequestRepository _swapRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly ShiftSwapNotificationService _notificationService = new();

    public NavMenuTests()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("gestor@casa.com.br");
        authContext.SetRoles(ApplicationRoles.Manager);
        authContext.SetClaims(new Claim(TenantClaimTypes.TenantId, _currentUserService.TenantId.ToString("D")));

        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<IRosterRepository>(_rosterRepository);
        Services.AddSingleton<IShiftSwapRequestRepository>(_swapRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IShiftSwapNotificationService>(_notificationService);
        Services.AddScoped<GetPendingShiftSwapsUseCase>();
    }

    [Fact]
    public async Task Render_WhenManagerNotificationFiresForSameTenant_ShouldRefreshPendingSwapsBadge()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Notificação",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var cut = Render<NavMenu>();
        cut.FindAll("[data-testid=\"nav-pending-swaps-badge\"]").Should().BeEmpty();

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId,
            company.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        // Act
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(_currentUserService.TenantId));

        // Assert
        cut.WaitForAssertion(() =>
        {
            var badge = cut.Find("[data-testid=\"nav-pending-swaps-badge\"]");
            badge.TextContent.Trim().Should().Be("1");
        });
    }

    [Fact]
    public async Task Render_WhenNotificationFiresForDifferentTenant_ShouldNotLeakOrChangeBadge()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Isolada",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var cut = Render<NavMenu>();
        cut.FindAll("[data-testid=\"nav-pending-swaps-badge\"]").Should().BeEmpty();

        var otherTenantSwap = ShiftSwapRequest.Create(
            Guid.NewGuid(),
            company.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRepository.Seed(otherTenantSwap);

        // Act
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(otherTenantSwap.TenantId));

        // Assert
        cut.FindAll("[data-testid=\"nav-pending-swaps-badge\"]").Should().BeEmpty();
    }

    [Fact]
    public async Task Render_WhenPendingSwapIsApprovedAndNotificationFires_ShouldHideBadge()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Limpa",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId,
            company.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var cut = Render<NavMenu>();
        cut.Find("[data-testid=\"nav-pending-swaps-badge\"]").TextContent.Trim().Should().Be("1");

        // Act - gestor aprova a única pendência e notifica circuito
        swap.Approve(_currentUserService.UserId, "OK").IsSuccess.Should().BeTrue();
        await cut.InvokeAsync(() => _notificationService.NotifyTenantSwapsChanged(_currentUserService.TenantId));

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=\"nav-pending-swaps-badge\"]").Should().BeEmpty();
        });
    }

    [Fact]
    public void Render_WhenAuthenticatedAsOwner_ShouldIncludeMobileTopBarDrawerToggleAndBottomBar()
    {
        // Act
        var cut = Render<NavMenu>();

        // Assert
        cut.Find("#nav-drawer-toggle").Should().NotBeNull();
        cut.Find(".nav-topbar").Should().NotBeNull();
        cut.Find(".nav-bottom-bar").Should().NotBeNull();
        cut.FindAll(".nav-bottom-bar__item").Should().HaveCount(5);
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

    private sealed class InMemoryShiftSwapRequestRepository : IShiftSwapRequestRepository
    {
        private readonly List<ShiftSwapRequest> _requests = [];
        public void Seed(ShiftSwapRequest request) => _requests.Add(request);
        public Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_requests.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.RosterId == rosterId).ToList());
        public Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.CompanyId == companyId && r.IsPending).ToList());
        public Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default) { _requests.Add(request); return Task.CompletedTask; }
        public void Update(ShiftSwapRequest request) { }
    }
}

