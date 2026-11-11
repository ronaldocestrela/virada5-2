using Application.UnitTests.Fakes;
using Application.UseCases.Employees;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Employees;

public class GetCurrentEmployeesWorkspaceUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetCurrentEmployeesWorkspaceUseCase CreateUseCase() =>
        new(_companyRepository, _employeeRepository, _currentUserService);

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
        result.Error.Code.Should().Be("EmployeeWorkspace.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExist_ShouldReturnNotFoundFailure()
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
    public async Task ExecuteAsync_WhenCompanyHasNoEmployees_ShouldReturnEmptyWorkspace()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        _companyRepository.Seed(company);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.CompanyName.Should().Be("Bistrô Central");
        result.Value.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        result.Value.ClosedDays.Should().Equal(DayOfWeek.Monday);
        result.Value.Employees.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasActiveAndInactiveEmployees_ShouldReturnAllMappedEmployees()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Cantina do Porto",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        var activeEmployee = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Bruna Costa",
            StationRole.Waiter,
            [
                SecondarySkill.Create(StationRole.Cashier, SkillProficiency.Autonomous).Value,
                SecondarySkill.Create(StationRole.Bar, SkillProficiency.Support).Value
            ],
            [DayOfWeek.Tuesday],
            [DayOfWeek.Sunday]).Value;

        var inactiveEmployee = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "Carlos Lima",
            StationRole.Kitchen).Value;
        inactiveEmployee.Deactivate();

        _companyRepository.Seed(company);
        _employeeRepository.Seed(activeEmployee, inactiveEmployee);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Employees.Should().HaveCount(2);

        var bruna = result.Value.Employees.Should().ContainSingle(e => e.Name == "Bruna Costa").Subject;
        bruna.EmployeeId.Should().Be(activeEmployee.Id);
        bruna.IsActive.Should().BeTrue();
        bruna.PrimaryRole.Should().Be(StationRole.Waiter);
        bruna.SecondarySkills.Should().HaveCount(2);
        bruna.PreferredDaysOff.Should().Equal(DayOfWeek.Tuesday);
        bruna.UnavailableDays.Should().Equal(DayOfWeek.Sunday);

        var carlos = result.Value.Employees.Should().ContainSingle(e => e.Name == "Carlos Lima").Subject;
        carlos.IsActive.Should().BeFalse();
    }
}

