using Application.Common.Interfaces;
using Application.DTOs.Financial;
using Application.UseCases.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Financial;

/// <summary>
/// Caso de uso de simulação financeira comparativa entre a escala atual 6x1 e a escala 5x2 otimizada.
/// </summary>
public sealed class SimulateFinancialImpactUseCase
{
    public static readonly FinancialSimulationAssumptionsDto DefaultAssumptions = new(
        MonthlyWorkHoursDivisor: 220m,
        EmployerChargesRate: 0.68m,
        OvertimeSurchargeRate: 0.50m,
        DsrOnOvertimeRate: 0.20m,
        OptimizedOvertimeReductionRate: 0.70m,
        AnnualTurnoverRate6x1: 0.65m,
        AnnualTurnoverRate5x2: 0.25m,
        TurnoverReplacementCostMonths: 1.50m,
        MonthlyLaborLiabilityRate6x1: 0.08m,
        MonthlyLaborLiabilityRate5x2: 0.01m);

    private const int MonthsPerYear = 12;

    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;

    public SimulateFinancialImpactUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<FinancialImpactSimulationDto>> ExecuteAsync(
        SimulateFinancialImpactCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<FinancialImpactSimulationDto>(
                Error.Validation("FinancialSimulation.InvalidCommand", "Comando de simulação financeira inválido."));
        }

        var validationResult = ValidateAndResolveAssumptions(command);
        if (validationResult.IsFailure)
        {
            return Result.Failure<FinancialImpactSimulationDto>(validationResult.Error);
        }

        var contextResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            command.CompanyId,
            _companyRepository,
            _currentUserService,
            "FinancialSimulation.EmptyTenant",
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<FinancialImpactSimulationDto>(contextResult.Error);
        }

        var (_, company) = contextResult.Value;

        var headcountResult = await ResolveHeadcountAsync(command.Headcount, company.Id, ct);
        if (headcountResult.IsFailure)
        {
            return Result.Failure<FinancialImpactSimulationDto>(headcountResult.Error);
        }

        var (headcount, headcountSource) = headcountResult.Value;
        var assumptions = validationResult.Value;

        var scenario6x1 = CalculateScenarioBreakdown(
            "6x1 Atual",
            headcount,
            command.AverageMonthlyBaseSalary,
            command.MonthlyOvertimeHoursPerEmployee,
            assumptions.AnnualTurnoverRate6x1,
            assumptions.MonthlyLaborLiabilityRate6x1,
            assumptions);

        var optimizedOvertimePerEmployee = RoundMoney(
            command.MonthlyOvertimeHoursPerEmployee * (1m - assumptions.OptimizedOvertimeReductionRate));

        var scenario5x2 = CalculateScenarioBreakdown(
            "5x2 Otimizado",
            headcount,
            command.AverageMonthlyBaseSalary,
            optimizedOvertimePerEmployee,
            assumptions.AnnualTurnoverRate5x2,
            assumptions.MonthlyLaborLiabilityRate5x2,
            assumptions);

        var monthlySavings = RoundMoney(scenario6x1.TotalMonthlyCost - scenario5x2.TotalMonthlyCost);
        var annualSavings = RoundMoney(monthlySavings * MonthsPerYear);
        var savingsPercentage = scenario6x1.TotalMonthlyCost > 0m
            ? RoundMoney(monthlySavings * 100m / scenario6x1.TotalMonthlyCost)
            : 0m;
        var overtimeHoursSavedPerMonth = RoundMoney(
            scenario6x1.TotalMonthlyOvertimeHours - scenario5x2.TotalMonthlyOvertimeHours);

        var warnings = new List<string>();
        var basePayrollWithCharges = scenario6x1.MonthlyBasePayroll + scenario6x1.MonthlyEmployerChargesOnBase;
        var (hasDemandMatrix, idlenessPercentage, idleExposure) = await EvaluateDemandIdlenessAsync(
            company,
            basePayrollWithCharges,
            warnings,
            ct);

        return Result.Success(new FinancialImpactSimulationDto(
            company.Id,
            company.Name,
            headcount,
            headcountSource,
            RoundMoney(command.AverageMonthlyBaseSalary),
            assumptions,
            scenario6x1,
            scenario5x2,
            monthlySavings,
            annualSavings,
            savingsPercentage,
            overtimeHoursSavedPerMonth,
            hasDemandMatrix,
            idlenessPercentage,
            idleExposure,
            warnings.AsReadOnly()));
    }

    private static Result<FinancialSimulationAssumptionsDto> ValidateAndResolveAssumptions(
        SimulateFinancialImpactCommand command)
    {
        if (command.AverageMonthlyBaseSalary <= 0m)
        {
            return Result.Failure<FinancialSimulationAssumptionsDto>(
                Error.Validation(
                    "FinancialSimulation.InvalidSalary",
                    "O salário base médio mensal deve ser maior que zero."));
        }

        if (command.MonthlyOvertimeHoursPerEmployee < 0m)
        {
            return Result.Failure<FinancialSimulationAssumptionsDto>(
                Error.Validation(
                    "FinancialSimulation.InvalidOvertimeHours",
                    "As horas extras mensais por colaborador não podem ser negativas."));
        }

        if (command.Headcount is <= 0)
        {
            return Result.Failure<FinancialSimulationAssumptionsDto>(
                Error.Validation(
                    "FinancialSimulation.InvalidHeadcount",
                    "A quantidade de colaboradores informada deve ser maior que zero."));
        }

        var divisor = command.MonthlyWorkHoursDivisor ?? DefaultAssumptions.MonthlyWorkHoursDivisor;
        var charges = command.EmployerChargesRate ?? DefaultAssumptions.EmployerChargesRate;
        var overtimeSurcharge = command.OvertimeSurchargeRate ?? DefaultAssumptions.OvertimeSurchargeRate;
        var dsrRate = command.DsrOnOvertimeRate ?? DefaultAssumptions.DsrOnOvertimeRate;
        var overtimeReduction = command.OptimizedOvertimeReductionRate ?? DefaultAssumptions.OptimizedOvertimeReductionRate;
        var turnover6x1 = command.AnnualTurnoverRate6x1 ?? DefaultAssumptions.AnnualTurnoverRate6x1;
        var turnover5x2 = command.AnnualTurnoverRate5x2 ?? DefaultAssumptions.AnnualTurnoverRate5x2;
        var replacementMonths = command.TurnoverReplacementCostMonths ?? DefaultAssumptions.TurnoverReplacementCostMonths;
        var liability6x1 = command.MonthlyLaborLiabilityRate6x1 ?? DefaultAssumptions.MonthlyLaborLiabilityRate6x1;
        var liability5x2 = command.MonthlyLaborLiabilityRate5x2 ?? DefaultAssumptions.MonthlyLaborLiabilityRate5x2;

        if (divisor <= 0m || divisor > 300m)
        {
            return Result.Failure<FinancialSimulationAssumptionsDto>(
                Error.Validation(
                    "FinancialSimulation.InvalidWorkHoursDivisor",
                    "O divisor mensal de horas deve estar entre 1 e 300 horas."));
        }

        var checks = new[]
        {
            ValidateRange(charges, 0m, 2m, "FinancialSimulation.InvalidEmployerChargesRate", "O percentual de encargos patronais deve estar entre 0 e 200%."),
            ValidateRange(overtimeSurcharge, 0m, 2m, "FinancialSimulation.InvalidOvertimeSurchargeRate", "O adicional de hora extra deve estar entre 0 e 200%."),
            ValidateRange(dsrRate, 0m, 1m, "FinancialSimulation.InvalidDsrRate", "O reflexo de DSR sobre horas extras deve estar entre 0 e 100%."),
            ValidateRange(overtimeReduction, 0m, 1m, "FinancialSimulation.InvalidOvertimeReductionRate", "A taxa de redução de horas extras no 5x2 deve estar entre 0 e 100%."),
            ValidateRange(turnover6x1, 0m, 5m, "FinancialSimulation.InvalidTurnoverRate", "A taxa anual de turnover deve estar entre 0 e 500%."),
            ValidateRange(turnover5x2, 0m, 5m, "FinancialSimulation.InvalidTurnoverRate", "A taxa anual de turnover deve estar entre 0 e 500%."),
            ValidateRange(replacementMonths, 0m, 12m, "FinancialSimulation.InvalidTurnoverReplacementCost", "O custo de reposição por desligamento deve estar entre 0 e 12 salários."),
            ValidateRange(liability6x1, 0m, 1m, "FinancialSimulation.InvalidLaborLiabilityRate", "A taxa mensal de provisão de passivo trabalhista deve estar entre 0 e 100%."),
            ValidateRange(liability5x2, 0m, 1m, "FinancialSimulation.InvalidLaborLiabilityRate", "A taxa mensal de provisão de passivo trabalhista deve estar entre 0 e 100%.")
        };

        var firstFailure = checks.FirstOrDefault(r => r.IsFailure);
        if (firstFailure is not null)
        {
            return Result.Failure<FinancialSimulationAssumptionsDto>(firstFailure.Error);
        }

        return Result.Success(new FinancialSimulationAssumptionsDto(
            divisor,
            charges,
            overtimeSurcharge,
            dsrRate,
            overtimeReduction,
            turnover6x1,
            turnover5x2,
            replacementMonths,
            liability6x1,
            liability5x2));
    }

    private static Result ValidateRange(decimal value, decimal min, decimal max, string code, string message) =>
        value < min || value > max
            ? Result.Failure(Error.Validation(code, message))
            : Result.Success();

    private async Task<Result<(int Headcount, HeadcountSource Source)>> ResolveHeadcountAsync(
        int? explicitHeadcount,
        Guid companyId,
        CancellationToken ct)
    {
        if (explicitHeadcount.HasValue)
        {
            return Result.Success((explicitHeadcount.Value, HeadcountSource.ExplicitInput));
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(companyId, ct);
        if (activeEmployees.Count == 0)
        {
            return Result.Failure<(int, HeadcountSource)>(
                Error.Validation(
                    "FinancialSimulation.HeadcountRequired",
                    "Informe a quantidade de colaboradores ou cadastre ao menos um colaborador ativo para simular."));
        }

        return Result.Success((activeEmployees.Count, HeadcountSource.ActiveEmployeesRoster));
    }

    private static ScenarioFinancialBreakdownDto CalculateScenarioBreakdown(
        string scenarioName,
        int headcount,
        decimal averageMonthlyBaseSalary,
        decimal monthlyOvertimeHoursPerEmployee,
        decimal annualTurnoverRate,
        decimal monthlyLiabilityRate,
        FinancialSimulationAssumptionsDto assumptions)
    {
        var monthlyBasePayroll = RoundMoney(headcount * averageMonthlyBaseSalary);
        var monthlyEmployerChargesOnBase = RoundMoney(monthlyBasePayroll * assumptions.EmployerChargesRate);

        var totalOvertimeHours = RoundMoney(headcount * monthlyOvertimeHoursPerEmployee);
        var hourlyRate = averageMonthlyBaseSalary / assumptions.MonthlyWorkHoursDivisor;
        var overtimeFactor = (1m + assumptions.OvertimeSurchargeRate) * (1m + assumptions.DsrOnOvertimeRate);

        var monthlyOvertimeBaseAndDsrCost = RoundMoney(totalOvertimeHours * hourlyRate * overtimeFactor);
        var monthlyEmployerChargesOnOvertime = RoundMoney(monthlyOvertimeBaseAndDsrCost * assumptions.EmployerChargesRate);
        var monthlyOvertimeTotalCost = RoundMoney(monthlyOvertimeBaseAndDsrCost + monthlyEmployerChargesOnOvertime);

        var monthlyTurnoverCost = RoundMoney(
            (headcount * annualTurnoverRate / MonthsPerYear) *
            (averageMonthlyBaseSalary * assumptions.TurnoverReplacementCostMonths));

        var monthlyLaborLiabilityProvision = RoundMoney(monthlyBasePayroll * monthlyLiabilityRate);

        var totalMonthlyCost = RoundMoney(
            monthlyBasePayroll +
            monthlyEmployerChargesOnBase +
            monthlyOvertimeTotalCost +
            monthlyTurnoverCost +
            monthlyLaborLiabilityProvision);

        var annualTotalCost = RoundMoney(totalMonthlyCost * MonthsPerYear);

        return new ScenarioFinancialBreakdownDto(
            scenarioName,
            monthlyBasePayroll,
            monthlyEmployerChargesOnBase,
            RoundMoney(monthlyOvertimeHoursPerEmployee),
            totalOvertimeHours,
            monthlyOvertimeBaseAndDsrCost,
            monthlyEmployerChargesOnOvertime,
            monthlyOvertimeTotalCost,
            monthlyTurnoverCost,
            monthlyLaborLiabilityProvision,
            totalMonthlyCost,
            annualTotalCost);
    }

    private async Task<(bool HasMatrix, decimal? IdlenessPercentage, decimal? IdleExposure)> EvaluateDemandIdlenessAsync(
        Company company,
        decimal basePayrollWithCharges,
        List<string> warnings,
        CancellationToken ct)
    {
        var slots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var openSlots = slots
            .Where(slot => company.IsOpenAt(slot.DayOfWeek, slot.Hour))
            .ToList();

        if (openSlots.Count == 0)
        {
            warnings.Add("Nenhuma matriz de demanda aberta foi encontrada; o índice de ociosidade em folha não foi calculado.");
            return (false, null, null);
        }

        var operationalPeak = openSlots.Max(slot => slot.ExpectedVolume);
        var idleOrLowCount = 0;

        foreach (var slot in openSlots)
        {
            var evaluationCopy = DemandSlot
                .Create(slot.TenantId, slot.CompanyId, slot.DayOfWeek, slot.Hour, slot.ExpectedVolume)
                .Value;
            evaluationCopy.EvaluateRelativeDemand(operationalPeak);

            if (evaluationCopy.Classification is DemandClassification.Idle or DemandClassification.Low)
            {
                idleOrLowCount++;
            }
        }

        var idlenessPercentage = RoundMoney(idleOrLowCount * 100m / openSlots.Count);
        var idleExposure = RoundMoney(basePayrollWithCharges * idlenessPercentage / 100m);

        return (true, idlenessPercentage, idleExposure);
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

