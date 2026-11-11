namespace Application.UseCases.Financial;

public sealed record SimulateFinancialImpactCommand(
    decimal AverageMonthlyBaseSalary,
    decimal MonthlyOvertimeHoursPerEmployee,
    int? Headcount = null,
    Guid? CompanyId = null,
    decimal? MonthlyWorkHoursDivisor = null,
    decimal? EmployerChargesRate = null,
    decimal? OvertimeSurchargeRate = null,
    decimal? DsrOnOvertimeRate = null,
    decimal? OptimizedOvertimeReductionRate = null,
    decimal? AnnualTurnoverRate6x1 = null,
    decimal? AnnualTurnoverRate5x2 = null,
    decimal? TurnoverReplacementCostMonths = null,
    decimal? MonthlyLaborLiabilityRate6x1 = null,
    decimal? MonthlyLaborLiabilityRate5x2 = null);

