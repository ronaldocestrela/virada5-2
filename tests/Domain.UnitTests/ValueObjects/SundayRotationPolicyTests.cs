using Domain.ValueObjects;

namespace Domain.UnitTests.ValueObjects;

public class SundayRotationPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Create_WhenValidMaxConsecutiveSundays_ShouldReturnSuccess(int maxConsecutiveSundays)
    {
        // Act
        var result = SundayRotationPolicy.Create(maxConsecutiveSundays);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.MaxConsecutiveSundaysWorked.Should().Be(maxConsecutiveSundays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void Create_WhenOutOfRangeMaxConsecutiveSundays_ShouldReturnValidationFailure(int maxConsecutiveSundays)
    {
        // Act
        var result = SundayRotationPolicy.Create(maxConsecutiveSundays);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SundayRotationPolicy.InvalidMaxConsecutiveSundays");
    }

    [Fact]
    public void PredefinedPolicies_WhenInspected_ShouldMatchCltDefaults()
    {
        // Assert
        SundayRotationPolicy.BiweeklyCycle.MaxConsecutiveSundaysWorked.Should().Be(1);
        SundayRotationPolicy.ThreeWeekCycle.MaxConsecutiveSundaysWorked.Should().Be(2);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 3, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    public void CanWorkSunday_WhenEvaluatedWithPriorStreak_ShouldRespectMaxLimit(
        int maxConsecutiveSundays,
        int consecutiveSundaysWorkedBeforeWeek,
        bool expectedAllowed)
    {
        // Arrange
        var policy = SundayRotationPolicy.Create(maxConsecutiveSundays).Value;

        // Act
        var canWork = policy.CanWorkSunday(consecutiveSundaysWorkedBeforeWeek);

        // Assert
        canWork.Should().Be(expectedAllowed);
    }

    [Fact]
    public void CanWorkSunday_WhenPriorStreakIsNegative_ShouldReturnFalse()
    {
        // Arrange
        var policy = SundayRotationPolicy.ThreeWeekCycle;

        // Act
        var canWork = policy.CanWorkSunday(-1);

        // Assert
        canWork.Should().BeFalse();
    }
}

