using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters.Engine;

internal static class Roster5x2OptimizationEngine
{
    private const int PrimaryRoleScore = 1000;
    private const int AutonomousSecondaryScore = 260;
    private const int SupportSecondaryScore = 150;
    private const int ValleyDayPenalty = -350;
    private const int PreferredDayOffPenalty = -450;
    private const int WorkedDayBalancePenalty = -80;
    private const int SundayStreakPenalty = -120;

    private sealed record ExpandedVacancy(
        RosterRequirementSlot Requirement,
        int EligibleCount);

    internal static void Optimize(
        Roster roster,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var employeeById = activeEmployees.ToDictionary(e => e.Id);
        var vacancies = BuildOrderedVacancies(activeEmployees, context);
        var uncoveredPeakVacancies = new List<RosterRequirementSlot>();

        // Fase B: Construção gulosa ponderada (Peak -> Moderate -> Low -> Idle, MRV)
        foreach (var vacancy in vacancies)
        {
            var req = vacancy.Requirement;
            var assigned = TryAssignBestCandidate(
                roster,
                req,
                activeEmployees,
                context,
                boundaryContexts);

            if (!assigned && req.DemandLevel == DemandClassification.Peak)
            {
                uncoveredPeakVacancies.Add(req);
            }
        }

        // Fase C1: Reparo local para tentar atingir 100% de cobertura em Peak
        foreach (var uncoveredPeak in uncoveredPeakVacancies)
        {
            TryRepairUncoveredPeak(
                roster,
                uncoveredPeak,
                activeEmployees,
                context,
                boundaryContexts);
        }

        // Fase C2: Refinamento de trocas para honrar PreferredDaysOff e alinhar folgas aos vales
        RefinePreferredDaysOffAndValleys(
            roster,
            employeeById,
            activeEmployees,
            context,
            boundaryContexts);
    }

    private static List<ExpandedVacancy> BuildOrderedVacancies(
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context)
    {
        var vacancies = new List<ExpandedVacancy>();

        foreach (var req in context.Requirements)
        {
            var eligibleCount = activeEmployees.Count(e =>
                e.EvaluateCoverageEligibility(req.Role, req.DemandLevel, req.DayOfWeek).IsSuccess);

            for (var i = 0; i < req.RequiredCount; i++)
            {
                vacancies.Add(new ExpandedVacancy(req, eligibleCount));
            }
        }

        return vacancies
            .OrderByDescending(v => v.Requirement.DemandLevel)
            .ThenBy(v => v.EligibleCount)
            .ThenByDescending(v => v.Requirement.ExpectedVolumeScore)
            .ThenBy(v => v.Requirement.Date)
            .ThenBy(v => v.Requirement.Template.StartTime)
            .ThenBy(v => v.Requirement.Role)
            .ToList();
    }

    private static bool TryAssignBestCandidate(
        Roster roster,
        RosterRequirementSlot req,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var scoredCandidates = activeEmployees
            .Select(emp => new
            {
                Employee = emp,
                Score = ScoreCandidateForRequirement(roster, emp, req, context, boundaryContexts)
            })
            .Where(x => x.Score.HasValue)
            .OrderByDescending(x => x.Score!.Value)
            .ThenBy(x => x.Employee.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Employee.Id)
            .ToList();

        foreach (var candidate in scoredCandidates)
        {
            boundaryContexts.TryGetValue(candidate.Employee.Id, out var boundary);
            var result = roster.AssignShift(
                candidate.Employee,
                req.Date,
                req.Template.StartTime,
                req.Template.EndTime,
                req.Role,
                req.DemandLevel,
                boundary);

            if (result.IsSuccess)
            {
                return true;
            }
        }

        return false;
    }

    private static int? ScoreCandidateForRequirement(
        Roster roster,
        Employee employee,
        RosterRequirementSlot req,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var eligibility = employee.EvaluateCoverageEligibility(req.Role, req.DemandLevel, req.DayOfWeek);
        if (eligibility.IsFailure)
        {
            return null;
        }

        if (roster.Shifts.Any(s => s.EmployeeId == employee.Id && s.Date == req.Date))
        {
            return null;
        }

        var workedDays = roster.GetEmployeeWorkedDaysCount(employee.Id);
        if (workedDays >= Roster.MaxWorkDaysPerWeek)
        {
            return null;
        }

        var score = eligibility.Value == CoverageOrigin.PrimaryRole
            ? PrimaryRoleScore
            : ResolveSecondarySkillScore(employee, req.Role);

        if (context.ValleyDays.Contains(req.DayOfWeek))
        {
            score += ValleyDayPenalty;
        }

        if (employee.PrefersDayOff(req.DayOfWeek))
        {
            score += PreferredDayOffPenalty;
        }

        score += workedDays * WorkedDayBalancePenalty;

        if (req.DayOfWeek == DayOfWeek.Sunday &&
            boundaryContexts.TryGetValue(employee.Id, out var boundary))
        {
            if (!roster.SundayPolicy.CanWorkSunday(boundary.ConsecutiveSundaysWorkedBeforeWeek))
            {
                return null;
            }

            score += boundary.ConsecutiveSundaysWorkedBeforeWeek * SundayStreakPenalty;
        }

        return score;
    }

    private static int ResolveSecondarySkillScore(Employee employee, StationRole role)
    {
        var skill = employee.SecondarySkills.FirstOrDefault(s => s.Role == role);
        return skill.Proficiency == SkillProficiency.Autonomous
            ? AutonomousSecondaryScore
            : SupportSecondaryScore;
    }

    private static bool TryRepairUncoveredPeak(
        Roster roster,
        RosterRequirementSlot uncoveredPeak,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var requirementByShiftKey = context.Requirements.ToDictionary(
            r => (r.Date, r.Template.StartTime, r.Template.EndTime, r.Role),
            r => r);

        var eligibleEmployees = activeEmployees
            .Where(e => e.EvaluateCoverageEligibility(
                uncoveredPeak.Role,
                DemandClassification.Peak,
                uncoveredPeak.DayOfWeek).IsSuccess)
            .OrderByDescending(e => e.PrimaryRole == uncoveredPeak.Role)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in eligibleEmployees)
        {
            boundaryContexts.TryGetValue(candidate.Id, out var boundary);

            var removableShifts = roster.Shifts
                .Where(s => s.EmployeeId == candidate.Id)
                .Select(s => new
                {
                    Shift = s,
                    DemandLevel = requirementByShiftKey.TryGetValue(
                        (s.Date, s.StartTime, s.EndTime, s.AssignedRole),
                        out var matchedReq)
                        ? matchedReq.DemandLevel
                        : DemandClassification.Moderate
                })
                .Where(x => x.DemandLevel < DemandClassification.Peak)
                .OrderBy(x => x.DemandLevel)
                .ThenBy(x => context.DailyDemandTotals.GetValueOrDefault(x.Shift.Date.DayOfWeek, 0m))
                .ToList();

            foreach (var removable in removableShifts)
            {
                var oldShift = removable.Shift;
                var removeResult = roster.RemoveShift(oldShift.Id);
                if (removeResult.IsFailure)
                {
                    continue;
                }

                var assignResult = roster.AssignShift(
                    candidate,
                    uncoveredPeak.Date,
                    uncoveredPeak.Template.StartTime,
                    uncoveredPeak.Template.EndTime,
                    uncoveredPeak.Role,
                    DemandClassification.Peak,
                    boundary);

                if (assignResult.IsSuccess)
                {
                    return true;
                }

                // Rollback determinístico caso falhe por 11h de interjornada ou política dominical
                roster.AssignShift(
                    candidate,
                    oldShift.Date,
                    oldShift.StartTime,
                    oldShift.EndTime,
                    oldShift.AssignedRole,
                    removable.DemandLevel,
                    boundary);
            }
        }

        return false;
    }

    private static void RefinePreferredDaysOffAndValleys(
        Roster roster,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var requirementByShiftKey = context.Requirements.ToDictionary(
            r => (r.Date, r.Template.StartTime, r.Template.EndTime, r.Role),
            r => r);

        var penaltyShifts = roster.Shifts
            .Where(s =>
                employeeById.TryGetValue(s.EmployeeId, out var emp) &&
                emp.PrefersDayOff(s.Date.DayOfWeek))
            .OrderBy(s => s.Date)
            .ToList();

        foreach (var shift in penaltyShifts)
        {
            if (!roster.Shifts.Any(s => s.Id == shift.Id) ||
                !employeeById.TryGetValue(shift.EmployeeId, out var currentEmp))
            {
                continue;
            }

            var demandLevel = requirementByShiftKey.TryGetValue(
                (shift.Date, shift.StartTime, shift.EndTime, shift.AssignedRole),
                out var req)
                ? req.DemandLevel
                : DemandClassification.Moderate;

            // Tenta primeiro mover o turno para um colaborador livre que não tenha preferência de folga nesse dia
            var replacement = activeEmployees
                .Where(e =>
                    e.Id != currentEmp.Id &&
                    !e.PrefersDayOff(shift.Date.DayOfWeek) &&
                    roster.GetEmployeeWorkedDaysCount(e.Id) < Roster.MaxWorkDaysPerWeek &&
                    e.EvaluateCoverageEligibility(shift.AssignedRole, demandLevel, shift.Date.DayOfWeek).IsSuccess)
                .OrderByDescending(e => e.PrimaryRole == shift.AssignedRole)
                .ThenBy(e => roster.GetEmployeeWorkedDaysCount(e.Id))
                .FirstOrDefault();

            if (replacement is null)
            {
                continue;
            }

            boundaryContexts.TryGetValue(replacement.Id, out var replacementBoundary);
            boundaryContexts.TryGetValue(currentEmp.Id, out var currentBoundary);

            if (roster.RemoveShift(shift.Id).IsFailure)
            {
                continue;
            }

            var reassigned = roster.AssignShift(
                replacement,
                shift.Date,
                shift.StartTime,
                shift.EndTime,
                shift.AssignedRole,
                demandLevel,
                replacementBoundary);

            if (reassigned.IsSuccess)
            {
                continue;
            }

            // Rollback caso o colaborador livre falhe por interjornada de 11h ou limite dominical
            roster.AssignShift(
                currentEmp,
                shift.Date,
                shift.StartTime,
                shift.EndTime,
                shift.AssignedRole,
                demandLevel,
                currentBoundary);
        }

        // Caso não haja colaborador abaixo de 5 dias, tenta troca 1-para-1 atômica via domínio
        foreach (var shift in penaltyShifts)
        {
            var liveShift = roster.Shifts.FirstOrDefault(s => s.Id == shift.Id);
            if (liveShift is null ||
                !employeeById.TryGetValue(liveShift.EmployeeId, out var currentEmp) ||
                !currentEmp.PrefersDayOff(liveShift.Date.DayOfWeek))
            {
                continue;
            }

            var demandLevel = requirementByShiftKey.TryGetValue(
                (liveShift.Date, liveShift.StartTime, liveShift.EndTime, liveShift.AssignedRole),
                out var req)
                ? req.DemandLevel
                : DemandClassification.Moderate;

            var candidateCounterpart = roster.Shifts
                .Where(other =>
                    other.Id != liveShift.Id &&
                    other.Date != liveShift.Date &&
                    other.AssignedRole == liveShift.AssignedRole &&
                    employeeById.TryGetValue(other.EmployeeId, out var otherEmp) &&
                    !otherEmp.PrefersDayOff(liveShift.Date.DayOfWeek) &&
                    !currentEmp.PrefersDayOff(other.Date.DayOfWeek) &&
                    !roster.Shifts.Any(s => s.EmployeeId == otherEmp.Id && s.Date == liveShift.Date) &&
                    !roster.Shifts.Any(s => s.EmployeeId == currentEmp.Id && s.Date == other.Date))
                .FirstOrDefault();

            if (candidateCounterpart is null)
            {
                continue;
            }

            var counterpartEmp = employeeById[candidateCounterpart.EmployeeId];
            boundaryContexts.TryGetValue(counterpartEmp.Id, out var counterpartBoundary);
            boundaryContexts.TryGetValue(currentEmp.Id, out var currentBoundary);

            roster.SwapShiftEmployees(
                liveShift.Id,
                counterpartEmp,
                candidateCounterpart.Id,
                currentEmp,
                demandLevel,
                counterpartBoundary,
                currentBoundary);
        }
    }
}
