using Application.UnitTests.Fakes;
using Application.UseCases.Companies;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Companies;

public class GetCurrentCompanySettingsUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetCurrentCompanySettingsUseCase CreateUseCase() =>
        new(_companyRepository, _currentUserService);

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CompanySettings.EmptyTenantId");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExistForTenant_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsWithFixedClosure_ShouldReturnMappedSettingsDto()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Cantina da Esquina",
            BusinessType.Restaurant,
            new TimeOnly(11, 30),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Tuesday, DayOfWeek.Monday]).Value;

        _companyRepository.Seed(company);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.Name.Should().Be("Cantina da Esquina");
        result.Value.BusinessType.Should().Be(BusinessType.Restaurant);
        result.Value.OpeningTime.Should().Be(new TimeOnly(11, 30));
        result.Value.ClosingTime.Should().Be(new TimeOnly(23, 0));
        result.Value.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        result.Value.ClosedDays.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday);
        result.Value.OperatesOvernight.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyOperatesOvernight_ShouldFlagOperatesOvernightTrue()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bar Noturno 5x2",
            BusinessType.Bar,
            new TimeOnly(18, 0),
            new TimeOnly(2, 0),
            ScheduleModel.RotatingDaysOff).Value;

        _companyRepository.Seed(company);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ScheduleModel.Should().Be(ScheduleModel.RotatingDaysOff);
        result.Value.ClosedDays.Should().BeEmpty();
        result.Value.OperatesOvernight.Should().BeTrue();
    }
}

