using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters.Engine;

internal static class RosterComplianceEvaluator
{
    internal static RosterComplianceReportDto Evaluate(
        Roster roster,
        Company company,
        IReadOnlyList<Employee> employees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var alerts = new List<ComplianceAlertDto>();
        var employeeById = employees.ToDictionary(e => e.Id);

        if (!context.HasDemandMatrix)
        {
            alerts.Add(new ComplianceAlertDto(
                "Demand.NoSlotsFoundUsingDefault",
                RosterAlertSeverity.Info,
                RosterAlertCategory.SoftConstraint,
                "Matriz de demanda não cadastrada; os turnos abertos foram classificados com demanda moderada padrão."));
        }

        var isCltCompliant = ValidateCltAndOperationalConsistency(
            roster,
            company,
            employeeById,
            context,
            boundaryContexts,
            alerts);

        var coverageDetails = EvaluateCoverage(roster, context, alerts);
        var employeeSummaries = BuildEmployeeSummaries(roster, employees, boundaryContexts);

        var totalPeakRequired = coverageDetails
            .Where(c => c.DemandLevel == DemandClassification.Peak)
            .Sum(c => c.RequiredCount);

        var totalPeakCovered = coverageDetails
            .Where(c => c.DemandLevel == DemandClassification.Peak)
            .Sum(c => Math.Min(c.RequiredCount, c.AssignedPrimaryCount + c.AssignedSecondaryCount));

        var peakCoveragePct = totalPeakRequired > 0
            ? Math.Round(totalPeakCovered * 100m / totalPeakRequired, 2, MidpointRounding.AwayFromZero)
            : 100m;

        var totalRequired = coverageDetails.Sum(c => c.RequiredCount);
        var totalCovered = coverageDetails
            .Sum(c => Math.Min(c.RequiredCount, c.AssignedPrimaryCount + c.AssignedSecondaryCount));

        var overallCoveragePct = totalRequired > 0
            ? Math.Round(totalCovered * 100m / totalRequired, 2, MidpointRounding.AwayFromZero)
            : 100m;

        var preferredStats = EvaluateSoftPreferencesAndPolyvalence(
            roster,
            employeeById,
            alerts);

        return new RosterComplianceReportDto(
            roster.Id,
            roster.CompanyId,
            roster.WeekStartDate,
            roster.WeekEndDate,
            roster.Status,
            isCltCompliant,
            peakCoveragePct,
            overallCoveragePct,
            roster.Shifts.Count,
            roster.Shifts.Count(s => s.CoverageOrigin == CoverageOrigin.PrimaryRole),
            roster.Shifts.Count(s => s.CoverageOrigin == CoverageOrigin.SecondarySkill),
            preferredStats.Honored,
            preferredStats.Requested,
            coverageDetails,
            employeeSummaries,
            alerts.AsReadOnly());
    }

    private static bool ValidateCltAndOperationalConsistency(
        Roster roster,
        Company company,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts,
        List<ComplianceAlertDto> alerts)
    {
        var isCltCompliant = true;

        foreach (var shift in roster.Shifts)
        {
            if (!company.IsOpenOn(shift.Date.DayOfWeek))
            {
                isCltCompliant = false;
                alerts.Add(new ComplianceAlertDto(
                    "Roster.ShiftOnClosedDay",
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    $"Turno alocado em dia de fechamento do estabelecimento ({shift.Date:yyyy-MM-dd}).",
                    shift.Date,
                    shift.AssignedRole,
                    shift.EmployeeId));
            }

            if (!employeeById.TryGetValue(shift.EmployeeId, out var emp))
            {
                isCltCompliant = false;
                alerts.Add(new ComplianceAlertDto(
                    "Roster.UnknownEmployeeAssigned",
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    "Turno referencia colaborador não encontrado no estabelecimento.",
                    shift.Date,
                    shift.AssignedRole,
                    shift.EmployeeId));
                continue;
            }

            var demandLevel = ResolveShiftDemandLevel(shift, context);
            var eligibility = emp.EvaluateCoverageEligibility(
                shift.AssignedRole,
                demandLevel,
                shift.Date.DayOfWeek);

            if (eligibility.IsFailure)
            {
                isCltCompliant = false;
                alerts.Add(new ComplianceAlertDto(
                    eligibility.Error.Code,
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    eligibility.Error.Message,
                    shift.Date,
                    shift.AssignedRole,
                    shift.EmployeeId));
            }
        }

        foreach (var group in roster.Shifts.GroupBy(s => s.EmployeeId))
        {
            var employeeShifts = group.OrderBy(s => s.StartDateTime).ToList();
            boundaryContexts.TryGetValue(group.Key, out var boundary);

            var workedDays = employeeShifts.Select(s => s.Date).Distinct().Count();
            if (workedDays > Roster.MaxWorkDaysPerWeek)
            {
                isCltCompliant = false;
                alerts.Add(new ComplianceAlertDto(
                    "Roster.WeeklyRestDaysViolated",
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    $"Colaborador excedeu o máximo de {Roster.MaxWorkDaysPerWeek} dias trabalhados na escala 5x2.",
                    EmployeeId: group.Key));
            }

            for (var i = 0; i < employeeShifts.Count; i++)
            {
                var current = employeeShifts[i];

                if (i > 0)
                {
                    var previous = employeeShifts[i - 1];
                    if (previous.OverlapsWith(current))
                    {
                        isCltCompliant = false;
                        alerts.Add(new ComplianceAlertDto(
                            "Roster.ShiftOverlap",
                            RosterAlertSeverity.Error,
                            RosterAlertCategory.CltHardConstraint,
                            $"Turnos sobrepostos detectados para o colaborador em {current.Date:yyyy-MM-dd}.",
                            current.Date,
                            current.AssignedRole,
                            group.Key));
                    }
                    else if (!previous.MeetsInterShiftRestWith(current))
                    {
                        isCltCompliant = false;
                        alerts.Add(new ComplianceAlertDto(
                            "Roster.InsufficientInterShiftRest",
                            RosterAlertSeverity.Error,
                            RosterAlertCategory.CltHardConstraint,
                            $"Intervalo interjornada inferior a {Shift.MinInterShiftRestHours}h antes do turno de {current.Date:yyyy-MM-dd}.",
                            current.Date,
                            current.AssignedRole,
                            group.Key));
                    }
                }

                if (boundary.PreviousWeekLastShiftEnd.HasValue &&
                    current.StartDateTime - boundary.PreviousWeekLastShiftEnd.Value < Shift.MinimumInterShiftRest)
                {
                    isCltCompliant = false;
                    alerts.Add(new ComplianceAlertDto(
                        "Roster.InsufficientInterShiftRest",
                        RosterAlertSeverity.Error,
                        RosterAlertCategory.CltHardConstraint,
                        $"Intervalo interjornada inferior a {Shift.MinInterShiftRestHours}h em relação ao último turno da semana anterior.",
                        current.Date,
                        current.AssignedRole,
                        group.Key));
                }

                if (boundary.NextWeekFirstShiftStart.HasValue &&
                    boundary.NextWeekFirstShiftStart.Value - current.EndDateTime < Shift.MinimumInterShiftRest)
                {
                    isCltCompliant = false;
                    alerts.Add(new ComplianceAlertDto(
                        "Roster.InsufficientInterShiftRest",
                        RosterAlertSeverity.Error,
                        RosterAlertCategory.CltHardConstraint,
                        $"Intervalo interjornada inferior a {Shift.MinInterShiftRestHours}h até o primeiro turno da semana seguinte.",
                        current.Date,
                        current.AssignedRole,
                        group.Key));
                }

                if (current.Date.DayOfWeek == DayOfWeek.Sunday &&
                    !roster.SundayPolicy.CanWorkSunday(boundary.ConsecutiveSundaysWorkedBeforeWeek))
                {
                    isCltCompliant = false;
                    alerts.Add(new ComplianceAlertDto(
                        "Roster.SundayRotationViolated",
                        RosterAlertSeverity.Error,
                        RosterAlertCategory.CltHardConstraint,
                        "Limite periódico de domingos consecutivos trabalhados excedido.",
                        current.Date,
                        current.AssignedRole,
                        group.Key));
                }
            }
        }

        return isCltCompliant;
    }

    private static DemandClassification ResolveShiftDemandLevel(Shift shift, RosterPlanningContext context)
    {
        var matched = context.Requirements.FirstOrDefault(r =>
            r.Date == shift.Date &&
            r.Template.StartTime == shift.StartTime &&
            r.Template.EndTime == shift.EndTime &&
            r.Role == shift.AssignedRole);

        if (matched is not null)
        {
            return matched.DemandLevel;
        }

        if (!context.HasDemandMatrix)
        {
            return DemandClassification.Moderate;
        }

        return context.DemandMatrix
            .Where(kvp => kvp.Key.Day == shift.Date.DayOfWeek)
            .Select(kvp => kvp.Value.Classification)
            .DefaultIfEmpty(DemandClassification.Moderate)
            .Max();
    }

    private static IReadOnlyList<ShiftRequirementCoverageDto> EvaluateCoverage(
        Roster roster,
        RosterPlanningContext context,
        List<ComplianceAlertDto> alerts)
    {
        var details = new List<ShiftRequirementCoverageDto>(context.Requirements.Count);

        foreach (var req in context.Requirements)
        {
            var matchingShifts = roster.Shifts
                .Where(s =>
                    s.Date == req.Date &&
                    s.StartTime == req.Template.StartTime &&
                    s.EndTime == req.Template.EndTime &&
                    s.AssignedRole == req.Role)
                .ToList();

            var primaryCount = matchingShifts.Count(s => s.CoverageOrigin == CoverageOrigin.PrimaryRole);
            var secondaryCount = matchingShifts.Count(s => s.CoverageOrigin == CoverageOrigin.SecondarySkill);
            var totalAssigned = primaryCount + secondaryCount;

            var status = totalAssigned >= req.RequiredCount
                ? ShiftCoverageStatus.FullyCovered
                : totalAssigned > 0
                    ? ShiftCoverageStatus.PartiallyCovered
                    : ShiftCoverageStatus.Uncovered;

            if (status != ShiftCoverageStatus.FullyCovered)
            {
                if (req.DemandLevel == DemandClassification.Peak)
                {
                    alerts.Add(new ComplianceAlertDto(
                        "Roster.UncoveredPeakSlot",
                        RosterAlertSeverity.Error,
                        RosterAlertCategory.PeakCoverage,
                        $"Cobertura insuficiente em horário de pico ({req.Role} em {req.Date:yyyy-MM-dd} {req.Template.StartTime:HH\\:mm}-{req.Template.EndTime:HH\\:mm}): {totalAssigned}/{req.RequiredCount}.",
                        req.Date,
                        req.Role));
                }
                else
                {
                    alerts.Add(new ComplianceAlertDto(
                        "Roster.UncoveredStationSlot",
                        RosterAlertSeverity.Warning,
                        RosterAlertCategory.StationStaffing,
                        $"Cobertura parcial na praça {req.Role} em {req.Date:yyyy-MM-dd} ({req.Template.Name}): {totalAssigned}/{req.RequiredCount}.",
                        req.Date,
                        req.Role));
                }
            }

            details.Add(new ShiftRequirementCoverageDto(
                req.Date,
                req.DayOfWeek,
                req.Template.Name,
                req.Template.StartTime,
                req.Template.EndTime,
                req.Role,
                req.DemandLevel,
                req.RequiredCount,
                primaryCount,
                secondaryCount,
                status));
        }

        return details.AsReadOnly();
    }

    private static IReadOnlyList<EmployeeRosterSummaryDto> BuildEmployeeSummaries(
        Roster roster,
        IReadOnlyList<Employee> employees,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var summaries = new List<EmployeeRosterSummaryDto>(employees.Count);

        foreach (var employee in employees.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var employeeShifts = roster.Shifts.Where(s => s.EmployeeId == employee.Id).ToList();
            var workedDaysCount = roster.GetEmployeeWorkedDaysCount(employee.Id);
            var daysOff = roster.GetEmployeeDaysOff(employee.Id);
            var workedSunday = employeeShifts.Any(s => s.Date.DayOfWeek == DayOfWeek.Sunday);
            var previousStreak = boundaryContexts.TryGetValue(employee.Id, out var boundary)
                ? boundary.ConsecutiveSundaysWorkedBeforeWeek
                : 0;

            summaries.Add(new EmployeeRosterSummaryDto(
                employee.Id,
                employee.Name,
                employee.PrimaryRole,
                workedDaysCount,
                daysOff.Count,
                daysOff,
                employeeShifts.Count(s => s.CoverageOrigin == CoverageOrigin.PrimaryRole),
                employeeShifts.Count(s => s.CoverageOrigin == CoverageOrigin.SecondarySkill),
                workedSunday,
                workedSunday ? previousStreak + 1 : 0));
        }

        return summaries.AsReadOnly();
    }

    private static (int Honored, int Requested) EvaluateSoftPreferencesAndPolyvalence(
        Roster roster,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        List<ComplianceAlertDto> alerts)
    {
        foreach (var shift in roster.Shifts.Where(s => s.CoverageOrigin == CoverageOrigin.SecondarySkill))
        {
            alerts.Add(new ComplianceAlertDto(
                "Roster.PolyvalenceCoverageUsed",
                RosterAlertSeverity.Info,
                RosterAlertCategory.PolyvalenceUsage,
                $"Praça {shift.AssignedRole} coberta por polivalência em {shift.Date:yyyy-MM-dd}.",
                shift.Date,
                shift.AssignedRole,
                shift.EmployeeId));
        }

        var requested = 0;
        var honored = 0;

        foreach (var employee in employeeById.Values.Where(e => e.IsActive))
        {
            if (employee.PreferredDaysOff.Count == 0)
            {
                continue;
            }

            var workedDaysOfWeek = roster.Shifts
                .Where(s => s.EmployeeId == employee.Id)
                .Select(s => s.Date.DayOfWeek)
                .ToHashSet();

            foreach (var preferredDay in employee.PreferredDaysOff)
            {
                requested++;
                if (!workedDaysOfWeek.Contains(preferredDay))
                {
                    honored++;
                }
                else
                {
                    var conflictShift = roster.Shifts.First(s =>
                        s.EmployeeId == employee.Id &&
                        s.Date.DayOfWeek == preferredDay);

                    alerts.Add(new ComplianceAlertDto(
                        "Employee.PreferredDayOffOverridden",
                        RosterAlertSeverity.Warning,
                        RosterAlertCategory.SoftConstraint,
                        $"Preferência de folga de {employee.Name} em {preferredDay} não pôde ser atendida.",
                        conflictShift.Date,
                        conflictShift.AssignedRole,
                        employee.Id));
                }
            }
        }

        return (honored, requested);
    }
}

