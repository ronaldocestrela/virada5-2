namespace Application.DTOs.Financial;

public enum HeadcountSource
{
    ExplicitInput = 1,
    ActiveEmployeesRoster = 2
}

public sealed record FinancialSimulationAssumptionsDto(
    decimal MonthlyWorkHoursDivisor,
    decimal EmployerChargesRate,
    decimal OvertimeSurchargeRate,
    decimal DsrOnOvertimeRate,
    decimal OptimizedOvertimeReductionRate,
    decimal AnnualTurnoverRate6x1,
    decimal AnnualTurnoverRate5x2,
    decimal TurnoverReplacementCostMonths,
    decimal MonthlyLaborLiabilityRate6x1,
    decimal MonthlyLaborLiabilityRate5x2);

public sealed record ScenarioFinancialBreakdownDto(
    string ScenarioName,
    decimal MonthlyBasePayroll,
    decimal MonthlyEmployerChargesOnBase,
    decimal MonthlyOvertimeHoursPerEmployee,
    decimal TotalMonthlyOvertimeHours,
    decimal MonthlyOvertimeBaseAndDsrCost,
    decimal MonthlyEmployerChargesOnOvertime,
    decimal MonthlyOvertimeTotalCost,
    decimal MonthlyTurnoverCost,
    decimal MonthlyLaborLiabilityProvision,
    decimal TotalMonthlyCost,
    decimal AnnualTotalCost);

public sealed record FinancialSimulationContextDto(
    Guid CompanyId,
    string CompanyName,
    int ActiveEmployeesCount,
    bool HasDemandMatrix,
    FinancialSimulationAssumptionsDto DefaultAssumptions);

public sealed record FinancialImpactSimulationDto(
    Guid CompanyId,
    string CompanyName,
    int HeadcountUsed,
    HeadcountSource HeadcountSource,
    decimal AverageMonthlyBaseSalary,
    FinancialSimulationAssumptionsDto Assumptions,
    ScenarioFinancialBreakdownDto CurrentScenario6x1,
    ScenarioFinancialBreakdownDto OptimizedScenario5x2,
    decimal MonthlySavings,
    decimal AnnualSavings,
    decimal SavingsPercentage,
    decimal OvertimeHoursSavedPerMonth,
    bool HasDemandMatrix,
    decimal? DemandIdlenessPercentage,
    decimal? EstimatedIdlePayrollExposure6x1,
    IReadOnlyList<string> Warnings);

