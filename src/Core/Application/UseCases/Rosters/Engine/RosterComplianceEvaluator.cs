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
        var demandLookup = BuildDemandLookup(context);
        var shiftsByEmployee = GroupShiftsByEmployee(roster.Shifts);

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
            shiftsByEmployee,
            context,
            demandLookup,
            boundaryContexts,
            alerts);

        var coverageDetails = EvaluateCoverage(roster, context, alerts);
        var employeeSummaries = BuildEmployeeSummaries(roster, employees, shiftsByEmployee, boundaryContexts);

        var totalPeakRequired = 0;
        var totalPeakCovered = 0;
        var totalRequired = 0;
        var totalCovered = 0;

        for (var i = 0; i < coverageDetails.Count; i++)
        {
            var detail = coverageDetails[i];
            var covered = Math.Min(detail.RequiredCount, detail.AssignedPrimaryCount + detail.AssignedSecondaryCount);
            totalRequired += detail.RequiredCount;
            totalCovered += covered;

            if (detail.DemandLevel == DemandClassification.Peak)
            {
                totalPeakRequired += detail.RequiredCount;
                totalPeakCovered += covered;
            }
        }

        var peakCoveragePct = totalPeakRequired > 0
            ? Math.Round(totalPeakCovered * 100m / totalPeakRequired, 2, MidpointRounding.AwayFromZero)
            : 100m;

        var overallCoveragePct = totalRequired > 0
            ? Math.Round(totalCovered * 100m / totalRequired, 2, MidpointRounding.AwayFromZero)
            : 100m;

        var preferredStats = EvaluateSoftPreferencesAndPolyvalence(
            roster,
            employeeById,
            shiftsByEmployee,
            alerts);

        var primaryShiftCount = 0;
        var secondaryShiftCount = 0;
        foreach (var shift in roster.Shifts)
        {
            if (shift.CoverageOrigin == CoverageOrigin.PrimaryRole)
            {
                primaryShiftCount++;
            }
            else if (shift.CoverageOrigin == CoverageOrigin.SecondarySkill)
            {
                secondaryShiftCount++;
            }
        }

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
            primaryShiftCount,
            secondaryShiftCount,
            preferredStats.Honored,
            preferredStats.Requested,
            coverageDetails,
            employeeSummaries,
            alerts.AsReadOnly());
    }

    private sealed record DemandLookup(
        Dictionary<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, StationRole Role), DemandClassification> BySlotKey,
        Dictionary<DayOfWeek, DemandClassification> MaxByDayOfWeek);

    private static DemandLookup BuildDemandLookup(RosterPlanningContext context)
    {
        var bySlotKey = new Dictionary<(DateOnly, TimeOnly, TimeOnly, StationRole), DemandClassification>(context.Requirements.Count);
        for (var i = 0; i < context.Requirements.Count; i++)
        {
            var req = context.Requirements[i];
            bySlotKey.TryAdd((req.Date, req.Template.StartTime, req.Template.EndTime, req.Role), req.DemandLevel);
        }

        var maxByDay = new Dictionary<DayOfWeek, DemandClassification>(7);
        foreach (var kvp in context.DemandMatrix)
        {
            var day = kvp.Key.Day;
            var classification = kvp.Value.Classification;
            if (!maxByDay.TryGetValue(day, out var existing) || classification > existing)
            {
                maxByDay[day] = classification;
            }
        }

        return new DemandLookup(bySlotKey, maxByDay);
    }

    private static Dictionary<Guid, List<Shift>> GroupShiftsByEmployee(IReadOnlyCollection<Shift> shifts)
    {
        var map = new Dictionary<Guid, List<Shift>>();
        foreach (var shift in shifts)
        {
            if (!map.TryGetValue(shift.EmployeeId, out var list))
            {
                list = new List<Shift>(Roster.MaxWorkDaysPerWeek);
                map[shift.EmployeeId] = list;
            }

            list.Add(shift);
        }

        return map;
    }

    private static bool ValidateCltAndOperationalConsistency(
        Roster roster,
        Company company,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        Dictionary<Guid, List<Shift>> shiftsByEmployee,
        RosterPlanningContext context,
        DemandLookup demandLookup,
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

            var demandLevel = ResolveShiftDemandLevel(shift, context, demandLookup);
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

        foreach (var (employeeId, rawShifts) in shiftsByEmployee)
        {
            rawShifts.Sort(static (a, b) => a.StartDateTime.CompareTo(b.StartDateTime));
            boundaryContexts.TryGetValue(employeeId, out var boundary);

            var workedDays = CountDistinctDates(rawShifts);
            if (workedDays > Roster.MaxWorkDaysPerWeek)
            {
                isCltCompliant = false;
                alerts.Add(new ComplianceAlertDto(
                    "Roster.WeeklyRestDaysViolated",
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    $"Colaborador excedeu o máximo de {Roster.MaxWorkDaysPerWeek} dias trabalhados na escala 5x2.",
                    EmployeeId: employeeId));
            }

            for (var i = 0; i < rawShifts.Count; i++)
            {
                var current = rawShifts[i];

                if (i > 0)
                {
                    var previous = rawShifts[i - 1];
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
                            employeeId));
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
                            employeeId));
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
                        employeeId));
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
                        employeeId));
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
                        employeeId));
                }
            }
        }

        return isCltCompliant;
    }

    private static DemandClassification ResolveShiftDemandLevel(
        Shift shift,
        RosterPlanningContext context,
        DemandLookup demandLookup)
    {
        if (demandLookup.BySlotKey.TryGetValue(
            (shift.Date, shift.StartTime, shift.EndTime, shift.AssignedRole),
            out var exactDemand))
        {
            return exactDemand;
        }

        if (!context.HasDemandMatrix)
        {
            return DemandClassification.Moderate;
        }

        return demandLookup.MaxByDayOfWeek.TryGetValue(shift.Date.DayOfWeek, out var dayMax)
            ? dayMax
            : DemandClassification.Moderate;
    }

    private static IReadOnlyList<ShiftRequirementCoverageDto> EvaluateCoverage(
        Roster roster,
        RosterPlanningContext context,
        List<ComplianceAlertDto> alerts)
    {
        var countsBySlotKey = new Dictionary<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, StationRole Role), (int Primary, int Secondary)>(context.Requirements.Count);
        foreach (var shift in roster.Shifts)
        {
            var key = (shift.Date, shift.StartTime, shift.EndTime, shift.AssignedRole);
            countsBySlotKey.TryGetValue(key, out var counts);
            if (shift.CoverageOrigin == CoverageOrigin.PrimaryRole)
            {
                counts.Primary++;
            }
            else if (shift.CoverageOrigin == CoverageOrigin.SecondarySkill)
            {
                counts.Secondary++;
            }

            countsBySlotKey[key] = counts;
        }

        var details = new List<ShiftRequirementCoverageDto>(context.Requirements.Count);

        foreach (var req in context.Requirements)
        {
            countsBySlotKey.TryGetValue(
                (req.Date, req.Template.StartTime, req.Template.EndTime, req.Role),
                out var counts);

            var primaryCount = counts.Primary;
            var secondaryCount = counts.Secondary;
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
        Dictionary<Guid, List<Shift>> shiftsByEmployee,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var summaries = new List<EmployeeRosterSummaryDto>(employees.Count);

        foreach (var employee in employees.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            shiftsByEmployee.TryGetValue(employee.Id, out var employeeShifts);
            var workedDaysCount = employeeShifts is null ? 0 : CountDistinctDates(employeeShifts);
            var daysOff = BuildEmployeeDaysOff(roster.WeekStartDate, employeeShifts);

            var workedSunday = false;
            var primaryCount = 0;
            var secondaryCount = 0;

            if (employeeShifts is not null)
            {
                for (var i = 0; i < employeeShifts.Count; i++)
                {
                    var shift = employeeShifts[i];
                    if (shift.Date.DayOfWeek == DayOfWeek.Sunday)
                    {
                        workedSunday = true;
                    }

                    if (shift.CoverageOrigin == CoverageOrigin.PrimaryRole)
                    {
                        primaryCount++;
                    }
                    else if (shift.CoverageOrigin == CoverageOrigin.SecondarySkill)
                    {
                        secondaryCount++;
                    }
                }
            }

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
                primaryCount,
                secondaryCount,
                workedSunday,
                workedSunday ? previousStreak + 1 : 0));
        }

        return summaries.AsReadOnly();
    }

    private static (int Honored, int Requested) EvaluateSoftPreferencesAndPolyvalence(
        Roster roster,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        Dictionary<Guid, List<Shift>> shiftsByEmployee,
        List<ComplianceAlertDto> alerts)
    {
        foreach (var shift in roster.Shifts)
        {
            if (shift.CoverageOrigin == CoverageOrigin.SecondarySkill)
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
        }

        var requested = 0;
        var honored = 0;

        foreach (var employee in employeeById.Values)
        {
            if (!employee.IsActive || employee.PreferredDaysOff.Count == 0)
            {
                continue;
            }

            shiftsByEmployee.TryGetValue(employee.Id, out var employeeShifts);

            foreach (var preferredDay in employee.PreferredDaysOff)
            {
                requested++;
                Shift? conflictShift = null;

                if (employeeShifts is not null)
                {
                    for (var i = 0; i < employeeShifts.Count; i++)
                    {
                        if (employeeShifts[i].Date.DayOfWeek == preferredDay)
                        {
                            conflictShift = employeeShifts[i];
                            break;
                        }
                    }
                }

                if (conflictShift is null)
                {
                    honored++;
                }
                else
                {
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

    private static int CountDistinctDates(List<Shift> shifts)
    {
        if (shifts.Count <= 1)
        {
            return shifts.Count;
        }

        var distinct = 0;
        for (var i = 0; i < shifts.Count; i++)
        {
            var date = shifts[i].Date;
            var seen = false;
            for (var j = 0; j < i; j++)
            {
                if (shifts[j].Date == date)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                distinct++;
            }
        }

        return distinct;
    }

    private static IReadOnlyList<DateOnly> BuildEmployeeDaysOff(
        DateOnly weekStartDate,
        List<Shift>? employeeShifts)
    {
        var daysOff = new List<DateOnly>(Roster.CycleLengthDays);

        for (var offset = 0; offset < Roster.CycleLengthDays; offset++)
        {
            var currentDate = weekStartDate.AddDays(offset);
            var worked = false;

            if (employeeShifts is not null)
            {
                for (var i = 0; i < employeeShifts.Count; i++)
                {
                    if (employeeShifts[i].Date == currentDate)
                    {
                        worked = true;
                        break;
                    }
                }
            }

            if (!worked)
            {
                daysOff.Add(currentDate);
            }
        }

        return daysOff.AsReadOnly();
    }
}
