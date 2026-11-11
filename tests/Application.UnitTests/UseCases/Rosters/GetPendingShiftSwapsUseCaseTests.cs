using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class GetPendingShiftSwapsUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetPendingShiftSwapsUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService);

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasManagerAndPeerPendingSwaps_ShouldSeparateCountsAndReportCltCompliance()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Gestor",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        var carla = Employee.Create(_currentUserService.TenantId, company.Id, "Carla", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno, carla);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sAna = roster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sBruno = roster.AssignShift(bruno, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sCarla = roster.AssignShift(carla, WeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var readyForManager = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, sBruno.Id,
            requirePeerApproval: false).Value;

        var waitingPeer = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            carla.Id, sCarla.Id, ana.Id, null,
            requirePeerApproval: true).Value;

        _swapRepository.Seed(readyForManager, waitingPeer);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PendingManagerApprovalCount.Should().Be(1);
        result.Value.PendingPeerApprovalCount.Should().Be(1);
        result.Value.Items.Should().HaveCount(2);

        var managerItem = result.Value.Items.Single(i => i.SwapRequest.Id == readyForManager.Id);
        managerItem.CanApproveNow.Should().BeTrue();
        managerItem.IsCltCompliant.Should().BeTrue();
        managerItem.ComplianceBlockMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDraftChangedCausingCltViolation_ShouldExposeNonCompliantWarningWithoutThrowing()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Gestor",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sAna = roster.AssignShift(ana, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, null,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        // Alteração posterior no rascunho cria conflito de interjornada para Bruno
        roster.AssignShift(bruno, WeekStart, new TimeOnly(17, 0), new TimeOnly(1, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.CanApproveNow.Should().BeFalse();
        item.IsCltCompliant.Should().BeFalse();
        item.ComplianceBlockMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSwapUsesSecondarySkillAndPeakDemand_ShouldPopulateValidationSummary()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Pico",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var waiter = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Ana Salão",
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]).Value;
        var bartender = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Bruno Bar",
            StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]).Value;
        _employeeRepository.Seed(waiter, bartender);

        var peakSlot = DemandSlot.Create(_currentUserService.TenantId, company.Id, WeekStart.DayOfWeek, 19, 100m).Value;
        _demandSlotRepository.Seed(peakSlot);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sWaiter = roster.AssignShift(waiter, WeekStart, new TimeOnly(18, 0), new TimeOnly(22, 0), StationRole.Waiter, DemandClassification.Peak).Value;
        var sBar = roster.AssignShift(bartender, WeekStart.AddDays(2), new TimeOnly(14, 0), new TimeOnly(18, 0), StationRole.Bar, DemandClassification.Moderate).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            waiter.Id, sWaiter.Id, bartender.Id, sBar.Id,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.CanApproveNow.Should().BeTrue();
        item.ValidationSummary.Should().NotBeNull();
        item.ValidationSummary!.RequesterShiftDemandLevel.Should().Be(DemandClassification.Peak);
        item.ValidationSummary.RequesterReplacementCoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        item.ValidationSummary.TargetReplacementCoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterIsArchivedOrMissing_ShouldMarkItemNonCompliantWithoutThrowing()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Arquivado",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var archivedRoster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sAna = archivedRoster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        archivedRoster.Archive().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(archivedRoster);

        var swapArchived = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, archivedRoster.Id,
            ana.Id, sAna.Id, bruno.Id, null,
            requirePeerApproval: false).Value;
        var swapMissing = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, Guid.NewGuid(),
            ana.Id, Guid.NewGuid(), bruno.Id, null,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swapArchived, swapMissing);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Should().OnlyContain(i => !i.CanApproveNow && !i.IsCltCompliant && !string.IsNullOrWhiteSpace(i.ComplianceBlockMessage));
    }

    [Fact]
    public async Task ExecuteAsync_WhenMultiplePendingSwapsAcrossWeeks_ShouldBatchQueriesWithoutNPlusOneCalls()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Batch",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        var carla = Employee.Create(_currentUserService.TenantId, company.Id, "Carla", StationRole.Waiter).Value;
        var diego = Employee.Create(_currentUserService.TenantId, company.Id, "Diego", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno, carla, diego);

        var week1 = WeekStart;
        var week2 = WeekStart.AddDays(7);

        var roster1 = Roster.Create(_currentUserService.TenantId, company.Id, week1).Value;
        var s1A = roster1.AssignShift(ana, week1, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s1B = roster1.AssignShift(bruno, week1.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s1C = roster1.AssignShift(carla, week1.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        var roster2 = Roster.Create(_currentUserService.TenantId, company.Id, week2).Value;
        var s2D = roster2.AssignShift(diego, week2, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster1, roster2);

        _swapRepository.Seed(
            ShiftSwapRequest.Create(_currentUserService.TenantId, company.Id, roster1.Id, ana.Id, s1A.Id, bruno.Id, s1B.Id, requirePeerApproval: false).Value,
            ShiftSwapRequest.Create(_currentUserService.TenantId, company.Id, roster1.Id, carla.Id, s1C.Id, diego.Id, null, requirePeerApproval: false).Value,
            ShiftSwapRequest.Create(_currentUserService.TenantId, company.Id, roster2.Id, diego.Id, s2D.Id, ana.Id, null, requirePeerApproval: false).Value);

        var countingDemandRepo = new CountingDemandSlotRepository(_demandSlotRepository);
        var countingEmployeeRepo = new CountingEmployeeRepository(_employeeRepository);
        var countingRosterRepo = new CountingRosterRepository(_rosterRepository);

        var useCase = new GetPendingShiftSwapsUseCase(
            _companyRepository,
            countingEmployeeRepo,
            countingDemandRepo,
            countingRosterRepo,
            _swapRepository,
            _currentUserService);

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Should().OnlyContain(i => i.CanApproveNow && i.IsCltCompliant);
        countingDemandRepo.GetByCompanyCalls.Should().Be(1);
        countingEmployeeRepo.GetByIdsCalls.Should().Be(1);
        countingRosterRepo.GetByWeekRangeCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIncludeCltEvaluationIsFalse_ShouldReturnCountsOnlyWithoutLoadingRostersOrDemand()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Leve",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        _swapRepository.Seed(
            ShiftSwapRequest.Create(_currentUserService.TenantId, company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requirePeerApproval: false).Value,
            ShiftSwapRequest.Create(_currentUserService.TenantId, company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requirePeerApproval: true).Value);

        var countingDemandRepo = new CountingDemandSlotRepository(_demandSlotRepository);
        var countingRosterRepo = new CountingRosterRepository(_rosterRepository);

        var useCase = new GetPendingShiftSwapsUseCase(
            _companyRepository,
            _employeeRepository,
            countingDemandRepo,
            countingRosterRepo,
            _swapRepository,
            _currentUserService);

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand(IncludeCltEvaluation: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PendingManagerApprovalCount.Should().Be(1);
        result.Value.PendingPeerApprovalCount.Should().Be(1);
        result.Value.Items.Should().BeEmpty();
        countingDemandRepo.GetByCompanyCalls.Should().Be(0);
        countingRosterRepo.GetByWeekRangeCalls.Should().Be(0);
    }

    private sealed class CountingDemandSlotRepository(IDemandSlotRepository inner) : IDemandSlotRepository
    {
        public int GetByCompanyCalls { get; private set; }
        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(Guid companyId, DayOfWeek dayOfWeek, int hour, CancellationToken ct = default) =>
            inner.GetByCompanyDayAndHourAsync(companyId, dayOfWeek, hour, ct);
        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
        {
            GetByCompanyCalls++;
            return inner.GetByCompanyAsync(companyId, ct);
        }
        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default) => inner.AddAsync(demandSlot, ct);
        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default) => inner.AddRangeAsync(demandSlots, ct);
        public void Update(DemandSlot demandSlot) => inner.Update(demandSlot);
        public void RemoveRange(IEnumerable<DemandSlot> demandSlots) => inner.RemoveRange(demandSlots);
    }

    private sealed class CountingEmployeeRepository(IEmployeeRepository inner) : IEmployeeRepository
    {
        public int GetByIdsCalls { get; private set; }
        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) => inner.GetByCompanyAsync(companyId, ct);
        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) => inner.GetActiveByCompanyAsync(companyId, ct);
        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            GetByIdsCalls++;
            return inner.GetByIdsAsync(ids, ct);
        }
        public Task AddAsync(Employee employee, CancellationToken ct = default) => inner.AddAsync(employee, ct);
        public void Update(Employee employee) => inner.Update(employee);
    }

    private sealed class CountingRosterRepository(IRosterRepository inner) : IRosterRepository
    {
        public int GetByWeekRangeCalls { get; private set; }
        public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) => inner.GetByIdWithShiftsAsync(id, ct);
        public Task<Roster?> GetByCompanyAndWeekStartAsync(Guid companyId, DateOnly weekStartDate, CancellationToken ct = default) =>
            inner.GetByCompanyAndWeekStartAsync(companyId, weekStartDate, ct);
        public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(Guid companyId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
        {
            GetByWeekRangeCalls++;
            return inner.GetByWeekRangeAsync(companyId, startDate, endDate, ct);
        }
        public Task AddAsync(Roster roster, CancellationToken ct = default) => inner.AddAsync(roster, ct);
        public void Update(Roster roster) => inner.Update(roster);
    }
}

