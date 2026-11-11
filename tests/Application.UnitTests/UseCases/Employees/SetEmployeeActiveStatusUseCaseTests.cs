using Application.UnitTests.Fakes;
using Application.UseCases.Employees;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Employees;

public class SetEmployeeActiveStatusUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private SetEmployeeActiveStatusUseCase CreateUseCase() =>
        new(_companyRepository, _employeeRepository, _currentUserService, _unitOfWork);

    private (Company Company, Employee Employee) SeedCompanyAndEmployee()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Paulista",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        var employee = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Mariana Silva",
            StationRole.Waiter).Value;

        _companyRepository.Seed(company);
        _employeeRepository.Seed(employee);
        return (company, employee);
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
    public async Task ExecuteAsync_WhenEmployeeDoesNotExist_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var (company, _) = SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(
            new SetEmployeeActiveStatusCommand(Guid.NewGuid(), false, company.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.NotFound");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeactivatingActiveEmployee_ShouldPersistInactiveStatus()
    {
        // Arrange
        var (company, employee) = SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(
            new SetEmployeeActiveStatusCommand(employee.Id, false, company.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        employee.IsActive.Should().BeFalse();
        _employeeRepository.UpdatedEmployees.Should().Contain(employee);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActivatingInactiveEmployee_ShouldPersistActiveStatus()
    {
        // Arrange
        var (company, employee) = SeedCompanyAndEmployee();
        employee.Deactivate();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(
            new SetEmployeeActiveStatusCommand(employee.Id, true, company.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
        employee.IsActive.Should().BeTrue();
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmployeeAlreadyInRequestedStatus_ShouldReturnConflictFailureWithoutSaving()
    {
        // Arrange
        var (company, employee) = SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(
            new SetEmployeeActiveStatusCommand(employee.Id, true, company.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.AlreadyActive");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }
}

