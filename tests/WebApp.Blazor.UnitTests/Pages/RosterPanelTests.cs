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
using WebApp.Blazor.Components.Pages.Roster;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class RosterPanelTests : BunitContext
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16); // Segunda-feira

    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly InMemoryShiftSwapRequestRepository _swapRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();

    public RosterPanelTests()
    {
        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<IRosterRepository>(_rosterRepository);
        Services.AddSingleton<IShiftSwapRequestRepository>(_swapRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider.Create("Virada5x2.Tests"));
        Services.AddScoped<IRosterShareLinkService, RosterShareLinkService>();
        Services.AddScoped<GetCurrentRosterWorkspaceUseCase>();
        Services.AddScoped<GenerateRoster5x2UseCase>();
        Services.AddScoped<AssignManualShiftUseCase>();
        Services.AddScoped<RemoveManualShiftUseCase>();
        Services.AddScoped<ReassignManualShiftUseCase>();
        Services.AddScoped<SwapManualShiftsUseCase>();
        Services.AddScoped<PublishRosterUseCase>();
        Services.AddScoped<GenerateRosterShareLinkUseCase>();
        Services.AddScoped<GetPendingShiftSwapsUseCase>();
        Services.AddScoped<ApproveShiftSwapUseCase>();
        Services.AddScoped<RejectShiftSwapUseCase>();
        Services.AddSingleton<IShiftSwapNotificationService, ShiftSwapNotificationService>();
        Services.AddSingleton<IToastService>(_toastService);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingRequiredCallToAction()
    {
        // Act
        var cut = Render<RosterPanel>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Painel de Escala Interativo");
        cut.Find("[data-testid=\"roster-onboarding-required\"]").TextContent.Should().Contain("Onboarding");
        cut.Markup.Should().Contain("/onboarding");
    }

    [Fact]
    public void Render_WhenWeekHasNoRosterAndHasEmployees_ShouldRenderEmptyStateWithGenerateButton()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana Cozinha", StationRole.Kitchen);

        // Act
        var cut = RenderForWeek(DefaultWeekStart);

        // Assert
        cut.Find("[data-testid=\"roster-empty-week\"]").Should().NotBeNull();
        cut.Find("[data-testid=\"generate-roster-btn\"]").Should().NotBeNull();
    }

    [Fact]
    public void Render_WhenRosterExists_ShouldDisplayPeakCoverageBarGridExportAndShareActions()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Cozinha", StationRole.Kitchen);
        SeedPeakDemand(company.Id, DayOfWeek.Monday, 12);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen, DemandClassification.Peak);
        _rosterRepository.Seed(roster);

        // Act
        var cut = RenderForWeek(DefaultWeekStart);

        // Assert
        cut.Find("[data-testid=\"peak-coverage-summary\"]").Should().NotBeNull();
        cut.FindAll(".roster-grid__employee-row").Should().ContainSingle();
        cut.Find("[data-testid=\"export-roster-csv-link\"]").GetAttribute("href").Should().Be($"/rosters/{roster.Id}/export");
        cut.Find("[data-testid=\"share-roster-btn\"]").Should().NotBeNull();
        cut.Find("[data-testid=\"publish-roster-btn\"]").Should().NotBeNull();
    }

    [Fact]
    public async Task ManualAssign_WhenClickingDayOffCellAndSubmitting_ShouldAddShiftToDraft()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Cozinha", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var cut = RenderForWeek(DefaultWeekStart);
        var tuesday = DefaultWeekStart.AddDays(1);

        // Act - clicar na folga de terça-feira e confirmar alocação
        cut.Find($"[data-testid=\"roster-dayoff-{ana.Id}-{tuesday:yyyy-MM-dd}\"]").Click();
        cut.Find("[data-testid=\"manual-assign-modal\"]").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-manual-assign-btn\"]").Click());

        // Assert
        roster.Shifts.Should().HaveCount(2);
        roster.Shifts.Should().Contain(s => s.Date == tuesday && s.EmployeeId == ana.Id);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualShiftActions_WhenClickingShiftAndRemoving_ShouldRemoveShiftFromDraft()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Cozinha", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var shift = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).Value;
        _rosterRepository.Seed(roster);

        var cut = RenderForWeek(DefaultWeekStart);

        // Act
        cut.Find($"[data-testid=\"roster-shift-{shift.Id}\"]").Click();
        cut.Find("[data-testid=\"shift-edit-modal\"]").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"remove-shift-btn\"]").Click());

        // Assert
        roster.Shifts.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualShiftActions_WhenReassigningAndSwapping_ShouldPersistChangesAndShowUpdatedGrid()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Salão", StationRole.Waiter);
        var bruno = SeedEmployee(company.Id, "Bruno Salão", StationRole.Waiter);
        var carla = SeedEmployee(company.Id, "Carla Salão", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var shiftAna = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var shiftBruno = roster.AssignShift(bruno, DefaultWeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var cut = RenderForWeek(DefaultWeekStart);

        // Act 1 - Reatribuir turno de Ana para Carla
        cut.Find($"[data-testid=\"roster-shift-{shiftAna.Id}\"]").Click();
        cut.Find("[data-testid=\"reassign-employee-select\"]").Change(carla.Id.ToString());
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-reassign-btn\"]").Click());

        roster.Shifts.Single(s => s.Id == shiftAna.Id).EmployeeId.Should().Be(carla.Id);

        // Act 2 - Iniciar modo troca no turno de Carla e selecionar turno de Bruno
        cut.Find($"[data-testid=\"roster-shift-{shiftAna.Id}\"]").Click();
        cut.Find("[data-testid=\"start-swap-mode-btn\"]").Click();
        cut.Find("[data-testid=\"swap-mode-banner\"]").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"roster-shift-{shiftBruno.Id}\"]").Click());

        // Assert
        roster.Shifts.Single(s => s.Id == shiftAna.Id).EmployeeId.Should().Be(bruno.Id);
        roster.Shifts.Single(s => s.Id == shiftBruno.Id).EmployeeId.Should().Be(carla.Id);
        _unitOfWork.SaveChangesCount.Should().Be(2);
    }

    [Fact]
    public async Task ShareAndPublish_WhenClicked_ShouldGenerateTokenizedLinkAndPublishRoster()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Cozinha", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var cut = RenderForWeek(DefaultWeekStart);

        // Act - gerar link de compartilhamento
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"share-roster-btn\"]").Click());
        cut.Find("[data-testid=\"share-roster-modal\"]").Should().NotBeNull();
        cut.Find("[data-testid=\"share-roster-url\"]").GetAttribute("value").Should().Contain("/shared/roster?token=");
        cut.Find("[data-testid=\"close-share-modal-btn\"]").Click();

        // Act - publicar escala
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"publish-roster-btn\"]").Click());

        // Assert
        roster.Status.Should().Be(RosterStatus.Published);
        cut.FindAll("[data-testid=\"publish-roster-btn\"]").Should().BeEmpty();
    }

    private IRenderedComponent<RosterPanel> RenderForWeek(DateOnly weekStart)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var uri = nav.GetUriWithQueryParameter("week", weekStart.ToString("yyyy-MM-dd"));
        nav.NavigateTo(uri);
        return Render<RosterPanel>();
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Horizonte",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(Guid companyId, string name, StationRole role)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            role).Value;
        _employeeRepository.Seed(employee);
        return employee;
    }

    private void SeedPeakDemand(Guid companyId, DayOfWeek day, int hour)
    {
        var slot = DemandSlot.Create(_currentUserService.TenantId, companyId, day, hour, 100m).Value;
        _demandSlotRepository.Seed(slot);
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
        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId).ToList());
        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId && e.IsActive).ToList());
        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var idSet = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => idSet.Contains(e.Id)).ToList());
        }
        public Task AddAsync(Employee employee, CancellationToken ct = default) { _employees.Add(employee); return Task.CompletedTask; }
        public void Update(Employee employee) { }
    }

    private sealed class InMemoryDemandSlotRepository : IDemandSlotRepository
    {
        private readonly List<DemandSlot> _slots = [];
        public void Seed(params DemandSlot[] slots) => _slots.AddRange(slots);
        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_slots.FirstOrDefault(s => s.Id == id));
        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(Guid companyId, DayOfWeek dayOfWeek, int hour, CancellationToken ct = default) =>
            Task.FromResult(_slots.FirstOrDefault(s => s.CompanyId == companyId && s.DayOfWeek == dayOfWeek && s.Hour == hour));
        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DemandSlot>>(_slots.Where(s => s.CompanyId == companyId).ToList());
        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default) { _slots.Add(demandSlot); return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default) { _slots.AddRange(demandSlots); return Task.CompletedTask; }
        public void Update(DemandSlot demandSlot) { }
        public void RemoveRange(IEnumerable<DemandSlot> demandSlots)
        {
            foreach (var s in demandSlots.ToList())
            {
                _slots.Remove(s);
            }
        }
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
        public void Seed(ShiftSwapRequest request) => _requests.Add(request);
        public Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_requests.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.RosterId == rosterId).ToList());
        public Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_requests.Where(r => r.CompanyId == companyId && r.IsPending).ToList());
        public Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
            Task.FromResult(_requests.Any(r => r.IsPending && (r.RequesterShiftId == shiftId || r.TargetShiftId == shiftId)));
        public Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default) { _requests.Add(request); return Task.CompletedTask; }
        public void Update(ShiftSwapRequest request) { }
    }

    [Fact]
    public async Task ManagerSwapQueue_WhenPendingManagerApproval_ShouldApproveInOneClickAndSwapRoster()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Garçom", StationRole.Waiter);
        var bruno = SeedEmployee(company.Id, "Bruno Garçom", StationRole.Waiter);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var sAna = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sBruno = roster.AssignShift(bruno, DefaultWeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, sBruno.Id,
            reason: "Troca combinada",
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var cut = RenderForWeek(DefaultWeekStart);

        // Act
        cut.Find("[data-testid=\"manager-swap-queue\"]").Should().NotBeNull();
        cut.Find($"[data-testid=\"swap-clt-badge-{swap.Id}\"]").TextContent.Should().Contain("Conforme CLT");
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"manager-approve-btn-{swap.Id}\"]").Click());

        // Assert
        swap.Status.Should().Be(ShiftSwapStatus.Approved);
        sAna.EmployeeId.Should().Be(bruno.Id);
        sBruno.EmployeeId.Should().Be(ana.Id);
        cut.FindAll("[data-testid=\"manager-swap-queue\"]").Should().BeEmpty();
    }

    [Fact]
    public async Task ManagerSwapQueue_WhenRejected_ShouldMarkRejectedWithoutMutatingRoster()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Garçom", StationRole.Waiter);
        var bruno = SeedEmployee(company.Id, "Bruno Garçom", StationRole.Waiter);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var sAna = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sBruno = roster.AssignShift(bruno, DefaultWeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, sBruno.Id,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var cut = RenderForWeek(DefaultWeekStart);

        // Act
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"manager-reject-btn-{swap.Id}\"]").Click());

        // Assert
        swap.Status.Should().Be(ShiftSwapStatus.Rejected);
        sAna.EmployeeId.Should().Be(ana.Id);
        sBruno.EmployeeId.Should().Be(bruno.Id);
    }

    [Fact]
    public void ManagerSwapQueue_WhenSwapViolatesClt_ShouldDisableOneClickApproveAndDisplaySummaryBadges()
    {
        // Arrange
        var company = SeedCompany();
        var ana = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Ana Garçom",
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]).Value;
        var bruno = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Bruno Bar",
            StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]).Value;
        _employeeRepository.Seed(ana);
        _employeeRepository.Seed(bruno);
        SeedPeakDemand(company.Id, DefaultWeekStart.DayOfWeek, 12);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var sAna = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Peak).Value;
        var sBruno = roster.AssignShift(bruno, DefaultWeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Bar).Value;

        var validSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, sBruno.Id,
            requirePeerApproval: false).Value;

        // Troca em conflito CLT (Diego trabalha segunda noite até 02:00, violando 11h antes de terça 10:00)
        var carla = SeedEmployee(company.Id, "Carla Salão", StationRole.Waiter);
        var diego = SeedEmployee(company.Id, "Diego Salão", StationRole.Waiter);
        var sCarla = roster.AssignShift(carla, DefaultWeekStart.AddDays(1), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter).Value;
        roster.AssignShift(diego, DefaultWeekStart, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var conflictingSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            carla.Id, sCarla.Id, diego.Id, null,
            requirePeerApproval: false).Value;

        _swapRepository.Seed(validSwap);
        _swapRepository.Seed(conflictingSwap);

        // Act
        var cut = RenderForWeek(DefaultWeekStart);

        // Assert
        cut.Find($"[data-testid=\"swap-polyvalence-badge-{validSwap.Id}\"]").TextContent.Should().Contain("Polivalência");
        cut.Find($"[data-testid=\"swap-peak-badge-{validSwap.Id}\"]").TextContent.Should().Contain("Turno de pico");
        cut.Find($"[data-testid=\"manager-approve-btn-{validSwap.Id}\"]").HasAttribute("disabled").Should().BeFalse();

        cut.Find($"[data-testid=\"swap-clt-badge-{conflictingSwap.Id}\"]").TextContent.Should().Contain("Conflito CLT");
        cut.Find($"[data-testid=\"manager-approve-btn-{conflictingSwap.Id}\"]").HasAttribute("disabled").Should().BeTrue();
        cut.Find($"[data-testid=\"swap-clt-alert-{conflictingSwap.Id}\"]").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ManagerSwapQueue_WhenTenantNotificationArrivesForNewSwapAndOtherWeek_ShouldShowToastAndWeekLink()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana Garçom", StationRole.Waiter);
        var bruno = SeedEmployee(company.Id, "Bruno Garçom", StationRole.Waiter);
        var otherWeek = DefaultWeekStart.AddDays(7);

        var currentRoster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var otherRoster = Roster.Create(_currentUserService.TenantId, company.Id, otherWeek).Value;
        var sOther = otherRoster.AssignShift(ana, otherWeek, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(currentRoster);
        _rosterRepository.Seed(otherRoster);

        var cut = RenderForWeek(DefaultWeekStart);
        cut.FindAll("[data-testid=\"manager-swap-queue\"]").Should().BeEmpty();

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, otherRoster.Id,
            ana.Id, sOther.Id, bruno.Id, null,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var notificationSvc = Services.GetRequiredService<IShiftSwapNotificationService>();

        // Act
        await cut.InvokeAsync(() => notificationSvc.NotifyTenantSwapsChanged(_currentUserService.TenantId));

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=\"manager-swap-queue\"]").Should().NotBeNull();
            cut.Find($"[data-testid=\"swap-week-link-{swap.Id}\"]").GetAttribute("href")
                .Should().Be($"/roster?week={otherWeek:yyyy-MM-dd}");
            _toastService.Messages.Should().Contain(m => m.Title.Contains("Nova troca"));
        });
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }
}
