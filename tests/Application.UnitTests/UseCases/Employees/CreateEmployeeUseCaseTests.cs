using Application.DTOs.Employees;
using Application.UnitTests.Fakes;
using Application.UseCases.Employees;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Employees;

public class CreateEmployeeUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private CreateEmployeeUseCase CreateUseCase() =>
        new(_companyRepository, _employeeRepository, _currentUserService, _unitOfWork);

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Paulista",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        _companyRepository.Seed(company);
        return company;
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsNull_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.InvalidCommand");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CreateEmployeeCommand("Ana", StationRole.Waiter));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyTenantId");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExist_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CreateEmployeeCommand("Ana", StationRole.Waiter));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenQuickEntryWithMinimalValidFields_ShouldPersistAndReturnDto()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new CreateEmployeeCommand("  João Pedro  ", StationRole.Kitchen);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.Name.Should().Be("João Pedro");
        result.Value.PrimaryRole.Should().Be(StationRole.Kitchen);
        result.Value.IsActive.Should().BeTrue();
        result.Value.SecondarySkills.Should().BeEmpty();
        result.Value.PreferredDaysOff.Should().BeEmpty();
        result.Value.UnavailableDays.Should().BeEmpty();

        _employeeRepository.AddedEmployees.Should().ContainSingle();
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullPolyvalenceAndConstraintsProvided_ShouldPersistNormalizedEmployee()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();
        var command = new CreateEmployeeCommand(
            "Mariana Silva",
            StationRole.Waiter,
            [
                new SecondarySkillDto(StationRole.Bar, SkillProficiency.Support),
                new SecondarySkillDto(StationRole.Cashier, SkillProficiency.Autonomous)
            ],
            [DayOfWeek.Monday],
            [DayOfWeek.Sunday]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SecondarySkills.Should().HaveCount(2);
        result.Value.PreferredDaysOff.Should().Equal(DayOfWeek.Monday);
        result.Value.UnavailableDays.Should().Equal(DayOfWeek.Sunday);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSecondarySkillMatchesPrimaryRole_ShouldReturnValidationFailureWithoutSaving()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();
        var command = new CreateEmployeeCommand(
            "Mariana Silva",
            StationRole.Waiter,
            [new SecondarySkillDto(StationRole.Waiter, SkillProficiency.Autonomous)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.PrimaryRoleCannotBeSecondary");
        _employeeRepository.AddedEmployees.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPreferredAndUnavailableDaysOverlap_ShouldReturnValidationFailureWithoutSaving()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();
        var command = new CreateEmployeeCommand(
            "Mariana Silva",
            StationRole.Waiter,
            PreferredDaysOff: [DayOfWeek.Saturday],
            UnavailableDays: [DayOfWeek.Saturday]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.ConflictingDayConstraint");
        _employeeRepository.AddedEmployees.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTelemetryProvided_ShouldRecordSuccessAndValidationFailure()
    {
        // Arrange
        SeedCompany();
        var telemetry = new FakeResultTelemetry();
        var useCase = new CreateEmployeeUseCase(
            _companyRepository,
            _employeeRepository,
            _currentUserService,
            _unitOfWork,
            telemetry);

        // Act
        var ok = await useCase.ExecuteAsync(new CreateEmployeeCommand("Carlos", StationRole.Waiter));
        var fail = await useCase.ExecuteAsync(new CreateEmployeeCommand("  ", StationRole.Waiter));

        // Assert
        ok.IsSuccess.Should().BeTrue();
        fail.IsFailure.Should().BeTrue();
        telemetry.RecordedOutcomes.Should().Contain(o =>
            o.UseCaseName == "CreateEmployee" &&
            o.IsSuccess &&
            o.TenantId == _currentUserService.TenantId);
        telemetry.RecordedOutcomes.Should().Contain(o =>
            o.UseCaseName == "CreateEmployee" &&
            !o.IsSuccess &&
            o.ErrorCode == fail.Error.Code &&
            o.ErrorCategory == "Validation" &&
            o.TenantId == _currentUserService.TenantId);
    }
}

