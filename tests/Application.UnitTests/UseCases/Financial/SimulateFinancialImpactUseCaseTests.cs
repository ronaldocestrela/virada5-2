using Application.DTOs.Financial;
using Application.UnitTests.Fakes;
using Application.UseCases.Financial;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Financial;

public class SimulateFinancialImpactUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private SimulateFinancialImpactUseCase CreateUseCase() =>
        new(_companyRepository, _employeeRepository, _demandSlotRepository, _currentUserService);

    private Company SeedCompany(
        TimeOnly? opening = null,
        TimeOnly? closing = null,
        ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff,
        params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Viabilidade",
            BusinessType.Restaurant,
            opening ?? new TimeOnly(11, 0),
            closing ?? new TimeOnly(14, 0),
            scheduleModel,
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
    public async Task ExecuteAsync_WhenCommandIsNull_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.InvalidCommand");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, 16m, Headcount: 10);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, 16m, Headcount: 10, CompanyId: Guid.NewGuid());

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1500)]
    public async Task ExecuteAsync_WhenAverageMonthlyBaseSalaryIsZeroOrNegative_ShouldReturnValidationFailure(decimal salary)
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(salary, 16m, Headcount: 10, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.InvalidSalary");
    }

    [Fact]
    public async Task ExecuteAsync_WhenMonthlyOvertimeHoursIsNegative_ShouldReturnValidationFailure()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, -1m, Headcount: 10, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.InvalidOvertimeHours");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ExecuteAsync_WhenExplicitHeadcountIsNonPositive_ShouldReturnValidationFailure(int headcount)
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, 16m, Headcount: headcount, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.InvalidHeadcount");
    }

    [Fact]
    public async Task ExecuteAsync_WhenHeadcountIsOmittedAndNoActiveEmployeesExist_ShouldReturnValidationFailure()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Colaborador Inativo", isActive: false);
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, 16m, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FinancialSimulation.HeadcountRequired");
    }

    [Theory]
    [InlineData(0, 0.68, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidWorkHoursDivisor")]
    [InlineData(301, 0.68, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidWorkHoursDivisor")]
    [InlineData(220, -0.01, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidEmployerChargesRate")]
    [InlineData(220, 2.01, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidEmployerChargesRate")]
    [InlineData(220, 0.68, -0.01, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidOvertimeSurchargeRate")]
    [InlineData(220, 0.68, 0.50, -0.01, 0.70, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidDsrRate")]
    [InlineData(220, 0.68, 0.50, 0.20, -0.01, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidOvertimeReductionRate")]
    [InlineData(220, 0.68, 0.50, 0.20, 1.01, 0.65, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidOvertimeReductionRate")]
    [InlineData(220, 0.68, 0.50, 0.20, 0.70, -0.01, 0.25, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidTurnoverRate")]
    [InlineData(220, 0.68, 0.50, 0.20, 0.70, 0.65, 5.01, 1.5, 0.08, 0.01, "FinancialSimulation.InvalidTurnoverRate")]
    [InlineData(220, 0.68, 0.50, 0.20, 0.70, 0.65, 0.25, -0.1, 0.08, 0.01, "FinancialSimulation.InvalidTurnoverReplacementCost")]
    [InlineData(220, 0.68, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, -0.01, 0.01, "FinancialSimulation.InvalidLaborLiabilityRate")]
    [InlineData(220, 0.68, 0.50, 0.20, 0.70, 0.65, 0.25, 1.5, 0.08, 1.01, "FinancialSimulation.InvalidLaborLiabilityRate")]
    public async Task ExecuteAsync_WhenOptionalRateParametersAreOutOfBounds_ShouldReturnValidationFailure(
        decimal divisor,
        decimal charges,
        decimal overtimeSurcharge,
        decimal dsrRate,
        decimal overtimeReduction,
        decimal turnover6x1,
        decimal turnover5x2,
        decimal replacementMonths,
        decimal liability6x1,
        decimal liability5x2,
        string expectedErrorCode)
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(
            2200m,
            16m,
            Headcount: 10,
            CompanyId: company.Id,
            MonthlyWorkHoursDivisor: divisor,
            EmployerChargesRate: charges,
            OvertimeSurchargeRate: overtimeSurcharge,
            DsrOnOvertimeRate: dsrRate,
            OptimizedOvertimeReductionRate: overtimeReduction,
            AnnualTurnoverRate6x1: turnover6x1,
            AnnualTurnoverRate5x2: turnover5x2,
            TurnoverReplacementCostMonths: replacementMonths,
            MonthlyLaborLiabilityRate6x1: liability6x1,
            MonthlyLaborLiabilityRate5x2: liability5x2);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExplicitHeadcountAndDefaultAssumptionsAreProvided_ShouldCalculate6x1Vs5x2SavingsAccurately()
    {
        // Arrange
        // 10 colaboradores, R$ 2.200,00 salário base (hora normal = R$ 10,00), 10h extras/mês por colaborador no 6x1.
        // Folha base = 22.000,00 | Encargos base (68%) = 14.960,00
        // 6x1 HE: 100h * 10 * 1.5 * 1.2 = 1.800,00 | Encargos HE = 1.224,00 | Total HE = 3.024,00
        // 6x1 Turnover: (10 * 0.65 / 12) * (2200 * 1.5) = 1.787,50
        // 6x1 Passivo: 22.000 * 0.08 = 1.760,00
        // Total 6x1 = 22.000 + 14.960 + 3.024 + 1.787,50 + 1.760 = 43.531,50
        // 5x2 HE (redução 70% -> 3h/colab = 30h): 30h * 10 * 1.5 * 1.2 = 540,00 | Encargos HE = 367,20 | Total HE = 907,20
        // 5x2 Turnover: (10 * 0.25 / 12) * (2200 * 1.5) = 687,50
        // 5x2 Passivo: 22.000 * 0.01 = 220,00
        // Total 5x2 = 22.000 + 14.960 + 907,20 + 687,50 + 220 = 38.774,70
        // Economia mensal = 43.531,50 - 38.774,70 = 4.756,80 | Anual = 57.081,60
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(
            AverageMonthlyBaseSalary: 2200m,
            MonthlyOvertimeHoursPerEmployee: 10m,
            Headcount: 10);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var sim = result.Value;

        sim.CompanyId.Should().Be(company.Id);
        sim.CompanyName.Should().Be("Restaurante Viabilidade");
        sim.HeadcountUsed.Should().Be(10);
        sim.HeadcountSource.Should().Be(HeadcountSource.ExplicitInput);
        sim.AverageMonthlyBaseSalary.Should().Be(2200m);

        sim.CurrentScenario6x1.ScenarioName.Should().Be("6x1 Atual");
        sim.CurrentScenario6x1.MonthlyBasePayroll.Should().Be(22000.00m);
        sim.CurrentScenario6x1.MonthlyEmployerChargesOnBase.Should().Be(14960.00m);
        sim.CurrentScenario6x1.MonthlyOvertimeHoursPerEmployee.Should().Be(10.00m);
        sim.CurrentScenario6x1.TotalMonthlyOvertimeHours.Should().Be(100.00m);
        sim.CurrentScenario6x1.MonthlyOvertimeBaseAndDsrCost.Should().Be(1800.00m);
        sim.CurrentScenario6x1.MonthlyEmployerChargesOnOvertime.Should().Be(1224.00m);
        sim.CurrentScenario6x1.MonthlyOvertimeTotalCost.Should().Be(3024.00m);
        sim.CurrentScenario6x1.MonthlyTurnoverCost.Should().Be(1787.50m);
        sim.CurrentScenario6x1.MonthlyLaborLiabilityProvision.Should().Be(1760.00m);
        sim.CurrentScenario6x1.TotalMonthlyCost.Should().Be(43531.50m);
        sim.CurrentScenario6x1.AnnualTotalCost.Should().Be(522378.00m);

        sim.OptimizedScenario5x2.ScenarioName.Should().Be("5x2 Otimizado");
        sim.OptimizedScenario5x2.MonthlyBasePayroll.Should().Be(22000.00m);
        sim.OptimizedScenario5x2.MonthlyEmployerChargesOnBase.Should().Be(14960.00m);
        sim.OptimizedScenario5x2.MonthlyOvertimeHoursPerEmployee.Should().Be(3.00m);
        sim.OptimizedScenario5x2.TotalMonthlyOvertimeHours.Should().Be(30.00m);
        sim.OptimizedScenario5x2.MonthlyOvertimeBaseAndDsrCost.Should().Be(540.00m);
        sim.OptimizedScenario5x2.MonthlyEmployerChargesOnOvertime.Should().Be(367.20m);
        sim.OptimizedScenario5x2.MonthlyOvertimeTotalCost.Should().Be(907.20m);
        sim.OptimizedScenario5x2.MonthlyTurnoverCost.Should().Be(687.50m);
        sim.OptimizedScenario5x2.MonthlyLaborLiabilityProvision.Should().Be(220.00m);
        sim.OptimizedScenario5x2.TotalMonthlyCost.Should().Be(38774.70m);
        sim.OptimizedScenario5x2.AnnualTotalCost.Should().Be(465296.40m);

        sim.MonthlySavings.Should().Be(4756.80m);
        sim.AnnualSavings.Should().Be(57081.60m);
        sim.SavingsPercentage.Should().Be(10.93m);
        sim.OvertimeHoursSavedPerMonth.Should().Be(70.00m);
        sim.HasDemandMatrix.Should().BeFalse();
        sim.DemandIdlenessPercentage.Should().BeNull();
        sim.EstimatedIdlePayrollExposure6x1.Should().BeNull();
        sim.Warnings.Should().ContainSingle(w => w.Contains("matriz de demanda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenHeadcountIsNull_ShouldUseActiveEmployeesCountAndExcludeInactiveEmployees()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", isActive: true);
        SeedEmployee(company.Id, "Bruno", isActive: true);
        SeedEmployee(company.Id, "Carlos", isActive: false);

        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(
            AverageMonthlyBaseSalary: 2000m,
            MonthlyOvertimeHoursPerEmployee: 0m,
            Headcount: null,
            CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HeadcountUsed.Should().Be(2);
        result.Value.HeadcountSource.Should().Be(HeadcountSource.ActiveEmployeesRoster);
        result.Value.CurrentScenario6x1.MonthlyBasePayroll.Should().Be(4000.00m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCustomAssumptionsAreProvided_ShouldApplyOverridesInCalculation()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(
            AverageMonthlyBaseSalary: 2000m,
            MonthlyOvertimeHoursPerEmployee: 10m,
            Headcount: 5,
            CompanyId: company.Id,
            MonthlyWorkHoursDivisor: 200m,
            EmployerChargesRate: 0.50m,
            OvertimeSurchargeRate: 1.00m,
            DsrOnOvertimeRate: 0.25m,
            OptimizedOvertimeReductionRate: 1.00m,
            AnnualTurnoverRate6x1: 0.60m,
            AnnualTurnoverRate5x2: 0.00m,
            TurnoverReplacementCostMonths: 1.00m,
            MonthlyLaborLiabilityRate6x1: 0.10m,
            MonthlyLaborLiabilityRate5x2: 0.00m);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var sim = result.Value;

        sim.Assumptions.MonthlyWorkHoursDivisor.Should().Be(200m);
        sim.Assumptions.EmployerChargesRate.Should().Be(0.50m);
        sim.Assumptions.OptimizedOvertimeReductionRate.Should().Be(1.00m);

        // Hora normal = 10; 50h * 10 * 2.0 * 1.25 = 1.250,00 + 50% encargos (625,00) = 1.875,00
        sim.CurrentScenario6x1.MonthlyOvertimeTotalCost.Should().Be(1875.00m);
        // Turnover 6x1: (5 * 0.60 / 12) * 2000 = 500,00
        sim.CurrentScenario6x1.MonthlyTurnoverCost.Should().Be(500.00m);
        // Passivo 6x1: 10.000 * 0.10 = 1.000,00
        sim.CurrentScenario6x1.MonthlyLaborLiabilityProvision.Should().Be(1000.00m);

        // 5x2 zerado nos itens variáveis customizados
        sim.OptimizedScenario5x2.MonthlyOvertimeTotalCost.Should().Be(0.00m);
        sim.OptimizedScenario5x2.MonthlyTurnoverCost.Should().Be(0.00m);
        sim.OptimizedScenario5x2.MonthlyLaborLiabilityProvision.Should().Be(0.00m);
        sim.MonthlySavings.Should().Be(3375.00m);
        sim.OvertimeHoursSavedPerMonth.Should().Be(50.00m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenZeroOvertimeHoursProvided_ShouldStillCalculateTurnoverAndLiabilitySavingsWithoutOvertime()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2200m, 0m, Headcount: 4, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentScenario6x1.MonthlyOvertimeTotalCost.Should().Be(0.00m);
        result.Value.OptimizedScenario5x2.MonthlyOvertimeTotalCost.Should().Be(0.00m);
        result.Value.OvertimeHoursSavedPerMonth.Should().Be(0.00m);
        result.Value.MonthlySavings.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDemandSlotsExist_ShouldComputeIdlenessAndIdlePayrollExposureWithoutPersistingMutations()
    {
        // Arrange
        // Aberto 11h-14h (horas 11, 12, 13). Segunda fechada.
        var company = SeedCompany(
            new TimeOnly(11, 0),
            new TimeOnly(14, 0),
            ScheduleModel.FixedClosure,
            DayOfWeek.Monday);

        var tenantId = _currentUserService.TenantId;
        var slots = new[]
        {
            // Pico = 100
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 11, 100m).Value, // 1.00 -> Peak
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 60m).Value,  // 0.60 -> Moderate
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 11, 20m).Value, // 0.20 -> Idle
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 12, 50m).Value, // 0.50 -> Low
            // Fora da janela aberta (não deve entrar na taxa de ociosidade)
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Monday, 12, 0m).Value
        };
        _demandSlotRepository.Seed(slots);

        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(
            AverageMonthlyBaseSalary: 2000m,
            MonthlyOvertimeHoursPerEmployee: 10m,
            Headcount: 10,
            CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var sim = result.Value;

        sim.HasDemandMatrix.Should().BeTrue();
        // 2 de 4 slots abertos são ociosos/baixos -> 50%
        sim.DemandIdlenessPercentage.Should().Be(50.00m);
        // Folha base com encargos = 20.000 + 13.600 = 33.600 -> 50% = 16.800,00
        sim.EstimatedIdlePayrollExposure6x1.Should().Be(16800.00m);
        sim.Warnings.Should().BeEmpty();

        // Garante que os slots persistidos não sofreram mutação
        _demandSlotRepository.UpdatedSlots.Should().BeEmpty();
        slots.Should().OnlyContain(s => s.RelativeVolume == 0m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllOpenDemandSlotsHaveZeroVolume_ShouldReport100PercentIdleness()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(13, 0));
        var tenantId = _currentUserService.TenantId;
        _demandSlotRepository.Seed(
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 11, 0m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 0m).Value);

        var useCase = CreateUseCase();
        var command = new SimulateFinancialImpactCommand(2000m, 5m, Headcount: 5, CompanyId: company.Id);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasDemandMatrix.Should().BeTrue();
        result.Value.DemandIdlenessPercentage.Should().Be(100.00m);
    }
}

