using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class ManualRosterAdjustmentsUseCaseTests
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16); // Segunda-feira

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private AssignManualShiftUseCase CreateAssignUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _currentUserService, _unitOfWork);

    private RemoveManualShiftUseCase CreateRemoveUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _currentUserService, _unitOfWork);

    private ReassignManualShiftUseCase CreateReassignUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _currentUserService, _unitOfWork);

    private SwapManualShiftsUseCase CreateSwapUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _currentUserService, _unitOfWork);

    private PublishRosterUseCase CreatePublishUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _currentUserService, _unitOfWork);

    private Company SeedCompany(ScheduleModel model = ScheduleModel.RotatingDaysOff, params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Horizonte",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            model,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole primaryRole,
        IEnumerable<SecondarySkill>? skills = null,
        IEnumerable<DayOfWeek>? unavailableDays = null)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            primaryRole,
            skills,
            null,
            unavailableDays).Value;

        _employeeRepository.Seed(employee);
        return employee;
    }

    [Fact]
    public async Task AssignManualShift_WhenValidDraftAndCompliant_ShouldPersistShiftAndRecalculateReport()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateAssignUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new AssignManualShiftCommand(
            roster.Id,
            ana.Id,
            DefaultWeekStart,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Kitchen));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().ContainSingle(s => s.EmployeeId == ana.Id && s.AssignedRole == StationRole.Kitchen);
        result.Value.ComplianceReport.TotalShifts.Should().Be(1);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task AssignManualShift_WhenCompanyClosedOnDate_ShouldReturnValidationFailure()
    {
        // Arrange
        var company = SeedCompany(ScheduleModel.FixedClosure, DayOfWeek.Monday);
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateAssignUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new AssignManualShiftCommand(
            roster.Id,
            ana.Id,
            DefaultWeekStart, // Segunda-feira fechada
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Kitchen));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.ShiftOnClosedDay");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task AssignManualShift_WhenViolatesElevenHourInterShiftRest_ShouldReturnCltConflictAndNotPersist()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(15, 0), new TimeOnly(23, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var useCase = CreateAssignUseCase();

        // Act - dia seguinte às 08:00 (apenas 9h de descanso)
        var result = await useCase.ExecuteAsync(new AssignManualShiftCommand(
            roster.Id,
            ana.Id,
            DefaultWeekStart.AddDays(1),
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            StationRole.Kitchen));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveManualShift_WhenShiftExistsInDraft_ShouldRemoveShiftAndUpdateComplianceReport()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var shift = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateRemoveUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RemoveManualShiftCommand(roster.Id, shift.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().BeEmpty();
        result.Value.ComplianceReport.TotalShifts.Should().Be(0);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ReassignManualShift_WhenReplacementUsesAutonomousPolyvalence_ShouldReassignWithSecondaryOrigin()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var bruno = SeedEmployee(
            company.Id,
            "Bruno",
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Kitchen, SkillProficiency.Autonomous).Value]);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var shift = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateReassignUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ReassignManualShiftCommand(roster.Id, shift.Id, bruno.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var updatedShift = result.Value.Shifts.Single();
        updatedShift.EmployeeId.Should().Be(bruno.Id);
        updatedShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ReassignManualShift_WhenRosterIsPublished_ShouldReturnConflict()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var carla = SeedEmployee(company.Id, "Carla", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var shift = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).Value;
        roster.Publish();
        _rosterRepository.Seed(roster);

        var useCase = CreateReassignUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ReassignManualShiftCommand(roster.Id, shift.Id, carla.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.NotEditable");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task SwapManualShifts_WhenBothEmployeesEligibleAndCltCompliant_ShouldSwapAtomically()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var bruno = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;

        var shiftAna = roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var shiftBruno = roster.AssignShift(bruno, DefaultWeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateSwapUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new SwapManualShiftsCommand(roster.Id, shiftAna.Id, shiftBruno.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().Contain(s => s.ShiftId == shiftAna.Id && s.EmployeeId == bruno.Id);
        result.Value.Shifts.Should().Contain(s => s.ShiftId == shiftBruno.Id && s.EmployeeId == ana.Id);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishRoster_WhenDraftHasShiftsAndMeetsCltBoundaries_ShouldTransitionToPublished()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var useCase = CreatePublishUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new PublishRosterCommand(roster.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RosterStatus.Published);
        roster.Status.Should().Be(RosterStatus.Published);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishRoster_WhenPreviousWeekSundayViolatesBoundary_ShouldReturnConflictAndKeepDraft()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);

        // Duas semanas anteriores trabalhando no domingo (streak = 2, limite ThreeWeekCycle = 2)
        var weekMinus2 = DefaultWeekStart.AddDays(-14);
        var r2 = Roster.Create(_currentUserService.TenantId, company.Id, weekMinus2).Value;
        r2.AssignShift(ana, weekMinus2.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        r2.Publish();

        var weekMinus1 = DefaultWeekStart.AddDays(-7);
        var r1 = Roster.Create(_currentUserService.TenantId, company.Id, weekMinus1).Value;
        r1.AssignShift(ana, weekMinus1.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        r1.Publish();

        var currentRoster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        currentRoster.AssignShift(ana, DefaultWeekStart.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(r2, r1, currentRoster);

        var useCase = CreatePublishUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new PublishRosterCommand(currentRoster.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.SundayRotationViolated");
        currentRoster.Status.Should().Be(RosterStatus.Draft);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }
}

