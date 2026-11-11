using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class RequestShiftSwapUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RequestShiftSwapUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService,
            _unitOfWork);

    [Fact]
    public async Task ExecuteAsync_WhenMutualSwapValidAndPeerRequired_ShouldCreatePendingPeerRequestWithoutMutatingRoster()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "João", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bartender = SeedEmployee(company.Id, "Marina", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var s1 = roster.AssignShift(waiter, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s2 = roster.AssignShift(bartender, WeekStart.AddDays(2), new TimeOnly(16, 0), new TimeOnly(0, 0), StationRole.Bar).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();
        var command = new RequestShiftSwapCommand(
            roster.Id,
            waiter.Id,
            s1.Id,
            bartender.Id,
            s2.Id,
            "Consulta odontológica",
            RequirePeerApproval: true,
            CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);
        result.Value.SwapRequest.Reason.Should().Be("Consulta odontológica");
        result.Value.ValidationSummary.IsCltCompliant.Should().BeTrue();
        result.Value.ValidationSummary.RequesterReplacementCoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        result.Value.ValidationSummary.TargetReplacementCoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);

        // Must NOT mutate roster shifts at request time
        s1.EmployeeId.Should().Be(waiter.Id);
        s2.EmployeeId.Should().Be(bartender.Id);
        _swapRepository.AddedRequests.Should().ContainSingle();
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneWayCoverWithoutPeerApproval_ShouldCreatePendingManagerRequest()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var backup = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var s1 = roster.AssignShift(waiter, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();
        var command = new RequestShiftSwapCommand(
            roster.Id,
            waiter.Id,
            s1.Id,
            backup.Id,
            TargetShiftId: null,
            Reason: "Cobertura combinada",
            RequirePeerApproval: false);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        result.Value.SwapRequest.TargetShiftId.Should().BeNull();
        result.Value.ValidationSummary.TargetShiftDemandLevel.Should().BeNull();
        result.Value.ValidationSummary.TargetReplacementCoverageOrigin.Should().BeNull();
        result.Value.ValidationSummary.RequesterReplacementCoverageOrigin.Should().Be(CoverageOrigin.PrimaryRole);
        s1.EmployeeId.Should().Be(waiter.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetEmployeeHasSupportSkillOnPeakShift_ShouldFailWithoutPersisting()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "João", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var supportBartender = SeedEmployee(company.Id, "Marina", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Support).Value]);

        // Friday night 18-23h is Peak, Monday 11-19h is Idle
        SeedDemandSlot(company.Id, DayOfWeek.Monday, 12, 10m, 100m);
        SeedDemandSlot(company.Id, DayOfWeek.Friday, 20, 100m, 100m);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var peakWaiterShift = roster.AssignShift(waiter, WeekStart.AddDays(4), new TimeOnly(18, 0), new TimeOnly(23, 0), StationRole.Waiter).Value;
        var idleBarShift = roster.AssignShift(supportBartender, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Bar).Value;
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RequestShiftSwapCommand(
            roster.Id, waiter.Id, peakWaiterShift.Id, supportBartender.Id, idleBarShift.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InsufficientProficiencyForPeak");
        _swapRepository.AddedRequests.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSwapViolatesPreviousWeekInterShiftRest_ShouldFail()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var prevWeek = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart.AddDays(-7)).Value;
        // Sunday overnight 18:00 -> Monday 02:00 for empB
        prevWeek.AssignShift(empB, prevWeek.WeekEndDate, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(prevWeek);

        var currentRoster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        // Monday 10:00 for empA -> only 8h after empB's Sunday 02:00 end!
        var mondayShiftA = currentRoster.AssignShift(empA, WeekStart, new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter).Value;
        var wednesdayShiftB = currentRoster.AssignShift(empB, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(currentRoster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RequestShiftSwapCommand(
            currentRoster.Id, empA.Id, mondayShiftA.Id, empB.Id, wednesdayShiftB.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSwapViolatesForwardSundayRotation_ShouldFail()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var targetWeek = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart, SundayRotationPolicy.ThreeWeekCycle).Value;
        var sundayShiftA = targetWeek.AssignShift(empA, targetWeek.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var tuesdayShiftB = targetWeek.AssignShift(empB, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        // empB already works the next 2 Sundays (W+1 and W+2)
        var weekPlus1 = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart.AddDays(7), SundayRotationPolicy.ThreeWeekCycle).Value;
        weekPlus1.AssignShift(empB, weekPlus1.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        var weekPlus2 = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart.AddDays(14), SundayRotationPolicy.ThreeWeekCycle).Value;
        weekPlus2.AssignShift(empB, weekPlus2.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        _rosterRepository.Seed(targetWeek, weekPlus1, weekPlus2);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RequestShiftSwapCommand(
            targetWeek.Id, empA.Id, sundayShiftA.Id, empB.Id, tuesdayShiftB.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.SundayRotationViolated");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActiveRequestExistsOrShiftOwnershipMismatch_ShouldReturnConflict()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);
        var empC = SeedEmployee(company.Id, "Carla", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sA = roster.AssignShift(empA, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sB = roster.AssignShift(empB, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sC = roster.AssignShift(empC, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var existingSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id, empA.Id, sA.Id, empB.Id, sB.Id).Value;
        _swapRepository.Seed(existingSwap);

        var useCase = CreateUseCase();

        // Act
        var duplicateResult = await useCase.ExecuteAsync(new RequestShiftSwapCommand(
            roster.Id, empA.Id, sA.Id, empC.Id, sC.Id));

        var wrongOwnerResult = await useCase.ExecuteAsync(new RequestShiftSwapCommand(
            roster.Id, empB.Id, sC.Id, empC.Id, null));

        // Assert
        duplicateResult.IsFailure.Should().BeTrue();
        duplicateResult.Error.Code.Should().Be("ShiftSwap.ActiveRequestExists");

        wrongOwnerResult.IsFailure.Should().BeTrue();
        wrongOwnerResult.Error.Code.Should().Be("ShiftSwap.ShiftOwnershipMismatch");
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Teste",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole role,
        IEnumerable<SecondarySkill>? skills = null)
    {
        var employee = Employee.Create(_currentUserService.TenantId, companyId, name, role, skills).Value;
        _employeeRepository.Seed(employee);
        return employee;
    }

    private void SeedDemandSlot(Guid companyId, DayOfWeek day, int hour, decimal volume, decimal peakVolume)
    {
        var slot = DemandSlot.Create(_currentUserService.TenantId, companyId, day, hour, volume).Value;
        slot.EvaluateRelativeDemand(peakVolume);
        _demandSlotRepository.Seed(slot);
    }
}

