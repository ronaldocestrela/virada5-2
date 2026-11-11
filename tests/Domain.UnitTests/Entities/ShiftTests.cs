using Domain.Entities;
using Domain.Enums;

namespace Domain.UnitTests.Entities;

public class ShiftTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidRosterId = Guid.NewGuid();
    private static readonly Guid ValidEmployeeId = Guid.NewGuid();
    private static readonly DateOnly DefaultDate = new(2026, 11, 9);

    private static Shift CreateValidShift(
        DateOnly? date = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        Guid? employeeId = null,
        StationRole role = StationRole.Waiter,
        CoverageOrigin origin = CoverageOrigin.PrimaryRole)
    {
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            employeeId ?? ValidEmployeeId,
            date ?? DefaultDate,
            startTime ?? new TimeOnly(11, 0),
            endTime ?? new TimeOnly(19, 0),
            role,
            origin);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_WhenValidDaytimeParameters_ShouldReturnSuccessWithComputedProperties()
    {
        // Arrange
        var date = new DateOnly(2026, 11, 10);
        var start = new TimeOnly(11, 0);
        var end = new TimeOnly(19, 30);

        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            date,
            start,
            end,
            StationRole.Bar,
            CoverageOrigin.SecondarySkill);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(ValidTenantId);
        result.Value.RosterId.Should().Be(ValidRosterId);
        result.Value.EmployeeId.Should().Be(ValidEmployeeId);
        result.Value.Date.Should().Be(date);
        result.Value.StartTime.Should().Be(start);
        result.Value.EndTime.Should().Be(end);
        result.Value.AssignedRole.Should().Be(StationRole.Bar);
        result.Value.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        result.Value.CrossesMidnight.Should().BeFalse();
        result.Value.StartDateTime.Should().Be(new DateTime(2026, 11, 10, 11, 0, 0));
        result.Value.EndDateTime.Should().Be(new DateTime(2026, 11, 10, 19, 30, 0));
        result.Value.Duration.Should().Be(TimeSpan.FromHours(8.5));
    }

    [Fact]
    public void Create_WhenOvernightShiftCrossesMidnight_ShouldComputeNextDayEndDateTime()
    {
        // Arrange
        var date = new DateOnly(2026, 11, 13);
        var start = new TimeOnly(18, 0);
        var end = new TimeOnly(2, 0);

        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            date,
            start,
            end,
            StationRole.Kitchen,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CrossesMidnight.Should().BeTrue();
        result.Value.StartDateTime.Should().Be(new DateTime(2026, 11, 13, 18, 0, 0));
        result.Value.EndDateTime.Should().Be(new DateTime(2026, 11, 14, 2, 0, 0));
        result.Value.Duration.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void Create_WhenDurationIsExactlyMaxAllowed12Hours_ShouldReturnSuccess()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(22, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Duration.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            Guid.Empty,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.EmptyTenantId");
    }

    [Fact]
    public void Create_WhenRosterIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            Guid.Empty,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.EmptyRosterId");
    }

    [Fact]
    public void Create_WhenEmployeeIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            Guid.Empty,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.EmptyEmployeeId");
    }

    [Fact]
    public void Create_WhenDateIsDefault_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            default,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.InvalidDate");
    }

    [Fact]
    public void Create_WhenStartTimeEqualsEndTime_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(12, 0),
            new TimeOnly(12, 0),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.InvalidTimeRange");
    }

    [Fact]
    public void Create_WhenDurationExceeds12Hours_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(9, 0),
            new TimeOnly(21, 1),
            StationRole.Waiter,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.DurationExceedsLimit");
    }

    [Fact]
    public void Create_WhenAssignedRoleIsInvalid_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            (StationRole)999,
            CoverageOrigin.PrimaryRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.InvalidStationRole");
    }

    [Fact]
    public void Create_WhenCoverageOriginIsInvalid_ShouldReturnValidationFailure()
    {
        // Act
        var result = Shift.Create(
            ValidTenantId,
            ValidRosterId,
            ValidEmployeeId,
            DefaultDate,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            StationRole.Waiter,
            (CoverageOrigin)999);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Shift.InvalidCoverageOrigin");
    }

    [Fact]
    public void OverlapsWith_WhenSameDayPartialOverlap_ShouldReturnTrue()
    {
        // Arrange
        var shiftA = CreateValidShift(DefaultDate, new TimeOnly(11, 0), new TimeOnly(16, 0));
        var shiftB = CreateValidShift(DefaultDate, new TimeOnly(15, 0), new TimeOnly(21, 0));

        // Act & Assert
        shiftA.OverlapsWith(shiftB).Should().BeTrue();
        shiftB.OverlapsWith(shiftA).Should().BeTrue();
    }

    [Fact]
    public void OverlapsWith_WhenOvernightShiftOverlapsNextDayShift_ShouldReturnTrue()
    {
        // Arrange
        var overnightShift = CreateValidShift(DefaultDate, new TimeOnly(18, 0), new TimeOnly(2, 0));
        var nextDayEarlyShift = CreateValidShift(DefaultDate.AddDays(1), new TimeOnly(1, 0), new TimeOnly(9, 0));

        // Act & Assert
        overnightShift.OverlapsWith(nextDayEarlyShift).Should().BeTrue();
        nextDayEarlyShift.OverlapsWith(overnightShift).Should().BeTrue();
    }

    [Fact]
    public void OverlapsWith_WhenBackToBackContiguous_ShouldReturnFalse()
    {
        // Arrange
        var morningShift = CreateValidShift(DefaultDate, new TimeOnly(8, 0), new TimeOnly(12, 0));
        var afternoonShift = CreateValidShift(DefaultDate, new TimeOnly(12, 0), new TimeOnly(16, 0));

        // Act & Assert
        morningShift.OverlapsWith(afternoonShift).Should().BeFalse();
        afternoonShift.OverlapsWith(morningShift).Should().BeFalse();
    }

    [Fact]
    public void MeetsInterShiftRestWith_WhenGapIsExactly11Hours_ShouldReturnTrue()
    {
        // Arrange
        var day1 = CreateValidShift(DefaultDate, new TimeOnly(11, 0), new TimeOnly(19, 0));
        var day2 = CreateValidShift(DefaultDate.AddDays(1), new TimeOnly(6, 0), new TimeOnly(14, 0));

        // Act & Assert
        day1.MeetsInterShiftRestWith(day2).Should().BeTrue();
        day2.MeetsInterShiftRestWith(day1).Should().BeTrue();
    }

    [Fact]
    public void MeetsInterShiftRestWith_WhenGapIsLessThan11Hours_ShouldReturnFalse()
    {
        // Arrange
        var day1 = CreateValidShift(DefaultDate, new TimeOnly(14, 0), new TimeOnly(23, 0));
        var day2 = CreateValidShift(DefaultDate.AddDays(1), new TimeOnly(9, 59), new TimeOnly(17, 59));

        // Act & Assert
        day1.MeetsInterShiftRestWith(day2).Should().BeFalse();
        day2.MeetsInterShiftRestWith(day1).Should().BeFalse();
    }

    [Fact]
    public void MeetsInterShiftRestWith_WhenOvernightEndsAt0200AndNextStartsAt1300_ShouldReturnTrue()
    {
        // Arrange
        var overnightShift = CreateValidShift(DefaultDate, new TimeOnly(18, 0), new TimeOnly(2, 0));
        var nextDayShift = CreateValidShift(DefaultDate.AddDays(1), new TimeOnly(13, 0), new TimeOnly(21, 0));

        // Act & Assert
        overnightShift.MeetsInterShiftRestWith(nextDayShift).Should().BeTrue();
        nextDayShift.MeetsInterShiftRestWith(overnightShift).Should().BeTrue();
    }

    [Fact]
    public void MeetsInterShiftRestWith_WhenOvernightEndsAt0200AndNextStartsAt1230_ShouldReturnFalse()
    {
        // Arrange
        var overnightShift = CreateValidShift(DefaultDate, new TimeOnly(18, 0), new TimeOnly(2, 0));
        var nextDayShift = CreateValidShift(DefaultDate.AddDays(1), new TimeOnly(12, 30), new TimeOnly(20, 30));

        // Act & Assert
        overnightShift.MeetsInterShiftRestWith(nextDayShift).Should().BeFalse();
    }

    [Fact]
    public void MeetsInterShiftRestWith_WhenShiftsOverlap_ShouldReturnFalse()
    {
        // Arrange
        var shiftA = CreateValidShift(DefaultDate, new TimeOnly(10, 0), new TimeOnly(15, 0));
        var shiftB = CreateValidShift(DefaultDate, new TimeOnly(14, 0), new TimeOnly(19, 0));

        // Act & Assert
        shiftA.MeetsInterShiftRestWith(shiftB).Should().BeFalse();
    }
}

