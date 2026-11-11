using Domain.ValueObjects;

namespace Domain.UnitTests.ValueObjects;

public class EmployeeWeekBoundaryContextTests
{
    [Fact]
    public void Create_WhenValidFullParameters_ShouldReturnSuccess()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var prevEnd = new DateTime(2026, 11, 8, 22, 0, 0);
        var nextStart = new DateTime(2026, 11, 16, 10, 0, 0);

        // Act
        var result = EmployeeWeekBoundaryContext.Create(
            employeeId,
            consecutiveSundaysWorkedBeforeWeek: 1,
            previousWeekLastShiftEnd: prevEnd,
            nextWeekFirstShiftStart: nextStart);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.EmployeeId.Should().Be(employeeId);
        result.Value.ConsecutiveSundaysWorkedBeforeWeek.Should().Be(1);
        result.Value.PreviousWeekLastShiftEnd.Should().Be(prevEnd);
        result.Value.NextWeekFirstShiftStart.Should().Be(nextStart);
    }

    [Fact]
    public void Create_WhenOnlyEmployeeIdProvided_ShouldReturnSuccessWithDefaults()
    {
        // Arrange
        var employeeId = Guid.NewGuid();

        // Act
        var result = EmployeeWeekBoundaryContext.Create(employeeId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ConsecutiveSundaysWorkedBeforeWeek.Should().Be(0);
        result.Value.PreviousWeekLastShiftEnd.Should().BeNull();
        result.Value.NextWeekFirstShiftStart.Should().BeNull();
    }

    [Fact]
    public void Create_WhenEmployeeIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = EmployeeWeekBoundaryContext.Create(Guid.Empty);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EmployeeWeekBoundaryContext.EmptyEmployeeId");
    }

    [Fact]
    public void Create_WhenConsecutiveSundaysIsNegative_ShouldReturnValidationFailure()
    {
        // Act
        var result = EmployeeWeekBoundaryContext.Create(
            Guid.NewGuid(),
            consecutiveSundaysWorkedBeforeWeek: -1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EmployeeWeekBoundaryContext.NegativeSundayStreak");
    }

    [Fact]
    public void Create_WhenPreviousShiftEndIsNotBeforeNextShiftStart_ShouldReturnValidationFailure()
    {
        // Arrange
        var employeeId = Guid.NewGuid();
        var boundary = new DateTime(2026, 11, 10, 12, 0, 0);

        // Act
        var result = EmployeeWeekBoundaryContext.Create(
            employeeId,
            previousWeekLastShiftEnd: boundary,
            nextWeekFirstShiftStart: boundary);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EmployeeWeekBoundaryContext.InvalidBoundaryWindow");
    }
}

