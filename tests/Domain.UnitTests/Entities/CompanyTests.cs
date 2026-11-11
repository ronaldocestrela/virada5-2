using Domain.Entities;
using Domain.Enums;

namespace Domain.UnitTests.Entities;

public class CompanyTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly TimeOnly DefaultOpening = new(11, 0);
    private static readonly TimeOnly DefaultClosing = new(23, 0);

    private static Company CreateValidCompany(
        ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff,
        IEnumerable<DayOfWeek>? closedDays = null)
    {
        var result = Company.Create(
            ValidTenantId,
            "Bistrô Central",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            scheduleModel,
            closedDays);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_WhenValidRotatingParameters_ShouldReturnSuccessWithInitializedCompany()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var opening = new TimeOnly(10, 30);
        var closing = new TimeOnly(22, 30);

        // Act
        var result = Company.Create(
            tenantId,
            "  Restaurante Sabor & Arte  ",
            BusinessType.Restaurant,
            opening,
            closing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Name.Should().Be("Restaurante Sabor & Arte");
        result.Value.BusinessType.Should().Be(BusinessType.Restaurant);
        result.Value.OpeningTime.Should().Be(opening);
        result.Value.ClosingTime.Should().Be(closing);
        result.Value.ScheduleModel.Should().Be(ScheduleModel.RotatingDaysOff);
        result.Value.ClosedDays.Should().BeEmpty();
    }

    [Fact]
    public void Create_WhenClosingTimeIsAfterMidnight_ShouldReturnSuccess()
    {
        // Arrange
        var opening = new TimeOnly(18, 0);
        var closing = new TimeOnly(2, 0);

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Bar da Esquina",
            BusinessType.Bar,
            opening,
            closing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OpeningTime.Should().Be(opening);
        result.Value.ClosingTime.Should().Be(closing);
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = Company.Create(
            Guid.Empty,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.EmptyTenantId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsNullOrWhitespace_ShouldReturnValidationFailure(string? invalidName)
    {
        // Act
        var result = Company.Create(
            ValidTenantId,
            invalidName!,
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.EmptyName");
    }

    [Fact]
    public void Create_WhenNameExceedsMaxLength_ShouldReturnValidationFailure()
    {
        // Arrange
        var longName = new string('A', Company.MaxNameLength + 1);

        // Act
        var result = Company.Create(
            ValidTenantId,
            longName,
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NameTooLong");
    }

    [Fact]
    public void Create_WhenBusinessTypeIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidType = (BusinessType)999;

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Café Paulista",
            invalidType,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidBusinessType");
    }

    [Fact]
    public void Create_WhenOpeningAndClosingTimesAreEqual_ShouldReturnValidationFailure()
    {
        // Arrange
        var sameTime = new TimeOnly(10, 0);

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            sameTime,
            sameTime,
            ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidOperatingHours");
    }

    [Fact]
    public void Create_WhenScheduleModelIsUndefined_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidModel = (ScheduleModel)999;

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            invalidModel);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidScheduleModel");
    }

    [Fact]
    public void Create_WhenRotatingDaysOffWithClosedDays_ShouldIgnoreClosedDays()
    {
        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.RotatingDaysOff,
            [DayOfWeek.Monday]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ClosedDays.Should().BeEmpty();
    }

    [Fact]
    public void Create_WhenFixedClosureWithNullClosedDays_ShouldReturnValidationFailure()
    {
        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.FixedClosure,
            null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.ClosedDaysRequired");
    }

    [Fact]
    public void Create_WhenFixedClosureWithoutClosedDays_ShouldReturnValidationFailure()
    {
        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.FixedClosure,
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.ClosedDaysRequired");
    }

    [Fact]
    public void Create_WhenAllSevenDaysAreClosed_ShouldReturnValidationFailure()
    {
        // Arrange
        var allDays = Enum.GetValues<DayOfWeek>();

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.FixedClosure,
            allDays);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.AllDaysClosed");
    }

    [Fact]
    public void Create_WhenClosedDaysContainUndefinedEnumValue_ShouldReturnValidationFailure()
    {
        // Arrange
        var invalidDays = new[] { (DayOfWeek)99 };

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.FixedClosure,
            invalidDays);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidClosedDay");
    }

    [Fact]
    public void Create_WhenFixedClosureWithDuplicateClosedDays_ShouldDeduplicateClosedDays()
    {
        // Arrange
        var closedDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Monday };

        // Act
        var result = Company.Create(
            ValidTenantId,
            "Cantina Italiana",
            BusinessType.Restaurant,
            DefaultOpening,
            DefaultClosing,
            ScheduleModel.FixedClosure,
            closedDays);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ClosedDays.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Tuesday]);
    }

    [Fact]
    public void UpdateProfile_WhenValidParameters_ShouldUpdateNameAndBusinessType()
    {
        // Arrange
        var company = CreateValidCompany();

        // Act
        var result = company.UpdateProfile("  Padaria Pão Quente ", BusinessType.CafeBakery);

        // Assert
        result.IsSuccess.Should().BeTrue();
        company.Name.Should().Be("Padaria Pão Quente");
        company.BusinessType.Should().Be(BusinessType.CafeBakery);
    }

    [Fact]
    public void UpdateProfile_WhenInvalidParameters_ShouldReturnFailureAndPreservePreviousState()
    {
        // Arrange
        var company = CreateValidCompany();

        // Act
        var result = company.UpdateProfile(" ", BusinessType.FastFood);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.EmptyName");
        company.Name.Should().Be("Bistrô Central");
        company.BusinessType.Should().Be(BusinessType.Restaurant);
    }

    [Fact]
    public void UpdateOperatingHours_WhenValidTimes_ShouldUpdateOpeningAndClosingHours()
    {
        // Arrange
        var company = CreateValidCompany();
        var newOpening = new TimeOnly(8, 0);
        var newClosing = new TimeOnly(18, 0);

        // Act
        var result = company.UpdateOperatingHours(newOpening, newClosing);

        // Assert
        result.IsSuccess.Should().BeTrue();
        company.OpeningTime.Should().Be(newOpening);
        company.ClosingTime.Should().Be(newClosing);
    }

    [Fact]
    public void UpdateOperatingHours_WhenTimesAreEqual_ShouldReturnFailureAndPreservePreviousHours()
    {
        // Arrange
        var company = CreateValidCompany();
        var sameTime = new TimeOnly(12, 0);

        // Act
        var result = company.UpdateOperatingHours(sameTime, sameTime);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidOperatingHours");
        company.OpeningTime.Should().Be(DefaultOpening);
        company.ClosingTime.Should().Be(DefaultClosing);
    }

    [Fact]
    public void UpdateScheduleModel_WhenChangedToFixedClosureWithValidDays_ShouldUpdateModelAndClosedDays()
    {
        // Arrange
        var company = CreateValidCompany(ScheduleModel.RotatingDaysOff);

        // Act
        var result = company.UpdateScheduleModel(
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday, DayOfWeek.Tuesday]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        company.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        company.ClosedDays.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Tuesday]);
    }

    [Fact]
    public void UpdateScheduleModel_WhenChangedToRotating_ShouldClearClosedDays()
    {
        // Arrange
        var company = CreateValidCompany(ScheduleModel.FixedClosure, [DayOfWeek.Monday]);

        // Act
        var result = company.UpdateScheduleModel(ScheduleModel.RotatingDaysOff);

        // Assert
        result.IsSuccess.Should().BeTrue();
        company.ScheduleModel.Should().Be(ScheduleModel.RotatingDaysOff);
        company.ClosedDays.Should().BeEmpty();
    }

    [Fact]
    public void UpdateScheduleModel_WhenInvalid_ShouldReturnFailureAndPreservePreviousState()
    {
        // Arrange
        var company = CreateValidCompany(ScheduleModel.FixedClosure, [DayOfWeek.Monday]);

        // Act
        var result = company.UpdateScheduleModel(ScheduleModel.FixedClosure, []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.ClosedDaysRequired");
        company.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        company.ClosedDays.Should().BeEquivalentTo([DayOfWeek.Monday]);
    }

    [Fact]
    public void IsOpenOn_WhenDayIsInClosedDays_ShouldReturnFalseAndOtherwiseTrue()
    {
        // Arrange
        var company = CreateValidCompany(ScheduleModel.FixedClosure, [DayOfWeek.Monday]);

        // Act & Assert
        company.IsOpenOn(DayOfWeek.Monday).Should().BeFalse();
        company.IsOpenOn(DayOfWeek.Friday).Should().BeTrue();
    }

    [Fact]
    public void IsOpenOn_WhenDayOfWeekIsUndefined_ShouldReturnFalse()
    {
        // Arrange
        var company = CreateValidCompany();

        // Act & Assert
        company.IsOpenOn((DayOfWeek)99).Should().BeFalse();
    }
}
