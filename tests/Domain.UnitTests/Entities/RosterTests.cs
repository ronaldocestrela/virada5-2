using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.UnitTests.Entities;

public class RosterTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidCompanyId = Guid.NewGuid();
    // 2026-11-09 is a Monday; week ends on Sunday 2026-11-15
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 9);

    private static Roster CreateValidRoster(
        DateOnly? weekStart = null,
        SundayRotationPolicy? sundayPolicy = null)
    {
        var result = Roster.Create(
            ValidTenantId,
            ValidCompanyId,
            weekStart ?? DefaultWeekStart,
            sundayPolicy);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Employee CreateValidEmployee(
        string name = "Lucas Almeida",
        StationRole primaryRole = StationRole.Waiter,
        IEnumerable<SecondarySkill>? secondarySkills = null,
        IEnumerable<DayOfWeek>? preferredDaysOff = null,
        IEnumerable<DayOfWeek>? unavailableDays = null,
        Guid? tenantId = null,
        Guid? companyId = null)
    {
        var result = Employee.Create(
            tenantId ?? ValidTenantId,
            companyId ?? ValidCompanyId,
            name,
            primaryRole,
            secondarySkills,
            preferredDaysOff,
            unavailableDays);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_WhenValidParameters_ShouldInitializeDraftRosterWithSevenDayCycle()
    {
        // Arrange
        var policy = SundayRotationPolicy.BiweeklyCycle;

        // Act
        var result = Roster.Create(ValidTenantId, ValidCompanyId, DefaultWeekStart, policy);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(ValidTenantId);
        result.Value.CompanyId.Should().Be(ValidCompanyId);
        result.Value.WeekStartDate.Should().Be(new DateOnly(2026, 11, 9));
        result.Value.WeekEndDate.Should().Be(new DateOnly(2026, 11, 15));
        result.Value.Status.Should().Be(RosterStatus.Draft);
        result.Value.SundayPolicy.Should().Be(policy);
        result.Value.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void Create_WhenSundayPolicyOmitted_ShouldDefaultToThreeWeekCycle()
    {
        // Act
        var result = Roster.Create(ValidTenantId, ValidCompanyId, DefaultWeekStart);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SundayPolicy.Should().Be(SundayRotationPolicy.ThreeWeekCycle);
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Roster.Create(Guid.Empty, ValidCompanyId, DefaultWeekStart);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.EmptyTenantId");
    }

    [Fact]
    public void Create_WhenCompanyIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Roster.Create(ValidTenantId, Guid.Empty, DefaultWeekStart);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.EmptyCompanyId");
    }

    [Fact]
    public void Create_WhenWeekStartDateIsDefault_ShouldReturnValidationFailure()
    {
        // Act
        var result = Roster.Create(ValidTenantId, ValidCompanyId, default);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InvalidWeekStartDate");
    }

    [Fact]
    public void Create_WhenSundayPolicyIsDefaultStruct_ShouldReturnValidationFailure()
    {
        // Act
        var result = Roster.Create(ValidTenantId, ValidCompanyId, DefaultWeekStart, default(SundayRotationPolicy));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InvalidSundayPolicy");
    }

    [Fact]
    public void AssignShift_WhenValidPrimaryRole_ShouldAddShiftWithPrimaryOrigin()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee(primaryRole: StationRole.Waiter);
        var monday = DefaultWeekStart;

        // Act
        var result = roster.AssignShift(
            employee,
            monday,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RosterId.Should().Be(roster.Id);
        result.Value.TenantId.Should().Be(roster.TenantId);
        result.Value.EmployeeId.Should().Be(employee.Id);
        result.Value.CoverageOrigin.Should().Be(CoverageOrigin.PrimaryRole);
        roster.Shifts.Should().ContainSingle().Which.Should().Be(result.Value);
    }

    [Fact]
    public void AssignShift_WhenEligibleBySecondarySkill_ShouldAddShiftWithSecondaryOrigin()
    {
        // Arrange
        var roster = CreateValidRoster();
        var barSkill = SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value;
        var employee = CreateValidEmployee(primaryRole: StationRole.Waiter, secondarySkills: [barSkill]);

        // Act
        var result = roster.AssignShift(
            employee,
            DefaultWeekStart,
            new TimeOnly(18, 0),
            new TimeOnly(2, 0),
            StationRole.Bar,
            DemandClassification.Peak);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        result.Value.CrossesMidnight.Should().BeTrue();
    }

    [Fact]
    public void AssignShift_WhenEmployeeBelongsToDifferentCompanyOrTenant_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var otherCompanyEmployee = CreateValidEmployee(companyId: Guid.NewGuid());
        var otherTenantEmployee = CreateValidEmployee(tenantId: Guid.NewGuid());

        // Act
        var companyMismatch = roster.AssignShift(otherCompanyEmployee, DefaultWeekStart, new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter);
        var tenantMismatch = roster.AssignShift(otherTenantEmployee, DefaultWeekStart, new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter);

        // Assert
        companyMismatch.IsFailure.Should().BeTrue();
        companyMismatch.Error.Code.Should().Be("Roster.EmployeeCompanyMismatch");
        tenantMismatch.IsFailure.Should().BeTrue();
        tenantMismatch.Error.Code.Should().Be("Roster.EmployeeCompanyMismatch");
    }

    [Fact]
    public void AssignShift_WhenDateIsOutsideRosterWeek_ShouldReturnValidationFailure()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();

        // Act
        var beforeResult = roster.AssignShift(employee, DefaultWeekStart.AddDays(-1), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter);
        var afterResult = roster.AssignShift(employee, DefaultWeekStart.AddDays(7), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter);

        // Assert
        beforeResult.IsFailure.Should().BeTrue();
        beforeResult.Error.Code.Should().Be("Roster.DateOutOfWeekRange");
        afterResult.IsFailure.Should().BeTrue();
        afterResult.Error.Code.Should().Be("Roster.DateOutOfWeekRange");
    }

    [Fact]
    public void AssignShift_WhenEmployeeIsInactiveOrUnavailableOrLacksProficiency_ShouldPropagateEligibilityError()
    {
        // Arrange
        var roster = CreateValidRoster();
        var supportBar = SecondarySkill.Create(StationRole.Bar, SkillProficiency.Support).Value;
        var employee = CreateValidEmployee(
            primaryRole: StationRole.Waiter,
            secondarySkills: [supportBar],
            unavailableDays: [DayOfWeek.Monday]);

        // Act: unavailable day
        var unavailableResult = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter);
        // Act: support skill on peak
        var peakResult = roster.AssignShift(employee, DefaultWeekStart.AddDays(1), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Bar, DemandClassification.Peak);

        // Assert
        unavailableResult.IsFailure.Should().BeTrue();
        unavailableResult.Error.Code.Should().Be("Employee.UnavailableOnDay");
        peakResult.IsFailure.Should().BeTrue();
        peakResult.Error.Code.Should().Be("Employee.InsufficientProficiencyForPeak");
    }

    [Fact]
    public void AssignShift_WhenOverlappingExistingShift_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(10, 0), new TimeOnly(15, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act
        var overlapResult = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(14, 0), new TimeOnly(19, 0), StationRole.Waiter);

        // Assert
        overlapResult.IsFailure.Should().BeTrue();
        overlapResult.Error.Code.Should().Be("Roster.ShiftOverlap");
        roster.Shifts.Should().HaveCount(1);
    }

    [Fact]
    public void AssignShift_WhenInterShiftRestIsLessThan11HoursWithinWeek_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act: ends 02:00 on Tuesday, next starts 12:30 on Tuesday (10h30 rest)
        var result = roster.AssignShift(employee, DefaultWeekStart.AddDays(1), new TimeOnly(12, 30), new TimeOnly(20, 30), StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
        roster.Shifts.Should().HaveCount(1);
    }

    [Fact]
    public void AssignShift_WhenInterShiftRestIsExactly11Hours_ShouldReturnSuccess()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act: ends 02:00 on Tuesday, next starts 13:00 on Tuesday (11h rest)
        var result = roster.AssignShift(employee, DefaultWeekStart.AddDays(1), new TimeOnly(13, 0), new TimeOnly(21, 0), StationRole.Waiter);

        // Assert
        result.IsSuccess.Should().BeTrue();
        roster.Shifts.Should().HaveCount(2);
    }

    [Fact]
    public void AssignShift_WhenAddingEarlierShiftThatViolates11HourRestBeforeExistingShift_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart.AddDays(1), new TimeOnly(8, 0), new TimeOnly(16, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act: ends 22:00 on Monday -> only 10h before Tuesday 08:00
        var result = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(14, 0), new TimeOnly(22, 0), StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
    }

    [Fact]
    public void AssignShift_WhenBoundaryContextPreviousShiftEndsTooLate_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        var sundayPrevEnd = new DateTime(2026, 11, 8, 23, 30, 0);
        var context = EmployeeWeekBoundaryContext.Create(employee.Id, previousWeekLastShiftEnd: sundayPrevEnd).Value;

        // Act: Monday 09:00 is only 9h30 after Sunday 23:30
        var result = roster.AssignShift(
            employee,
            DefaultWeekStart,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            StationRole.Waiter,
            boundaryContext: context);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
    }

    [Fact]
    public void AssignShift_WhenBoundaryContextNextShiftStartsTooEarly_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        var nextMondayStart = new DateTime(2026, 11, 16, 8, 0, 0);
        var context = EmployeeWeekBoundaryContext.Create(employee.Id, nextWeekFirstShiftStart: nextMondayStart).Value;
        var sunday = DefaultWeekStart.AddDays(6);

        // Act: Sunday 15:00-23:00 leaves only 9h before next Monday 08:00
        var result = roster.AssignShift(
            employee,
            sunday,
            new TimeOnly(15, 0),
            new TimeOnly(23, 0),
            StationRole.Waiter,
            boundaryContext: context);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
    }

    [Fact]
    public void AssignShift_WhenFiveDaysAssignedAndSixthDayAttempted_ShouldEnforceTwoDaysOffPerWeek()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();

        for (var i = 0; i < 5; i++)
        {
            var assignResult = roster.AssignShift(
                employee,
                DefaultWeekStart.AddDays(i),
                new TimeOnly(11, 0),
                new TimeOnly(19, 0),
                StationRole.Waiter);
            assignResult.IsSuccess.Should().BeTrue();
        }

        // Act: 6th distinct day violates 5x2 (minimum 2 days off)
        var sixthDayResult = roster.AssignShift(
            employee,
            DefaultWeekStart.AddDays(5),
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter);

        // Assert
        sixthDayResult.IsFailure.Should().BeTrue();
        sixthDayResult.Error.Code.Should().Be("Roster.WeeklyRestDaysViolated");
        roster.GetEmployeeWorkedDaysCount(employee.Id).Should().Be(5);
        roster.GetEmployeeDaysOff(employee.Id).Should().BeEquivalentTo([
            DefaultWeekStart.AddDays(5),
            DefaultWeekStart.AddDays(6)
        ]);
    }

    [Fact]
    public void AssignShift_WhenMultipleShiftsOnSameDayRespect11HourRest_ShouldCountAsSingleWorkedDay()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();

        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(8, 0), new TimeOnly(11, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act: second shift same day starting at 22:00 (11h after 11:00)
        var secondSameDay = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(22, 0), new TimeOnly(2, 0), StationRole.Waiter);

        // Assert
        secondSameDay.IsSuccess.Should().BeTrue();
        roster.GetEmployeeWorkedDaysCount(employee.Id).Should().Be(1);
    }

    [Fact]
    public void AssignShift_WhenSundayRotationLimitReached_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster(sundayPolicy: SundayRotationPolicy.ThreeWeekCycle);
        var employee = CreateValidEmployee();
        var sunday = DefaultWeekStart.AddDays(6);
        var context = EmployeeWeekBoundaryContext.Create(employee.Id, consecutiveSundaysWorkedBeforeWeek: 2).Value;

        // Act
        var result = roster.AssignShift(
            employee,
            sunday,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter,
            boundaryContext: context);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.SundayRotationViolated");
    }

    [Fact]
    public void AssignShift_WhenBiweeklySundayPolicyAllowsAfterZeroStreak_ShouldReturnSuccess()
    {
        // Arrange
        var roster = CreateValidRoster(sundayPolicy: SundayRotationPolicy.BiweeklyCycle);
        var employee = CreateValidEmployee();
        var sunday = DefaultWeekStart.AddDays(6);
        var context = EmployeeWeekBoundaryContext.Create(employee.Id, consecutiveSundaysWorkedBeforeWeek: 0).Value;

        // Act
        var result = roster.AssignShift(
            employee,
            sunday,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter,
            boundaryContext: context);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AssignShift_WhenBoundaryContextBelongsToAnotherEmployee_ShouldReturnValidationFailure()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        var wrongContext = EmployeeWeekBoundaryContext.Create(Guid.NewGuid(), consecutiveSundaysWorkedBeforeWeek: 1).Value;

        // Act
        var result = roster.AssignShift(
            employee,
            DefaultWeekStart,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter,
            boundaryContext: wrongContext);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.BoundaryContextMismatch");
    }

    [Fact]
    public void RemoveShift_WhenDraftAndShiftExists_ShouldRemoveShift()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        var shift = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        // Act
        var result = roster.RemoveShift(shift.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        roster.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void RemoveShift_WhenShiftDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        var roster = CreateValidRoster();

        // Act
        var result = roster.RemoveShift(Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.ShiftNotFound");
    }

    [Fact]
    public void Publish_WhenValidDraftWithShifts_ShouldTransitionToPublishedAndBlockDirectEdits()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act
        var publishResult = roster.Publish();
        var assignAfterPublish = roster.AssignShift(employee, DefaultWeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        var removeAfterPublish = roster.RemoveShift(roster.Shifts.Single().Id);

        // Assert
        publishResult.IsSuccess.Should().BeTrue();
        roster.Status.Should().Be(RosterStatus.Published);
        assignAfterPublish.IsFailure.Should().BeTrue();
        assignAfterPublish.Error.Code.Should().Be("Roster.NotEditable");
        removeAfterPublish.IsFailure.Should().BeTrue();
        removeAfterPublish.Error.Code.Should().Be("Roster.NotEditable");
    }

    [Fact]
    public void Publish_WhenRosterHasNoShifts_ShouldReturnValidationFailure()
    {
        // Arrange
        var roster = CreateValidRoster();

        // Act
        var result = roster.Publish();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.EmptyShifts");
        roster.Status.Should().Be(RosterStatus.Draft);
    }

    [Fact]
    public void Publish_WhenBoundaryContextViolatesSundayRotation_ShouldFailAndRemainDraft()
    {
        // Arrange
        var roster = CreateValidRoster(sundayPolicy: SundayRotationPolicy.BiweeklyCycle);
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        var context = EmployeeWeekBoundaryContext.Create(employee.Id, consecutiveSundaysWorkedBeforeWeek: 1).Value;

        // Act
        var result = roster.Publish([context]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.SundayRotationViolated");
        roster.Status.Should().Be(RosterStatus.Draft);
    }

    [Fact]
    public void SwapShiftEmployees_WhenPublishedAndCltCompliant_ShouldSwapEmployeesAndUpdateCoverageOrigin()
    {
        // Arrange
        var roster = CreateValidRoster();
        var waiter = CreateValidEmployee("João", StationRole.Waiter, [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bartender = CreateValidEmployee("Marina", StationRole.Bar, [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]);

        var waiterShift = roster.AssignShift(waiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var barShift = roster.AssignShift(bartender, DefaultWeekStart.AddDays(2), new TimeOnly(16, 0), new TimeOnly(0, 0), StationRole.Bar).Value;
        roster.Publish().IsSuccess.Should().BeTrue();

        // Act
        var swapResult = roster.SwapShiftEmployees(waiterShift.Id, bartender, barShift.Id, waiter);

        // Assert
        swapResult.IsSuccess.Should().BeTrue();
        waiterShift.EmployeeId.Should().Be(bartender.Id);
        waiterShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        barShift.EmployeeId.Should().Be(waiter.Id);
        barShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
    }

    [Fact]
    public void SwapShiftEmployees_WhenSwapViolatesInterShiftRest_ShouldFailWithoutMutatingOriginalShifts()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employeeA = CreateValidEmployee("Ana", StationRole.Waiter);
        var employeeB = CreateValidEmployee("Bruno", StationRole.Waiter);

        var shiftA1 = roster.AssignShift(employeeA, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        // employeeB works overnight Monday 18:00-02:00 (ends Tuesday 02:00)
        roster.AssignShift(employeeB, DefaultWeekStart, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        // employeeB also works Wednesday 11:00-19:00; employeeA works Tuesday 10:00-18:00
        var shiftA2 = roster.AssignShift(employeeA, DefaultWeekStart.AddDays(1), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter).Value;
        var shiftB2 = roster.AssignShift(employeeB, DefaultWeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        // Act: swapping shiftA2 (Tue 10:00) to employeeB violates 11h rest after B's Mon 18:00-02:00 shift
        var swapResult = roster.SwapShiftEmployees(shiftA2.Id, employeeB, shiftB2.Id, employeeA);

        // Assert
        swapResult.IsFailure.Should().BeTrue();
        swapResult.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
        shiftA2.EmployeeId.Should().Be(employeeA.Id);
        shiftB2.EmployeeId.Should().Be(employeeB.Id);
        shiftA1.EmployeeId.Should().Be(employeeA.Id);
    }

    [Fact]
    public void SwapShiftEmployees_WhenArchived_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employeeA = CreateValidEmployee("Ana", StationRole.Waiter);
        var employeeB = CreateValidEmployee("Bruno", StationRole.Waiter);
        var s1 = roster.AssignShift(employeeA, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s2 = roster.AssignShift(employeeB, DefaultWeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Archive().IsSuccess.Should().BeTrue();

        // Act
        var result = roster.SwapShiftEmployees(s1.Id, employeeB, s2.Id, employeeA);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.ArchivedNotModifiable");
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_ShouldReturnConflict()
    {
        // Arrange
        var roster = CreateValidRoster();
        roster.Archive().IsSuccess.Should().BeTrue();

        // Act
        var secondArchive = roster.Archive();

        // Assert
        secondArchive.IsFailure.Should().BeTrue();
        secondArchive.Error.Code.Should().Be("Roster.AlreadyArchived");
    }

    [Fact]
    public void ValidateMultiWeekSundayRotation_WhenEmployeeExceedsConsecutiveSundaysAcrossWeeks_ShouldReturnConflict()
    {
        // Arrange
        var employee = CreateValidEmployee();
        var week1 = CreateValidRoster(DefaultWeekStart, SundayRotationPolicy.ThreeWeekCycle);
        var week2 = CreateValidRoster(DefaultWeekStart.AddDays(7), SundayRotationPolicy.ThreeWeekCycle);
        var week3 = CreateValidRoster(DefaultWeekStart.AddDays(14), SundayRotationPolicy.ThreeWeekCycle);

        week1.AssignShift(employee, week1.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        week2.AssignShift(employee, week2.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        week3.AssignShift(employee, week3.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act
        var result = Roster.ValidateMultiWeekSundayRotation([week1, week2, week3], employee.Id);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.SundayRotationViolated");
    }

    [Fact]
    public void ValidateMultiWeekSundayRotation_WhenEmployeeRestsOnThirdSunday_ShouldReturnSuccess()
    {
        // Arrange
        var employee = CreateValidEmployee();
        var week1 = CreateValidRoster(DefaultWeekStart, SundayRotationPolicy.ThreeWeekCycle);
        var week2 = CreateValidRoster(DefaultWeekStart.AddDays(7), SundayRotationPolicy.ThreeWeekCycle);
        var week3 = CreateValidRoster(DefaultWeekStart.AddDays(14), SundayRotationPolicy.ThreeWeekCycle);

        week1.AssignShift(employee, week1.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        week2.AssignShift(employee, week2.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        // Week 3 works Saturday instead of Sunday
        week3.AssignShift(employee, week3.WeekEndDate.AddDays(-1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act
        var result = Roster.ValidateMultiWeekSundayRotation([week1, week2, week3], employee.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AssignShift_WhenEmployeeIsNull_ShouldReturnValidationFailure()
    {
        // Arrange
        var roster = CreateValidRoster();

        // Act
        var result = roster.AssignShift(null!, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.NullEmployee");
    }

    [Fact]
    public void SwapShiftEmployees_WhenSameShiftIdOrMissingShift_ShouldReturnFailure()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        var shift = roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        // Act
        var sameShiftResult = roster.SwapShiftEmployees(shift.Id, employee, shift.Id, employee);
        var missingShiftResult = roster.SwapShiftEmployees(shift.Id, employee, Guid.NewGuid(), employee);

        // Assert
        sameShiftResult.IsFailure.Should().BeTrue();
        sameShiftResult.Error.Code.Should().Be("Roster.SameShiftSwap");
        missingShiftResult.IsFailure.Should().BeTrue();
        missingShiftResult.Error.Code.Should().Be("Roster.ShiftNotFound");
    }

    [Fact]
    public void SwapShiftEmployees_WhenReassigningBothShiftsToSameEmployeeWithValidRest_ShouldSucceed()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employeeA = CreateValidEmployee("Ana", StationRole.Waiter);
        var employeeB = CreateValidEmployee("Bruno", StationRole.Waiter);
        var employeeC = CreateValidEmployee("Carla", StationRole.Waiter);

        var s1 = roster.AssignShift(employeeA, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s2 = roster.AssignShift(employeeB, DefaultWeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        // Act
        var result = roster.SwapShiftEmployees(s1.Id, employeeC, s2.Id, employeeC);

        // Assert
        result.IsSuccess.Should().BeTrue();
        s1.EmployeeId.Should().Be(employeeC.Id);
        s2.EmployeeId.Should().Be(employeeC.Id);
    }

    [Fact]
    public void Publish_WhenAlreadyPublishedOrBoundaryContextInvalid_ShouldReturnFailure()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employee = CreateValidEmployee();
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        var duplicateContext = EmployeeWeekBoundaryContext.Create(employee.Id).Value;

        // Act
        var duplicateResult = roster.Publish([duplicateContext, duplicateContext]);
        var defaultContextResult = roster.Publish([default(EmployeeWeekBoundaryContext)]);
        var validPublish = roster.Publish();
        var republish = roster.Publish();

        // Assert
        duplicateResult.IsFailure.Should().BeTrue();
        duplicateResult.Error.Code.Should().Be("Roster.DuplicateBoundaryContext");
        defaultContextResult.IsFailure.Should().BeTrue();
        defaultContextResult.Error.Code.Should().Be("Roster.EmptyEmployeeId");
        validPublish.IsSuccess.Should().BeTrue();
        republish.IsFailure.Should().BeTrue();
        republish.Error.Code.Should().Be("Roster.NotDraft");
    }

    [Fact]
    public void ValidateMultiWeekSundayRotation_WhenSequenceHasGapOrInvalidInputs_ShouldHandleProperly()
    {
        // Arrange
        var employee = CreateValidEmployee();
        var week1 = CreateValidRoster(DefaultWeekStart, SundayRotationPolicy.BiweeklyCycle);
        var week3WithGap = CreateValidRoster(DefaultWeekStart.AddDays(14), SundayRotationPolicy.BiweeklyCycle);
        var otherCompanyWeek = Roster.Create(ValidTenantId, Guid.NewGuid(), DefaultWeekStart.AddDays(7)).Value;

        week1.AssignShift(employee, week1.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        week3WithGap.AssignShift(employee, week3WithGap.WeekEndDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        // Act & Assert
        Roster.ValidateMultiWeekSundayRotation([], employee.Id).Error.Code.Should().Be("Roster.EmptyRosterSequence");
        Roster.ValidateMultiWeekSundayRotation([week1], Guid.Empty).Error.Code.Should().Be("Roster.EmptyEmployeeId");
        Roster.ValidateMultiWeekSundayRotation([week1], employee.Id, -1).Error.Code.Should().Be("Roster.NegativeSundayStreak");
        Roster.ValidateMultiWeekSundayRotation([week1, otherCompanyWeek], employee.Id).Error.Code.Should().Be("Roster.SequenceCompanyMismatch");
        Roster.ValidateMultiWeekSundayRotation([week3WithGap, week1], employee.Id).Error.Code.Should().Be("Roster.InvalidSequenceOrder");
        // Intermediate week skipped resets streak, so biweekly policy allows week 3 Sunday
        Roster.ValidateMultiWeekSundayRotation([week1, week3WithGap], employee.Id).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSwapShiftEmployees_WhenValid_ShouldSucceedWithoutMutatingShiftOwners()
    {
        // Arrange
        var roster = CreateValidRoster();
        var waiter = CreateValidEmployee("João", StationRole.Waiter, [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bartender = CreateValidEmployee("Marina", StationRole.Bar, [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]);

        var waiterShift = roster.AssignShift(waiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var barShift = roster.AssignShift(bartender, DefaultWeekStart.AddDays(2), new TimeOnly(16, 0), new TimeOnly(0, 0), StationRole.Bar).Value;

        // Act
        var validation = roster.ValidateSwapShiftEmployees(
            waiterShift.Id,
            bartender,
            barShift.Id,
            waiter,
            DemandClassification.Idle,
            DemandClassification.Peak);

        // Assert
        validation.IsSuccess.Should().BeTrue();
        validation.Value.FirstOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        validation.Value.SecondOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        waiterShift.EmployeeId.Should().Be(waiter.Id);
        barShift.EmployeeId.Should().Be(bartender.Id);
    }

    [Fact]
    public void SwapShiftEmployees_WithDistinctShiftDemandLevels_ShouldRejectSupportProficiencyOnPeakShift()
    {
        // Arrange
        var roster = CreateValidRoster();
        var waiter = CreateValidEmployee("João", StationRole.Waiter, [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bartenderWithSupportWaiter = CreateValidEmployee(
            "Marina",
            StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Support).Value]);

        var peakWaiterShift = roster.AssignShift(waiter, DefaultWeekStart.AddDays(4), new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).Value;
        var idleBarShift = roster.AssignShift(bartenderWithSupportWaiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Bar).Value;

        // Act
        var result = roster.SwapShiftEmployees(
            peakWaiterShift.Id,
            bartenderWithSupportWaiter,
            idleBarShift.Id,
            waiter,
            firstShiftDemandLevel: DemandClassification.Peak,
            secondShiftDemandLevel: DemandClassification.Idle);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InsufficientProficiencyForPeak");
        peakWaiterShift.EmployeeId.Should().Be(waiter.Id);
        idleBarShift.EmployeeId.Should().Be(bartenderWithSupportWaiter.Id);
    }

    [Fact]
    public void ReassignShiftEmployee_WhenReplacementEligibleAndWithinFiveDays_ShouldUpdateOwnerAndCoverageOrigin()
    {
        // Arrange
        var roster = CreateValidRoster();
        var waiter = CreateValidEmployee("João", StationRole.Waiter);
        var bartender = CreateValidEmployee("Marina", StationRole.Bar, [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]);

        var waiterShift = roster.AssignShift(waiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Publish().IsSuccess.Should().BeTrue();

        // Act
        var validation = roster.ValidateReassignShiftEmployee(waiterShift.Id, bartender, DemandClassification.Peak);
        var reassignResult = roster.ReassignShiftEmployee(waiterShift.Id, bartender, DemandClassification.Peak);

        // Assert
        validation.IsSuccess.Should().BeTrue();
        validation.Value.Should().Be(CoverageOrigin.SecondarySkill);
        reassignResult.IsSuccess.Should().BeTrue();
        waiterShift.EmployeeId.Should().Be(bartender.Id);
        waiterShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
    }

    [Fact]
    public void ReassignShiftEmployee_WhenExceedsFiveDaysOrSameEmployeeOrArchived_ShouldReturnFailure()
    {
        // Arrange
        var roster = CreateValidRoster();
        var employeeA = CreateValidEmployee("Ana", StationRole.Waiter);
        var employeeB = CreateValidEmployee("Bruno", StationRole.Waiter);

        var shiftA = roster.AssignShift(employeeA, DefaultWeekStart.AddDays(5), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        for (var i = 0; i < 5; i++)
        {
            roster.AssignShift(employeeB, DefaultWeekStart.AddDays(i), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter)
                .IsSuccess.Should().BeTrue();
        }

        // Act
        var sameEmployeeResult = roster.ReassignShiftEmployee(shiftA.Id, employeeA);
        var missingShiftResult = roster.ReassignShiftEmployee(Guid.NewGuid(), employeeB);
        var sixDaysResult = roster.ReassignShiftEmployee(shiftA.Id, employeeB);

        roster.Archive().IsSuccess.Should().BeTrue();
        var archivedResult = roster.ReassignShiftEmployee(shiftA.Id, employeeB);

        // Assert
        sameEmployeeResult.Error.Code.Should().Be("Roster.SameEmployeeReassignment");
        missingShiftResult.Error.Code.Should().Be("Roster.ShiftNotFound");
        sixDaysResult.Error.Code.Should().Be("Roster.WeeklyRestDaysViolated");
        archivedResult.Error.Code.Should().Be("Roster.ArchivedNotModifiable");
        shiftA.EmployeeId.Should().Be(employeeA.Id);
    }
}
