using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.UnitTests.Entities;

public class EmployeeTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidCompanyId = Guid.NewGuid();

    private static SecondarySkill CreateSkill(StationRole role, SkillProficiency proficiency)
    {
        var result = SecondarySkill.Create(role, proficiency);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Employee CreateValidEmployee(
        StationRole primaryRole = StationRole.Waiter,
        IEnumerable<SecondarySkill>? secondarySkills = null,
        IEnumerable<DayOfWeek>? preferredDaysOff = null,
        IEnumerable<DayOfWeek>? unavailableDays = null)
    {
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            primaryRole,
            secondarySkills,
            preferredDaysOff,
            unavailableDays);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_WhenValidMinimalParameters_ShouldReturnSuccessWithInitializedEmployee()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        // Act
        var result = Employee.Create(
            tenantId,
            companyId,
            "  Carlos Eduardo  ",
            StationRole.Kitchen);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.CompanyId.Should().Be(companyId);
        result.Value.Name.Should().Be("Carlos Eduardo");
        result.Value.PrimaryRole.Should().Be(StationRole.Kitchen);
        result.Value.IsActive.Should().BeTrue();
        result.Value.SecondarySkills.Should().BeEmpty();
        result.Value.PreferredDaysOff.Should().BeEmpty();
        result.Value.UnavailableDays.Should().BeEmpty();
    }

    [Fact]
    public void Create_WhenValidFullParametersWithDuplicateDays_ShouldDeduplicateDaysAndPreserveSkills()
    {
        // Arrange
        var barSkill = CreateSkill(StationRole.Bar, SkillProficiency.Support);
        var cashierSkill = CreateSkill(StationRole.Cashier, SkillProficiency.Autonomous);

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Ana Paula",
            StationRole.Waiter,
            [barSkill, cashierSkill],
            [DayOfWeek.Monday, DayOfWeek.Monday],
            [DayOfWeek.Wednesday, DayOfWeek.Wednesday]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SecondarySkills.Should().BeEquivalentTo([barSkill, cashierSkill]);
        result.Value.PreferredDaysOff.Should().BeEquivalentTo([DayOfWeek.Monday]);
        result.Value.UnavailableDays.Should().BeEquivalentTo([DayOfWeek.Wednesday]);
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            Guid.Empty,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyTenantId");
    }

    [Fact]
    public void Create_WhenCompanyIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            Guid.Empty,
            "Mariana Silva",
            StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyCompanyId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsNullOrWhitespace_ShouldReturnValidationFailure(string? invalidName)
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            invalidName!,
            StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyName");
    }

    [Fact]
    public void Create_WhenNameExceedsMaxLength_ShouldReturnValidationFailure()
    {
        // Arrange
        var longName = new string('A', Employee.MaxNameLength + 1);

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            longName,
            StationRole.Waiter);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.NameTooLong");
    }

    [Fact]
    public void Create_WhenPrimaryRoleIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidRole = (StationRole)999;

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            invalidRole);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidStationRole");
    }

    [Fact]
    public void Create_WhenSecondarySkillsContainDefaultStruct_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            [default(SecondarySkill)]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidSecondarySkill");
    }

    [Fact]
    public void Create_WhenSecondarySkillsContainPrimaryRole_ShouldReturnValidationFailure()
    {
        // Arrange
        var sameAsPrimary = CreateSkill(StationRole.Waiter, SkillProficiency.Autonomous);

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            [sameAsPrimary]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.PrimaryRoleCannotBeSecondary");
    }

    [Fact]
    public void Create_WhenSecondarySkillsContainDuplicateRoles_ShouldReturnValidationFailure()
    {
        // Arrange
        var first = CreateSkill(StationRole.Bar, SkillProficiency.Support);
        var duplicate = CreateSkill(StationRole.Bar, SkillProficiency.Autonomous);

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            [first, duplicate]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.DuplicateSecondarySkill");
    }

    [Fact]
    public void Create_WhenPreferredDaysOffContainUndefinedDay_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            preferredDaysOff: [(DayOfWeek)99]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidPreferredDayOff");
    }

    [Fact]
    public void Create_WhenUnavailableDaysContainUndefinedDay_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            unavailableDays: [(DayOfWeek)99]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidUnavailableDay");
    }

    [Fact]
    public void Create_WhenAllSevenDaysAreUnavailable_ShouldReturnValidationFailure()
    {
        // Arrange
        var allDays = Enum.GetValues<DayOfWeek>();

        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            unavailableDays: allDays);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.AllDaysUnavailable");
    }

    [Fact]
    public void Create_WhenPreferredDaysOverlapUnavailableDays_ShouldReturnValidationFailure()
    {
        // Act
        var result = Employee.Create(
            ValidTenantId,
            ValidCompanyId,
            "Mariana Silva",
            StationRole.Waiter,
            preferredDaysOff: [DayOfWeek.Monday, DayOfWeek.Tuesday],
            unavailableDays: [DayOfWeek.Tuesday]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.ConflictingDayConstraint");
    }

    [Fact]
    public void UpdateName_WhenValidName_ShouldTrimAndUpdateName()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.UpdateName("  Mariana Costa  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.Name.Should().Be("Mariana Costa");
    }

    [Fact]
    public void UpdateName_WhenInvalidName_ShouldReturnFailureAndPreservePreviousName()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.UpdateName("   ");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyName");
        employee.Name.Should().Be("Mariana Silva");
    }

    [Fact]
    public void UpdatePrimaryRole_WhenNewRoleNotInSecondarySkills_ShouldUpdatePrimaryRole()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Support)]);

        // Act
        var result = employee.UpdatePrimaryRole(StationRole.Cashier);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.PrimaryRole.Should().Be(StationRole.Cashier);
        employee.SecondarySkills.Should().ContainSingle(s => s.Role == StationRole.Bar);
    }

    [Fact]
    public void UpdatePrimaryRole_WhenNewRoleExistsInSecondarySkills_ShouldPromoteAndRemoveFromSecondarySkills()
    {
        // Arrange
        var barSkill = CreateSkill(StationRole.Bar, SkillProficiency.Autonomous);
        var cashierSkill = CreateSkill(StationRole.Cashier, SkillProficiency.Support);
        var employee = CreateValidEmployee(StationRole.Waiter, [barSkill, cashierSkill]);

        // Act
        var result = employee.UpdatePrimaryRole(StationRole.Bar);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.PrimaryRole.Should().Be(StationRole.Bar);
        employee.SecondarySkills.Should().BeEquivalentTo([cashierSkill]);
    }

    [Fact]
    public void UpdatePrimaryRole_WhenUndefinedRole_ShouldReturnFailureAndPreservePreviousState()
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Waiter);

        // Act
        var result = employee.UpdatePrimaryRole((StationRole)999);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidStationRole");
        employee.PrimaryRole.Should().Be(StationRole.Waiter);
    }

    [Fact]
    public void AddOrUpdateSecondarySkill_WhenNewSkill_ShouldAddToMatrix()
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Waiter);

        // Act
        var result = employee.AddOrUpdateSecondarySkill(StationRole.Bar, SkillProficiency.Support);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.SecondarySkills.Should().ContainSingle(s =>
            s.Role == StationRole.Bar && s.Proficiency == SkillProficiency.Support);
    }

    [Fact]
    public void AddOrUpdateSecondarySkill_WhenRoleAlreadyExists_ShouldUpdateProficiencyWithoutDuplicating()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Support)]);

        // Act
        var result = employee.AddOrUpdateSecondarySkill(StationRole.Bar, SkillProficiency.Autonomous);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.SecondarySkills.Should().ContainSingle(s =>
            s.Role == StationRole.Bar && s.Proficiency == SkillProficiency.Autonomous);
    }

    [Fact]
    public void AddOrUpdateSecondarySkill_WhenRoleMatchesPrimaryRole_ShouldReturnConflictFailure()
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Waiter);

        // Act
        var result = employee.AddOrUpdateSecondarySkill(StationRole.Waiter, SkillProficiency.Autonomous);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.PrimaryRoleCannotBeSecondary");
        employee.SecondarySkills.Should().BeEmpty();
    }

    [Fact]
    public void AddOrUpdateSecondarySkill_WhenInvalidRoleOrProficiency_ShouldReturnValidationFailure()
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Waiter);

        // Act
        var invalidRoleResult = employee.AddOrUpdateSecondarySkill((StationRole)999, SkillProficiency.Support);
        var invalidProficiencyResult = employee.AddOrUpdateSecondarySkill(StationRole.Bar, (SkillProficiency)999);

        // Assert
        invalidRoleResult.IsFailure.Should().BeTrue();
        invalidRoleResult.Error.Code.Should().Be("SecondarySkill.InvalidStationRole");
        invalidProficiencyResult.IsFailure.Should().BeTrue();
        invalidProficiencyResult.Error.Code.Should().Be("SecondarySkill.InvalidProficiency");
    }

    [Fact]
    public void RemoveSecondarySkill_WhenRoleExists_ShouldRemoveFromMatrix()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Support)]);

        // Act
        var result = employee.RemoveSecondarySkill(StationRole.Bar);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.SecondarySkills.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSecondarySkill_WhenRoleIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.RemoveSecondarySkill((StationRole)999);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidStationRole");
    }

    [Fact]
    public void RemoveSecondarySkill_WhenRoleNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.RemoveSecondarySkill(StationRole.Bar);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.SecondarySkillNotFound");
    }

    [Fact]
    public void UpdateAvailability_WhenValidDays_ShouldReplacePreferredAndUnavailableDays()
    {
        // Arrange
        var employee = CreateValidEmployee(
            preferredDaysOff: [DayOfWeek.Monday],
            unavailableDays: [DayOfWeek.Tuesday]);

        // Act
        var result = employee.UpdateAvailability(
            [DayOfWeek.Wednesday, DayOfWeek.Wednesday],
            [DayOfWeek.Thursday]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.PreferredDaysOff.Should().BeEquivalentTo([DayOfWeek.Wednesday]);
        employee.UnavailableDays.Should().BeEquivalentTo([DayOfWeek.Thursday]);
    }

    [Fact]
    public void UpdateAvailability_WhenNullCollections_ShouldClearBothCollections()
    {
        // Arrange
        var employee = CreateValidEmployee(
            preferredDaysOff: [DayOfWeek.Monday],
            unavailableDays: [DayOfWeek.Tuesday]);

        // Act
        var result = employee.UpdateAvailability(null, null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.PreferredDaysOff.Should().BeEmpty();
        employee.UnavailableDays.Should().BeEmpty();
    }

    [Fact]
    public void UpdateAvailability_WhenInvalidConstraint_ShouldReturnFailureAndPreservePreviousAvailability()
    {
        // Arrange
        var employee = CreateValidEmployee(
            preferredDaysOff: [DayOfWeek.Monday],
            unavailableDays: [DayOfWeek.Tuesday]);

        // Act
        var result = employee.UpdateAvailability([DayOfWeek.Friday], [DayOfWeek.Friday]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.ConflictingDayConstraint");
        employee.PreferredDaysOff.Should().BeEquivalentTo([DayOfWeek.Monday]);
        employee.UnavailableDays.Should().BeEquivalentTo([DayOfWeek.Tuesday]);
    }

    [Fact]
    public void Deactivate_WhenActive_ShouldSetIsActiveFalse()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.Deactivate();

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ShouldReturnConflictFailure()
    {
        // Arrange
        var employee = CreateValidEmployee();
        employee.Deactivate();

        // Act
        var result = employee.Deactivate();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.AlreadyInactive");
    }

    [Fact]
    public void Activate_WhenInactive_ShouldSetIsActiveTrue()
    {
        // Arrange
        var employee = CreateValidEmployee();
        employee.Deactivate();

        // Act
        var result = employee.Activate();

        // Assert
        result.IsSuccess.Should().BeTrue();
        employee.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ShouldReturnConflictFailure()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var result = employee.Activate();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.AlreadyActive");
    }

    [Fact]
    public void PrefersDayOff_WhenDayIsInPreferredDaysOff_ShouldReturnTrueOtherwiseFalse()
    {
        // Arrange
        var employee = CreateValidEmployee(preferredDaysOff: [DayOfWeek.Monday]);

        // Act & Assert
        employee.PrefersDayOff(DayOfWeek.Monday).Should().BeTrue();
        employee.PrefersDayOff(DayOfWeek.Friday).Should().BeFalse();
        employee.PrefersDayOff((DayOfWeek)99).Should().BeFalse();
    }

    [Fact]
    public void IsAvailableOn_WhenDayIsInUnavailableDays_ShouldReturnFalseOtherwiseTrue()
    {
        // Arrange
        var employee = CreateValidEmployee(unavailableDays: [DayOfWeek.Wednesday]);

        // Act & Assert
        employee.IsAvailableOn(DayOfWeek.Wednesday).Should().BeFalse();
        employee.IsAvailableOn(DayOfWeek.Friday).Should().BeTrue();
        employee.IsAvailableOn((DayOfWeek)99).Should().BeFalse();
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenUndefinedInputs_ShouldReturnValidationFailure()
    {
        // Arrange
        var employee = CreateValidEmployee();

        // Act
        var invalidRole = employee.EvaluateCoverageEligibility((StationRole)999, DemandClassification.Moderate);
        var invalidDemand = employee.EvaluateCoverageEligibility(StationRole.Waiter, (DemandClassification)999);
        var invalidDay = employee.EvaluateCoverageEligibility(StationRole.Waiter, DemandClassification.Moderate, (DayOfWeek)99);

        // Assert
        invalidRole.IsFailure.Should().BeTrue();
        invalidRole.Error.Code.Should().Be("Employee.InvalidStationRole");
        invalidDemand.IsFailure.Should().BeTrue();
        invalidDemand.Error.Code.Should().Be("Employee.InvalidDemandClassification");
        invalidDay.IsFailure.Should().BeTrue();
        invalidDay.Error.Code.Should().Be("Employee.InvalidDayOfWeek");
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenEmployeeIsInactive_ShouldReturnInactiveConflict()
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Waiter);
        employee.Deactivate();

        // Act
        var result = employee.EvaluateCoverageEligibility(StationRole.Waiter, DemandClassification.Low);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.Inactive");
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenDayIsUnavailable_ShouldReturnUnavailableConflict()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            unavailableDays: [DayOfWeek.Sunday]);

        // Act
        var result = employee.EvaluateCoverageEligibility(
            StationRole.Waiter,
            DemandClassification.Low,
            DayOfWeek.Sunday);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.UnavailableOnDay");
    }

    [Theory]
    [InlineData(DemandClassification.Idle)]
    [InlineData(DemandClassification.Low)]
    [InlineData(DemandClassification.Moderate)]
    [InlineData(DemandClassification.Peak)]
    public void EvaluateCoverageEligibility_WhenTargetIsPrimaryRole_ShouldReturnSuccessWithPrimaryOrigin(
        DemandClassification demandLevel)
    {
        // Arrange
        var employee = CreateValidEmployee(StationRole.Kitchen);

        // Act
        var result = employee.EvaluateCoverageEligibility(StationRole.Kitchen, demandLevel, DayOfWeek.Friday);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CoverageOrigin.PrimaryRole);
    }

    [Theory]
    [InlineData(DemandClassification.Idle)]
    [InlineData(DemandClassification.Low)]
    [InlineData(DemandClassification.Moderate)]
    public void EvaluateCoverageEligibility_WhenSupportSecondarySkillAndNonPeakDemand_ShouldReturnSuccessWithSecondaryOrigin(
        DemandClassification demandLevel)
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Support)]);

        // Act
        var result = employee.EvaluateCoverageEligibility(StationRole.Bar, demandLevel, DayOfWeek.Tuesday);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CoverageOrigin.SecondarySkill);
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenSupportSecondarySkillAndPeakDemand_ShouldReturnProficiencyConflict()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Support)]);

        // Act
        var result = employee.EvaluateCoverageEligibility(
            StationRole.Bar,
            DemandClassification.Peak,
            DayOfWeek.Saturday);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InsufficientProficiencyForPeak");
    }

    [Theory]
    [InlineData(DemandClassification.Idle)]
    [InlineData(DemandClassification.Low)]
    [InlineData(DemandClassification.Moderate)]
    [InlineData(DemandClassification.Peak)]
    public void EvaluateCoverageEligibility_WhenAutonomousSecondarySkill_ShouldReturnSuccessForAllDemandLevels(
        DemandClassification demandLevel)
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Cashier, SkillProficiency.Autonomous)]);

        // Act
        var result = employee.EvaluateCoverageEligibility(StationRole.Cashier, demandLevel, DayOfWeek.Saturday);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CoverageOrigin.SecondarySkill);
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenRoleIsNotCovered_ShouldReturnRoleNotCoveredConflict()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            [CreateSkill(StationRole.Bar, SkillProficiency.Autonomous)]);

        // Act
        var result = employee.EvaluateCoverageEligibility(StationRole.Kitchen, DemandClassification.Idle);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.RoleNotCovered");
    }

    [Fact]
    public void EvaluateCoverageEligibility_WhenDayIsPreferredDayOffButAvailable_ShouldRemainEligible()
    {
        // Arrange
        var employee = CreateValidEmployee(
            StationRole.Waiter,
            preferredDaysOff: [DayOfWeek.Monday]);

        // Act
        var result = employee.EvaluateCoverageEligibility(
            StationRole.Waiter,
            DemandClassification.Moderate,
            DayOfWeek.Monday);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(CoverageOrigin.PrimaryRole);
        employee.PrefersDayOff(DayOfWeek.Monday).Should().BeTrue();
    }
}

