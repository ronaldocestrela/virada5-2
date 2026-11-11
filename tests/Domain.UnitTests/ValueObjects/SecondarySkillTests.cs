using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.UnitTests.ValueObjects;

public class SecondarySkillTests
{
    [Fact]
    public void Create_WhenValidRoleAndProficiency_ShouldReturnSuccess()
    {
        // Arrange
        var role = StationRole.Bar;
        var proficiency = SkillProficiency.Support;

        // Act
        var result = SecondarySkill.Create(role, proficiency);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(StationRole.Bar);
        result.Value.Proficiency.Should().Be(SkillProficiency.Support);
    }

    [Fact]
    public void Create_WhenRoleIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidRole = (StationRole)999;

        // Act
        var result = SecondarySkill.Create(invalidRole, SkillProficiency.Autonomous);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecondarySkill.InvalidStationRole");
    }

    [Fact]
    public void Create_WhenProficiencyIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidProficiency = (SkillProficiency)999;

        // Act
        var result = SecondarySkill.Create(StationRole.Cashier, invalidProficiency);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecondarySkill.InvalidProficiency");
    }

    [Theory]
    [InlineData(DemandClassification.Idle, true)]
    [InlineData(DemandClassification.Low, true)]
    [InlineData(DemandClassification.Moderate, true)]
    [InlineData(DemandClassification.Peak, false)]
    public void CanCover_WhenSupportProficiency_ShouldAllowUpToModerateAndRejectPeak(
        DemandClassification demandLevel,
        bool expected)
    {
        // Arrange
        var skill = SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Support).Value;

        // Act
        var canCover = skill.CanCover(demandLevel);

        // Assert
        canCover.Should().Be(expected);
    }

    [Theory]
    [InlineData(DemandClassification.Idle)]
    [InlineData(DemandClassification.Low)]
    [InlineData(DemandClassification.Moderate)]
    [InlineData(DemandClassification.Peak)]
    public void CanCover_WhenAutonomousProficiency_ShouldAllowAllDefinedDemandLevels(
        DemandClassification demandLevel)
    {
        // Arrange
        var skill = SecondarySkill.Create(StationRole.Kitchen, SkillProficiency.Autonomous).Value;

        // Act
        var canCover = skill.CanCover(demandLevel);

        // Assert
        canCover.Should().BeTrue();
    }

    [Fact]
    public void CanCover_WhenDemandLevelIsUndefined_ShouldReturnFalse()
    {
        // Arrange
        var skill = SecondarySkill.Create(StationRole.Kitchen, SkillProficiency.Autonomous).Value;
        var invalidDemand = (DemandClassification)999;

        // Act
        var canCover = skill.CanCover(invalidDemand);

        // Assert
        canCover.Should().BeFalse();
    }
}

