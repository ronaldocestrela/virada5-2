using Domain.Entities;
using Domain.Enums;

namespace Domain.UnitTests.Entities;

public class DemandSlotTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidCompanyId = Guid.NewGuid();

    private static DemandSlot CreateValidSlot(
        DayOfWeek dayOfWeek = DayOfWeek.Friday,
        int hour = 19,
        decimal expectedVolume = 80m)
    {
        var result = DemandSlot.Create(
            ValidTenantId,
            ValidCompanyId,
            dayOfWeek,
            hour,
            expectedVolume);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_WhenValidParameters_ShouldReturnSuccessWithInitializedSlot()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        // Act
        var result = DemandSlot.Create(
            tenantId,
            companyId,
            DayOfWeek.Saturday,
            20,
            120m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.CompanyId.Should().Be(companyId);
        result.Value.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        result.Value.Hour.Should().Be(20);
        result.Value.ExpectedVolume.Should().Be(120m);
        result.Value.RelativeVolume.Should().Be(0m);
        result.Value.Classification.Should().Be(DemandClassification.Idle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public void Create_WhenHourIsOnValidBoundary_ShouldReturnSuccess(int validHour)
    {
        // Act
        var result = DemandSlot.Create(
            ValidTenantId,
            ValidCompanyId,
            DayOfWeek.Monday,
            validHour,
            0m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Hour.Should().Be(validHour);
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = DemandSlot.Create(
            Guid.Empty,
            ValidCompanyId,
            DayOfWeek.Monday,
            12,
            10m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.EmptyTenantId");
    }

    [Fact]
    public void Create_WhenCompanyIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = DemandSlot.Create(
            ValidTenantId,
            Guid.Empty,
            DayOfWeek.Monday,
            12,
            10m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.EmptyCompanyId");
    }

    [Fact]
    public void Create_WhenDayOfWeekIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidDay = (DayOfWeek)99;

        // Act
        var result = DemandSlot.Create(
            ValidTenantId,
            ValidCompanyId,
            invalidDay,
            12,
            10m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.InvalidDayOfWeek");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(99)]
    public void Create_WhenHourIsOutOfRange_ShouldReturnValidationFailure(int invalidHour)
    {
        // Act
        var result = DemandSlot.Create(
            ValidTenantId,
            ValidCompanyId,
            DayOfWeek.Tuesday,
            invalidHour,
            10m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.InvalidHour");
    }

    [Fact]
    public void Create_WhenExpectedVolumeIsNegative_ShouldReturnValidationFailure()
    {
        // Act
        var result = DemandSlot.Create(
            ValidTenantId,
            ValidCompanyId,
            DayOfWeek.Wednesday,
            12,
            -0.01m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.NegativeVolume");
    }

    [Fact]
    public void UpdateVolume_WhenVolumeIsValid_ShouldUpdateExpectedVolumeAndResetRelativeEvaluation()
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: 50m);
        slot.EvaluateRelativeDemand(100m).IsSuccess.Should().BeTrue();

        // Act
        var result = slot.UpdateVolume(75m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        slot.ExpectedVolume.Should().Be(75m);
        slot.RelativeVolume.Should().Be(0m);
        slot.Classification.Should().Be(DemandClassification.Idle);
    }

    [Fact]
    public void UpdateVolume_WhenVolumeIsNegative_ShouldReturnFailureAndPreservePreviousState()
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: 50m);
        slot.EvaluateRelativeDemand(100m).IsSuccess.Should().BeTrue();

        // Act
        var result = slot.UpdateVolume(-10m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.NegativeVolume");
        slot.ExpectedVolume.Should().Be(50m);
        slot.RelativeVolume.Should().Be(0.5m);
        slot.Classification.Should().Be(DemandClassification.Low);
    }

    [Fact]
    public void EvaluateRelativeDemand_WhenPeakVolumeIsZero_ShouldSetRelativeVolumeZeroAndClassifyAsIdle()
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: 0m);

        // Act
        var result = slot.EvaluateRelativeDemand(0m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        slot.RelativeVolume.Should().Be(0m);
        slot.Classification.Should().Be(DemandClassification.Idle);
    }

    [Fact]
    public void EvaluateRelativeDemand_WhenPeakVolumeIsNegative_ShouldReturnValidationFailure()
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: 10m);

        // Act
        var result = slot.EvaluateRelativeDemand(-5m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.InvalidPeakVolume");
    }

    [Fact]
    public void EvaluateRelativeDemand_WhenPeakVolumeIsLessThanExpectedVolume_ShouldReturnValidationFailure()
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: 80m);

        // Act
        var result = slot.EvaluateRelativeDemand(60m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandSlot.PeakLessThanSlotVolume");
    }

    [Theory]
    [InlineData(0, 100, 0.0000, DemandClassification.Idle)]
    [InlineData(25, 100, 0.2500, DemandClassification.Idle)]
    [InlineData(26, 100, 0.2600, DemandClassification.Low)]
    [InlineData(50, 100, 0.5000, DemandClassification.Low)]
    [InlineData(51, 100, 0.5100, DemandClassification.Moderate)]
    [InlineData(74, 100, 0.7400, DemandClassification.Moderate)]
    [InlineData(75, 100, 0.7500, DemandClassification.Peak)]
    [InlineData(100, 100, 1.0000, DemandClassification.Peak)]
    [InlineData(1, 3, 0.3333, DemandClassification.Low)]
    public void EvaluateRelativeDemand_WhenValidPeakVolume_ShouldCalculateRelativeVolumeAndClassification(
        decimal expectedVolume,
        decimal peakVolume,
        decimal expectedRelative,
        DemandClassification expectedClassification)
    {
        // Arrange
        var slot = CreateValidSlot(expectedVolume: expectedVolume);

        // Act
        var result = slot.EvaluateRelativeDemand(peakVolume);

        // Assert
        result.IsSuccess.Should().BeTrue();
        slot.RelativeVolume.Should().Be(expectedRelative);
        slot.Classification.Should().Be(expectedClassification);
    }
}

