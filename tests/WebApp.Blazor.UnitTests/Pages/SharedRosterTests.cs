using Application.Common.Interfaces;
using Application.UseCases.Rosters;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Shared;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class SharedRosterTests : BunitContext
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly InMemoryShiftSwapRequestRepository _swapRepository = new();
    private readonly ScopedTenantContext _scopedTenantContext = new();
    private readonly ScopedAwareCurrentUserService _currentUserService;
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();
    private readonly RosterShareLinkService _shareLinkService;

    public SharedRosterTests()
    {
        _currentUserService = new ScopedAwareCurrentUserService(_scopedTenantContext);
        _shareLinkService = new RosterShareLinkService(DataProtectionProvider.Create("Virada5x2.Tests"));

        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<IRosterRepository>(_rosterRepository);
        Services.AddSingleton<IShiftSwapRequestRepository>(_swapRepository);
        Services.AddSingleton<IScopedTenantContext>(_scopedTenantContext);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddSingleton<IRosterShareLinkService>(_shareLinkService);
        Services.AddScoped<GetSharedRosterWorkspaceUseCase>();
        Services.AddScoped<GetCompatibleSwapOptionsUseCase>();
        Services.AddScoped<RequestShiftSwapUseCase>();
        Services.AddScoped<RespondShiftSwapPeerUseCase>();
        Services.AddScoped<RejectShiftSwapUseCase>();
        Services.AddScoped<RequestSharedShiftSwapUseCase>();
        Services.AddScoped<RespondSharedShiftSwapPeerUseCase>();
        Services.AddScoped<CancelSharedShiftSwapUseCase>();
        Services.AddSingleton<IToastService>(_toastService);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_WhenTokenInvalid_ShouldDisplayErrorCardWithoutExposingData()
    {
        // Act
        var cut = RenderWithToken("tampered-token");

        // Assert
        cut.Find("[data-testid=\"shared-roster-error\"]").TextContent.Should().Contain("Link indisponível");
        cut.FindAll("[data-testid=\"shared-roster-header\"]").Should().BeEmpty();
    }

    [Fact]
    public async Task Render_WhenEmployeeSelected_ShouldShowExactShiftsDaysOffAndCompletePeerSwapFlow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Cantina Mobile",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            closedDays: [DayOfWeek.Monday]).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana Garçom", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno Bar", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var anaShift = roster.AssignShift(ana, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var brunoShift = roster.AssignShift(bruno, WeekStart.AddDays(3), new TimeOnly(16, 0), new TimeOnly(23, 0), StationRole.Bar).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var token = _shareLinkService.CreateShareLink(tenantId, company.Id, roster.Id, WeekStart).Value.Token;

        // Act 1: Renderizar como Ana
        var cut = RenderWithToken(token, ana.Id);

        // Assert 1: Turnos e folga de segunda (casa fechada)
        cut.Find("[data-testid=\"shared-roster-header\"]").TextContent.Should().Contain("Cantina Mobile");
        cut.Find($"[data-testid=\"shared-dayoff-{WeekStart:yyyy-MM-dd}\"]").TextContent.Should().Contain("Casa fechada");
        cut.Find($"[data-testid=\"shared-shift-{anaShift.Id}\"]").TextContent.Should().Contain("11:00 – 19:00");

        // Act 2: Abrir modal de troca e enviar solicitação para Bruno
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"request-swap-btn-{anaShift.Id}\"]").Click());
        cut.Find("[data-testid=\"shared-swap-modal\"]").Should().NotBeNull();
        cut.Find($"[data-testid=\"swap-option-{bruno.Id}-{brunoShift.Id}\"]").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-shared-swap-btn\"]").Click());

        // Assert 2: Solicitação criada como PendingPeerApproval
        _swapRepository.PersistedRequests.Should().ContainSingle();
        var swap = _swapRepository.PersistedRequests[0];
        swap.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);

        // Act 3: Alternar para Bruno e aceitar a solicitação
        await cut.InvokeAsync(() =>
            cut.Find("#shared-employee-select").Change(bruno.Id.ToString("D")));
        cut.Find("[data-testid=\"shared-peer-inbox\"]").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"peer-accept-btn-{swap.Id}\"]").Click());

        // Assert 3: Encaminhada para aprovação em 1 clique do gestor
        swap.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
    }

    [Fact]
    public async Task OpenSwapModal_WhenNoCompatiblePeersAvailable_ShouldDisplayEmptyGuidanceMessage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Solo",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana Cozinha", StationRole.Kitchen).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno Caixa", StationRole.Cashier).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var anaShift = roster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).Value;
        roster.AssignShift(bruno, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Cashier).IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var token = _shareLinkService.CreateShareLink(tenantId, company.Id, roster.Id, WeekStart).Value.Token;
        var cut = RenderWithToken(token, ana.Id);

        // Act
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"request-swap-btn-{anaShift.Id}\"]").Click());

        // Assert
        cut.Find("[data-testid=\"shared-no-compatible-options\"]").TextContent
            .Should().Contain("Nenhum colega compatível");
    }

    [Fact]
    public async Task OpenSwapModal_WhenMutualPolyvalentAndOneWayOptionsExist_ShouldDisplayRoleAndPolyvalenceBadges()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Restaurante Polivalente",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana Salão", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno Bar", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var anaShift = roster.AssignShift(ana, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var brunoShift = roster.AssignShift(bruno, WeekStart.AddDays(3), new TimeOnly(16, 0), new TimeOnly(23, 0), StationRole.Bar).Value;
        _rosterRepository.Seed(roster);

        var token = _shareLinkService.CreateShareLink(tenantId, company.Id, roster.Id, WeekStart).Value.Token;
        var cut = RenderWithToken(token, ana.Id);

        // Act
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"request-swap-btn-{anaShift.Id}\"]").Click());

        // Assert
        var mutualOptionText = cut.Find($"[data-testid=\"swap-option-{bruno.Id}-{brunoShift.Id}\"]").TextContent;
        mutualOptionText.Should().Contain("Bruno Bar");
        mutualOptionText.Should().Contain("Bar");
        mutualOptionText.Should().Contain("Troca mútua");
        mutualOptionText.Should().Contain("Polivalência");
        mutualOptionText.Should().Contain("Você cobre via polivalência");

        var oneWayOptionText = cut.Find($"[data-testid=\"swap-option-{bruno.Id}-cover\"]").TextContent;
        oneWayOptionText.Should().Contain("Cobertura de folga");
    }

    [Fact]
    public async Task PeerInboxAndRequesterFlow_WhenPeerRejectsOrRequesterCancels_ShouldUpdateStatusAndExposePeerCopyLink()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Bar Trocas",
            BusinessType.Bar,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var sAna = roster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sBruno = roster.AssignShift(bruno, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var token = _shareLinkService.CreateShareLink(tenantId, company.Id, roster.Id, WeekStart).Value.Token;
        var cut = RenderWithToken(token, ana.Id);

        // Act 1: Ana envia solicitação e vê botão para copiar link direto ao colega
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"request-swap-btn-{sAna.Id}\"]").Click());
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-shared-swap-btn\"]").Click());

        var firstSwap = _swapRepository.PersistedRequests.Single();
        cut.Find($"[data-testid=\"copy-peer-link-btn-{firstSwap.Id}\"]").Should().NotBeNull();

        // Act 2: Ana cancela a solicitação e o turno volta a permitir nova solicitação
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"cancel-swap-btn-{firstSwap.Id}\"]").Click());
        firstSwap.Status.Should().Be(ShiftSwapStatus.Cancelled);
        cut.Find($"[data-testid=\"request-swap-btn-{sAna.Id}\"]").Should().NotBeNull();

        // Act 3: Ana envia nova solicitação e Bruno recusa
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"request-swap-btn-{sAna.Id}\"]").Click());
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-shared-swap-btn\"]").Click());
        var secondSwap = _swapRepository.PersistedRequests[1];

        await cut.InvokeAsync(() => cut.Find("#shared-employee-select").Change(bruno.Id.ToString("D")));
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"peer-reject-btn-{secondSwap.Id}\"]").Click());

        // Assert 3
        secondSwap.Status.Should().Be(ShiftSwapStatus.Rejected);
        cut.FindAll("[data-testid=\"shared-peer-inbox\"]").Should().BeEmpty();
    }

    private IRenderedComponent<SharedRoster> RenderWithToken(string token, Guid? employeeId = null)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var uri = employeeId.HasValue
            ? $"/shared/roster?token={Uri.EscapeDataString(token)}&employeeId={employeeId.Value:D}"
            : $"/shared/roster?token={Uri.EscapeDataString(token)}";
        nav.NavigateTo(uri);
        return Render<SharedRoster>();
    }

    private sealed class ScopedAwareCurrentUserService(IScopedTenantContext scopedTenantContext) : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => scopedTenantContext.ScopedTenantId ?? Guid.Empty;
        public bool IsAuthenticated => false;
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
        public void Seed(params Employee[] employees) => _employees.AddRange(employees);
        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));
        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId).ToList());
        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId && e.IsActive).ToList());
        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var set = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => set.Contains(e.Id)).ToList());
        }
        public Task AddAsync(Employee employee, CancellationToken ct = default) { _employees.Add(employee); return Task.CompletedTask; }
        public void Update(Employee employee) { }
    }

    private sealed class InMemoryDemandSlotRepository : IDemandSlotRepository
    {
        private readonly List<DemandSlot> _slots = [];
        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_slots.FirstOrDefault(s => s.Id == id));
        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(Guid companyId, DayOfWeek dayOfWeek, int hour, CancellationToken ct = default) =>
            Task.FromResult(_slots.FirstOrDefault(s => s.CompanyId == companyId && s.DayOfWeek == dayOfWeek && s.Hour == hour));
        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DemandSlot>>(_slots.Where(s => s.CompanyId == companyId).ToList());
        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default) { _slots.Add(demandSlot); return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default) { _slots.AddRange(demandSlots); return Task.CompletedTask; }
        public void Update(DemandSlot demandSlot) { }
        public void RemoveRange(IEnumerable<DemandSlot> demandSlots) { }
    }

    private sealed class InMemoryRosterRepository : IRosterRepository
    {
        private readonly List<Roster> _rosters = [];
        public void Seed(Roster roster) => _rosters.Add(roster);
        public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_rosters.FirstOrDefault(r => r.Id == id));
        public Task<Roster?> GetByCompanyAndWeekStartAsync(Guid companyId, DateOnly weekStartDate, CancellationToken ct = default) =>
            Task.FromResult(_rosters.FirstOrDefault(r => r.CompanyId == companyId && r.WeekStartDate == weekStartDate && r.Status != RosterStatus.Archived));
        public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(Guid companyId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Roster>>(_rosters.Where(r => r.CompanyId == companyId && r.WeekStartDate >= startDate && r.WeekStartDate <= endDate && r.Status != RosterStatus.Archived).ToList());
        public Task AddAsync(Roster roster, CancellationToken ct = default) { _rosters.Add(roster); return Task.CompletedTask; }
        public void Update(Roster roster) { }
    }

    private sealed class InMemoryShiftSwapRequestRepository : IShiftSwapRequestRepository
    {
        private readonly List<ShiftSwapRequest> _requests = [];
        public IReadOnlyList<ShiftSwapRequest> PersistedRequests => _requests.AsReadOnly();
        public Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_requests.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.RosterId == rosterId).ToList());
        public Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.CompanyId == companyId && r.IsPending).ToList());
        public Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
            Task.FromResult(_requests.Any(r => r.IsPending && (r.RequesterShiftId == shiftId || r.TargetShiftId == shiftId)));
        public Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default) { _requests.Add(request); return Task.CompletedTask; }
        public void Update(ShiftSwapRequest request) { }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}

