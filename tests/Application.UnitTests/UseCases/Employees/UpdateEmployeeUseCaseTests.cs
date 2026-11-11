using Application.DTOs.Employees;
using Application.UnitTests.Fakes;
using Application.UseCases.Employees;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Employees;

public class UpdateEmployeeUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private UpdateEmployeeUseCase CreateUseCase() =>
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
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Support).Value],
            [DayOfWeek.Monday],
            [DayOfWeek.Sunday]).Value;

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
    public async Task ExecuteAsync_WhenEmployeeIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(
            new UpdateEmployeeCommand(Guid.Empty, "Mariana", StationRole.Waiter));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.EmptyEmployeeId");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmployeeDoesNotExistOrBelongsToAnotherCompany_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var (company, _) = SeedCompanyAndEmployee();
        var otherEmployee = Employee.Create(
            _currentUserService.TenantId,
            Guid.NewGuid(),
            "Outro Colaborador",
            StationRole.Bar).Value;
        _employeeRepository.Seed(otherEmployee);
        var useCase = CreateUseCase();

        // Act
        var missingResult = await useCase.ExecuteAsync(
            new UpdateEmployeeCommand(Guid.NewGuid(), "Teste", StationRole.Waiter, CompanyId: company.Id));
        var otherCompanyResult = await useCase.ExecuteAsync(
            new UpdateEmployeeCommand(otherEmployee.Id, "Teste", StationRole.Bar, CompanyId: company.Id));

        // Assert
        missingResult.IsFailure.Should().BeTrue();
        missingResult.Error.Code.Should().Be("Employee.NotFound");
        otherCompanyResult.IsFailure.Should().BeTrue();
        otherCompanyResult.Error.Code.Should().Be("Employee.NotFound");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidUpdatePromotesSecondarySkillToPrimaryRole_ShouldReplaceSkillsAndPersist()
    {
        // Arrange
        var (company, employee) = SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        var command = new UpdateEmployeeCommand(
            employee.Id,
            "Mariana Souza",
            StationRole.Bar,
            [
                new SecondarySkillDto(StationRole.Waiter, SkillProficiency.Autonomous),
                new SecondarySkillDto(StationRole.Cashier, SkillProficiency.Support)
            ],
            [DayOfWeek.Tuesday],
            [DayOfWeek.Saturday],
            company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Mariana Souza");
        result.Value.PrimaryRole.Should().Be(StationRole.Bar);
        result.Value.SecondarySkills.Should().BeEquivalentTo(
        [
            new SecondarySkillDto(StationRole.Waiter, SkillProficiency.Autonomous),
            new SecondarySkillDto(StationRole.Cashier, SkillProficiency.Support)
        ]);
        result.Value.PreferredDaysOff.Should().Equal(DayOfWeek.Tuesday);
        result.Value.UnavailableDays.Should().Equal(DayOfWeek.Saturday);

        _employeeRepository.UpdatedEmployees.Should().Contain(employee);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAvailabilityValidationFails_ShouldNotMutateEmployeeOrSaveChanges()
    {
        // Arrange
        var (company, employee) = SeedCompanyAndEmployee();
        var useCase = CreateUseCase();

        var command = new UpdateEmployeeCommand(
            employee.Id,
            "Nome Alterado Indevido",
            StationRole.Kitchen,
            [new SecondarySkillDto(StationRole.Cashier, SkillProficiency.Autonomous)],
            [DayOfWeek.Wednesday],
            [DayOfWeek.Wednesday],
            company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.ConflictingDayConstraint");
        employee.Name.Should().Be("Mariana Silva");
        employee.PrimaryRole.Should().Be(StationRole.Waiter);
        employee.SecondarySkills.Should().ContainSingle(s => s.Role == StationRole.Bar);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }
}

