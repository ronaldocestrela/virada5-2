using Application.UnitTests.Fakes;
using Application.UseCases.Companies;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Companies;

public class SaveCompanySettingsUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private SaveCompanySettingsUseCase CreateUseCase() =>
        new(_companyRepository, _currentUserService, _unitOfWork);

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsNull_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CompanySettings.InvalidCommand");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();
        var command = new SaveCompanySettingsCommand(
            "Bistrô Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CompanySettings.EmptyTenantId");
        _companyRepository.AddedCompanies.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExistAndCommandValid_ShouldCreateAndPersistCompany()
    {
        // Arrange
        var useCase = CreateUseCase();
        var command = new SaveCompanySettingsCommand(
            "  Padaria Aurora  ",
            BusinessType.CafeBakery,
            new TimeOnly(6, 0),
            new TimeOnly(20, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Sunday, DayOfWeek.Monday]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Padaria Aurora");
        result.Value.BusinessType.Should().Be(BusinessType.CafeBakery);
        result.Value.OpeningTime.Should().Be(new TimeOnly(6, 0));
        result.Value.ClosingTime.Should().Be(new TimeOnly(20, 0));
        result.Value.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        result.Value.ClosedDays.Should().Equal(DayOfWeek.Monday, DayOfWeek.Sunday);
        result.Value.OperatesOvernight.Should().BeFalse();

        _companyRepository.AddedCompanies.Should().ContainSingle();
        _companyRepository.AddedCompanies[0].TenantId.Should().Be(_currentUserService.TenantId);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExistAndDomainValidationFails_ShouldNotPersist()
    {
        // Arrange
        var useCase = CreateUseCase();
        var command = new SaveCompanySettingsCommand(
            "Casa Inválida",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            []);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.ClosedDaysRequired");
        _companyRepository.AddedCompanies.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsAndCommandValid_ShouldUpdateAndPersistCompany()
    {
        // Arrange
        var existing = Company.Create(
            _currentUserService.TenantId,
            "Casa Antiga",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        _companyRepository.Seed(existing);
        var useCase = CreateUseCase();

        var command = new SaveCompanySettingsCommand(
            "Bar da Virada",
            BusinessType.Bar,
            new TimeOnly(18, 0),
            new TimeOnly(2, 0),
            ScheduleModel.RotatingDaysOff);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(existing.Id);
        result.Value.Name.Should().Be("Bar da Virada");
        result.Value.BusinessType.Should().Be(BusinessType.Bar);
        result.Value.OpeningTime.Should().Be(new TimeOnly(18, 0));
        result.Value.ClosingTime.Should().Be(new TimeOnly(2, 0));
        result.Value.ScheduleModel.Should().Be(ScheduleModel.RotatingDaysOff);
        result.Value.ClosedDays.Should().BeEmpty();
        result.Value.OperatesOvernight.Should().BeTrue();

        _companyRepository.AddedCompanies.Should().BeEmpty();
        _companyRepository.UpdatedCompanies.Should().ContainSingle().Which.Should().BeSameAs(existing);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsAndOperatingHoursInvalid_ShouldReturnFailureWithoutSavingOrMutating()
    {
        // Arrange
        var existing = Company.Create(
            _currentUserService.TenantId,
            "Casa Original",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        _companyRepository.Seed(existing);
        var useCase = CreateUseCase();

        var command = new SaveCompanySettingsCommand(
            "Outro Nome",
            BusinessType.Bar,
            new TimeOnly(12, 0),
            new TimeOnly(12, 0),
            ScheduleModel.RotatingDaysOff);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.InvalidOperatingHours");
        existing.Name.Should().Be("Casa Original");
        existing.BusinessType.Should().Be(BusinessType.Restaurant);
        existing.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        _companyRepository.UpdatedCompanies.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsAndProfileInvalid_ShouldReturnFailureWithoutSaving()
    {
        // Arrange
        var existing = Company.Create(
            _currentUserService.TenantId,
            "Casa Original",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        _companyRepository.Seed(existing);
        var useCase = CreateUseCase();

        var command = new SaveCompanySettingsCommand(
            "   ",
            BusinessType.FastFood,
            new TimeOnly(10, 0),
            new TimeOnly(22, 0),
            ScheduleModel.RotatingDaysOff);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.EmptyName");
        _companyRepository.UpdatedCompanies.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsAndScheduleModelInvalid_ShouldReturnFailureWithoutSavingOrMutating()
    {
        // Arrange
        var existing = Company.Create(
            _currentUserService.TenantId,
            "Casa Original",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        _companyRepository.Seed(existing);
        var useCase = CreateUseCase();

        var command = new SaveCompanySettingsCommand(
            "Casa Editada",
            BusinessType.FastFood,
            new TimeOnly(10, 0),
            new TimeOnly(22, 0),
            ScheduleModel.FixedClosure,
            Enum.GetValues<DayOfWeek>());

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.AllDaysClosed");
        existing.Name.Should().Be("Casa Original");
        existing.BusinessType.Should().Be(BusinessType.Restaurant);
        existing.OpeningTime.Should().Be(new TimeOnly(11, 0));
        existing.ClosingTime.Should().Be(new TimeOnly(23, 0));
        _companyRepository.UpdatedCompanies.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }
}

