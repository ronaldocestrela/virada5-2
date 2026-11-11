using Application.UnitTests.Fakes;
using Application.UseCases.Financial;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Financial;

public class GetCurrentFinancialSimulationContextUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetCurrentFinancialSimulationContextUseCase CreateUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _currentUserService);

    private Company SeedCompany(
        TimeOnly? opening = null,
        TimeOnly? closing = null,
        params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Horizonte",
            BusinessType.Restaurant,
            opening ?? new TimeOnly(11, 0),
            closing ?? new TimeOnly(15, 0),
            closedDays.Length > 0 ? ScheduleModel.FixedClosure : ScheduleModel.RotatingDaysOff,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(Guid companyId, string name, bool isActive = true)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            StationRole.Waiter).Value;

        if (!isActive)
        {
            employee.Deactivate();
        }

        _employeeRepository.Seed(employee);
        return employee;
    }

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
        result.Error.Code.Should().Be("FinancialSimulation.EmptyTenant");
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
    public async Task ExecuteAsync_WhenCompanyExistsWithoutEmployeesOrDemand_ShouldReturnZeroHeadcountAndFalseDemandFlag()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.CompanyName.Should().Be("Restaurante Horizonte");
        result.Value.ActiveEmployeesCount.Should().Be(0);
        result.Value.HasDemandMatrix.Should().BeFalse();
        result.Value.DefaultAssumptions.Should().Be(SimulateFinancialImpactUseCase.DefaultAssumptions);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasActiveAndInactiveEmployees_ShouldCountOnlyActiveEmployees()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", isActive: true);
        SeedEmployee(company.Id, "Bruno", isActive: true);
        SeedEmployee(company.Id, "Carlos", isActive: false);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveEmployeesCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasDemandOnlyInClosedHours_ShouldReturnHasDemandMatrixFalse()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(15, 0), DayOfWeek.Monday);
        _demandSlotRepository.Seed(
            DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Monday, 12, 40m).Value,
            DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Tuesday, 22, 60m).Value);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasDemandMatrix.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasOpenDemandAndExplicitCompanyId_ShouldReturnFullContextAndDefaultAssumptions()
    {
        // Arrange
        var firstCompany = SeedCompany();
        var secondCompany = Company.Create(
            _currentUserService.TenantId,
            "Café Esquina",
            BusinessType.CafeBakery,
            new TimeOnly(8, 0),
            new TimeOnly(18, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(secondCompany);

        SeedEmployee(firstCompany.Id, "Ana", isActive: true);
        SeedEmployee(secondCompany.Id, "Diana", isActive: true);
        _demandSlotRepository.Seed(
            DemandSlot.Create(_currentUserService.TenantId, secondCompany.Id, DayOfWeek.Friday, 10, 95m).Value);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(secondCompany.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(secondCompany.Id);
        result.Value.CompanyName.Should().Be("Café Esquina");
        result.Value.ActiveEmployeesCount.Should().Be(1);
        result.Value.HasDemandMatrix.Should().BeTrue();
        result.Value.DefaultAssumptions.Should().Be(SimulateFinancialImpactUseCase.DefaultAssumptions);
    }
}

