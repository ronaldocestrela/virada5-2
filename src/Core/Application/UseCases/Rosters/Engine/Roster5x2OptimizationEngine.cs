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

    private static readonly Comparison<(Employee Employee, int Score)> CandidateComparison =
        static (left, right) =>
        {
            var scoreCmp = right.Score.CompareTo(left.Score);
            if (scoreCmp != 0)
            {
                return scoreCmp;
            }

            var nameCmp = string.CompareOrdinal(left.Employee.Name, right.Employee.Name);
            return nameCmp != 0 ? nameCmp : left.Employee.Id.CompareTo(right.Employee.Id);
        };

    private sealed record ExpandedVacancy(
        RosterRequirementSlot Requirement,
        int EligibleCount);

    private sealed class RosterWorkIndex
    {
        private readonly Dictionary<Guid, List<Shift>> _shiftsByEmployee;
        private readonly Dictionary<Guid, int> _workedDaysByEmployee;
        private readonly HashSet<(Guid EmployeeId, DateOnly Date)> _workedDates;
        private readonly HashSet<Guid> _activeShiftIds;

        internal RosterWorkIndex(IReadOnlyList<Employee> activeEmployees, Roster roster)
        {
            _shiftsByEmployee = new Dictionary<Guid, List<Shift>>(activeEmployees.Count);
            _workedDaysByEmployee = new Dictionary<Guid, int>(activeEmployees.Count);
            _workedDates = new HashSet<(Guid, DateOnly)>(activeEmployees.Count * Roster.MaxWorkDaysPerWeek);
            _activeShiftIds = new HashSet<Guid>(activeEmployees.Count * Roster.MaxWorkDaysPerWeek);

            foreach (var employee in activeEmployees)
            {
                _shiftsByEmployee[employee.Id] = new List<Shift>(Roster.MaxWorkDaysPerWeek);
                _workedDaysByEmployee[employee.Id] = 0;
            }

            foreach (var shift in roster.Shifts)
            {
                TrackShiftAdded(shift);
            }
        }

        internal bool WorksOnDate(Guid employeeId, DateOnly date) =>
            _workedDates.Contains((employeeId, date));

        internal int GetWorkedDaysCount(Guid employeeId) =>
            _workedDaysByEmployee.TryGetValue(employeeId, out var count) ? count : 0;

        internal bool ContainsShift(Guid shiftId) =>
            _activeShiftIds.Contains(shiftId);

        internal IReadOnlyList<Shift> GetEmployeeShifts(Guid employeeId) =>
            _shiftsByEmployee.TryGetValue(employeeId, out var shifts) ? shifts : Array.Empty<Shift>();

        internal Result<Shift> TryAssignShift(
            Roster roster,
            Employee employee,
            DateOnly date,
            TimeOnly startTime,
            TimeOnly endTime,
            StationRole assignedRole,
            DemandClassification demandLevel,
            EmployeeWeekBoundaryContext? boundary)
        {
            var result = roster.AssignShift(
                employee,
                date,
                startTime,
                endTime,
                assignedRole,
                demandLevel,
                boundary);

            if (result.IsSuccess)
            {
                TrackShiftAdded(result.Value);
            }

            return result;
        }

        internal Result TryRemoveShift(Roster roster, Shift shift)
        {
            var result = roster.RemoveShift(shift.Id);
            if (result.IsSuccess)
            {
                TrackShiftRemoved(shift);
            }

            return result;
        }

        internal Result TrySwapShiftEmployees(
            Roster roster,
            Shift firstShift,
            Employee firstReplacement,
            Shift secondShift,
            Employee secondReplacement,
            DemandClassification firstShiftDemandLevel,
            DemandClassification secondShiftDemandLevel,
            EmployeeWeekBoundaryContext? firstReplacementBoundary,
            EmployeeWeekBoundaryContext? secondReplacementBoundary)
        {
            var previousFirstEmployeeId = firstShift.EmployeeId;
            var previousSecondEmployeeId = secondShift.EmployeeId;

            var result = roster.SwapShiftEmployees(
                firstShift.Id,
                firstReplacement,
                secondShift.Id,
                secondReplacement,
                firstShiftDemandLevel,
                secondShiftDemandLevel,
                firstReplacementBoundary,
                secondReplacementBoundary);

            if (result.IsSuccess)
            {
                TrackShiftRemovedForEmployee(firstShift, previousFirstEmployeeId);
                TrackShiftRemovedForEmployee(secondShift, previousSecondEmployeeId);
                TrackShiftAdded(firstShift);
                TrackShiftAdded(secondShift);
            }

            return result;
        }

        private void TrackShiftAdded(Shift shift)
        {
            if (!_shiftsByEmployee.TryGetValue(shift.EmployeeId, out var list))
            {
                list = new List<Shift>(Roster.MaxWorkDaysPerWeek);
                _shiftsByEmployee[shift.EmployeeId] = list;
            }

            list.Add(shift);
            _activeShiftIds.Add(shift.Id);

            if (_workedDates.Add((shift.EmployeeId, shift.Date)))
            {
                _workedDaysByEmployee[shift.EmployeeId] = GetWorkedDaysCount(shift.EmployeeId) + 1;
            }
        }

        private void TrackShiftRemoved(Shift shift) =>
            TrackShiftRemovedForEmployee(shift, shift.EmployeeId);

        private void TrackShiftRemovedForEmployee(Shift shift, Guid employeeId)
        {
            _activeShiftIds.Remove(shift.Id);

            if (!_shiftsByEmployee.TryGetValue(employeeId, out var list))
            {
                return;
            }

            list.Remove(shift);
            var stillWorksOnDate = false;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Date == shift.Date)
                {
                    stillWorksOnDate = true;
                    break;
                }
            }

            if (!stillWorksOnDate && _workedDates.Remove((employeeId, shift.Date)))
            {
                var current = GetWorkedDaysCount(employeeId);
                _workedDaysByEmployee[employeeId] = current > 0 ? current - 1 : 0;
            }
        }
    }

    internal static void Optimize(
        Roster roster,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        var employeeById = activeEmployees.ToDictionary(e => e.Id);
        var workIndex = new RosterWorkIndex(activeEmployees, roster);
        var requirementByShiftKey = BuildRequirementLookup(context.Requirements);
        var candidateBuffer = new List<(Employee Employee, int Score)>(activeEmployees.Count);

        var vacancies = BuildOrderedVacancies(activeEmployees, context);
        var uncoveredPeakVacancies = new List<RosterRequirementSlot>();

        // Fase B: Construção gulosa ponderada (Peak -> Moderate -> Low -> Idle, MRV)
        foreach (var vacancy in vacancies)
        {
            var req = vacancy.Requirement;
            var assigned = TryAssignBestCandidate(
                roster,
                workIndex,
                req,
                activeEmployees,
                context,
                boundaryContexts,
                candidateBuffer);

            if (!assigned && req.DemandLevel == DemandClassification.Peak)
            {
                uncoveredPeakVacancies.Add(req);
            }
        }

        // Fase C1: Reparo local para tentar atingir 100% de cobertura em Peak
        if (uncoveredPeakVacancies.Count > 0)
        {
            var repairEligibilityCache = new Dictionary<StationRole, List<Employee>>();

            foreach (var uncoveredPeak in uncoveredPeakVacancies)
            {
                TryRepairUncoveredPeak(
                    roster,
                    workIndex,
                    uncoveredPeak,
                    activeEmployees,
                    context,
                    boundaryContexts,
                    requirementByShiftKey,
                    repairEligibilityCache);
            }
        }

        // Fase C2: Refinamento de trocas para honrar PreferredDaysOff e alinhar folgas aos vales
        RefinePreferredDaysOffAndValleys(
            roster,
            workIndex,
            employeeById,
            activeEmployees,
            boundaryContexts,
            requirementByShiftKey);
    }

    private static Dictionary<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, StationRole Role), RosterRequirementSlot> BuildRequirementLookup(
        IReadOnlyList<RosterRequirementSlot> requirements)
    {
        var map = new Dictionary<(DateOnly, TimeOnly, TimeOnly, StationRole), RosterRequirementSlot>(requirements.Count);
        for (var i = 0; i < requirements.Count; i++)
        {
            var r = requirements[i];
            map[(r.Date, r.Template.StartTime, r.Template.EndTime, r.Role)] = r;
        }

        return map;
    }

    private static List<ExpandedVacancy> BuildOrderedVacancies(
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context)
    {
        var vacancies = new List<ExpandedVacancy>(context.Requirements.Count * 2);
        var eligibleCountCache = new Dictionary<(StationRole Role, DemandClassification Demand, DayOfWeek Day), int>();

        foreach (var req in context.Requirements)
        {
            var key = (req.Role, req.DemandLevel, req.DayOfWeek);
            if (!eligibleCountCache.TryGetValue(key, out var eligibleCount))
            {
                eligibleCount = 0;
                for (var i = 0; i < activeEmployees.Count; i++)
                {
                    if (activeEmployees[i].EvaluateCoverageEligibility(req.Role, req.DemandLevel, req.DayOfWeek).IsSuccess)
                    {
                        eligibleCount++;
                    }
                }

                eligibleCountCache[key] = eligibleCount;
            }

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
        RosterWorkIndex workIndex,
        RosterRequirementSlot req,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts,
        List<(Employee Employee, int Score)> candidateBuffer)
    {
        candidateBuffer.Clear();

        for (var i = 0; i < activeEmployees.Count; i++)
        {
            var emp = activeEmployees[i];
            var score = ScoreCandidateForRequirement(roster, workIndex, emp, req, context, boundaryContexts);
            if (score.HasValue)
            {
                candidateBuffer.Add((emp, score.Value));
            }
        }

        if (candidateBuffer.Count == 0)
        {
            return false;
        }

        candidateBuffer.Sort(CandidateComparison);

        for (var i = 0; i < candidateBuffer.Count; i++)
        {
            var candidate = candidateBuffer[i].Employee;
            boundaryContexts.TryGetValue(candidate.Id, out var boundary);
            var result = workIndex.TryAssignShift(
                roster,
                candidate,
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
        RosterWorkIndex workIndex,
        Employee employee,
        RosterRequirementSlot req,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts)
    {
        if (workIndex.WorksOnDate(employee.Id, req.Date))
        {
            return null;
        }

        var workedDays = workIndex.GetWorkedDaysCount(employee.Id);
        if (workedDays >= Roster.MaxWorkDaysPerWeek)
        {
            return null;
        }

        if (req.DayOfWeek == DayOfWeek.Sunday &&
            boundaryContexts.TryGetValue(employee.Id, out var boundary) &&
            !roster.SundayPolicy.CanWorkSunday(boundary.ConsecutiveSundaysWorkedBeforeWeek))
        {
            return null;
        }

        var eligibility = employee.EvaluateCoverageEligibility(req.Role, req.DemandLevel, req.DayOfWeek);
        if (eligibility.IsFailure)
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
            boundaryContexts.TryGetValue(employee.Id, out var sundayBoundary))
        {
            score += sundayBoundary.ConsecutiveSundaysWorkedBeforeWeek * SundayStreakPenalty;
        }

        return score;
    }

    private static int ResolveSecondarySkillScore(Employee employee, StationRole role)
    {
        foreach (var skill in employee.SecondarySkills)
        {
            if (skill.Role == role)
            {
                return skill.Proficiency == SkillProficiency.Autonomous
                    ? AutonomousSecondaryScore
                    : SupportSecondaryScore;
            }
        }

        return SupportSecondaryScore;
    }

    private static bool TryRepairUncoveredPeak(
        Roster roster,
        RosterWorkIndex workIndex,
        RosterRequirementSlot uncoveredPeak,
        IReadOnlyList<Employee> activeEmployees,
        RosterPlanningContext context,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts,
        IReadOnlyDictionary<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, StationRole Role), RosterRequirementSlot> requirementByShiftKey,
        Dictionary<StationRole, List<Employee>> repairEligibilityCache)
    {
        if (!repairEligibilityCache.TryGetValue(uncoveredPeak.Role, out var eligibleEmployees))
        {
            eligibleEmployees = activeEmployees
                .Where(e =>
                    e.IsActive &&
                    (e.PrimaryRole == uncoveredPeak.Role ||
                     e.SecondarySkills.Any(s =>
                         s.Role == uncoveredPeak.Role &&
                         s.Proficiency == SkillProficiency.Autonomous)))
                .OrderByDescending(e => e.PrimaryRole == uncoveredPeak.Role)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            repairEligibilityCache[uncoveredPeak.Role] = eligibleEmployees;
        }

        foreach (var candidate in eligibleEmployees)
        {
            if (candidate.EvaluateCoverageEligibility(
                uncoveredPeak.Role,
                DemandClassification.Peak,
                uncoveredPeak.DayOfWeek).IsFailure)
            {
                continue;
            }

            boundaryContexts.TryGetValue(candidate.Id, out var boundary);
            var candidateShifts = workIndex.GetEmployeeShifts(candidate.Id);
            if (candidateShifts.Count == 0)
            {
                continue;
            }

            var removableShifts = candidateShifts
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
                var removeResult = workIndex.TryRemoveShift(roster, oldShift);
                if (removeResult.IsFailure)
                {
                    continue;
                }

                var assignResult = workIndex.TryAssignShift(
                    roster,
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
                workIndex.TryAssignShift(
                    roster,
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
        RosterWorkIndex workIndex,
        IReadOnlyDictionary<Guid, Employee> employeeById,
        IReadOnlyList<Employee> activeEmployees,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts,
        IReadOnlyDictionary<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, StationRole Role), RosterRequirementSlot> requirementByShiftKey)
    {
        var penaltyShifts = roster.Shifts
            .Where(s =>
                employeeById.TryGetValue(s.EmployeeId, out var emp) &&
                emp.PrefersDayOff(s.Date.DayOfWeek))
            .OrderBy(s => s.Date)
            .ToList();

        if (penaltyShifts.Count == 0)
        {
            return;
        }

        foreach (var shift in penaltyShifts)
        {
            if (!workIndex.ContainsShift(shift.Id) ||
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
            Employee? replacement = null;
            var bestIsPrimary = false;
            var bestWorkedDays = int.MaxValue;

            for (var i = 0; i < activeEmployees.Count; i++)
            {
                var candidate = activeEmployees[i];
                if (candidate.Id == currentEmp.Id ||
                    candidate.PrefersDayOff(shift.Date.DayOfWeek) ||
                    workIndex.WorksOnDate(candidate.Id, shift.Date))
                {
                    continue;
                }

                var workedDays = workIndex.GetWorkedDaysCount(candidate.Id);
                if (workedDays >= Roster.MaxWorkDaysPerWeek ||
                    candidate.EvaluateCoverageEligibility(shift.AssignedRole, demandLevel, shift.Date.DayOfWeek).IsFailure)
                {
                    continue;
                }

                var isPrimary = candidate.PrimaryRole == shift.AssignedRole;
                if (replacement is null ||
                    (isPrimary && !bestIsPrimary) ||
                    (isPrimary == bestIsPrimary && workedDays < bestWorkedDays))
                {
                    replacement = candidate;
                    bestIsPrimary = isPrimary;
                    bestWorkedDays = workedDays;
                }
            }

            if (replacement is null)
            {
                continue;
            }

            boundaryContexts.TryGetValue(replacement.Id, out var replacementBoundary);
            boundaryContexts.TryGetValue(currentEmp.Id, out var currentBoundary);

            if (workIndex.TryRemoveShift(roster, shift).IsFailure)
            {
                continue;
            }

            var reassigned = workIndex.TryAssignShift(
                roster,
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
            workIndex.TryAssignShift(
                roster,
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
            if (!workIndex.ContainsShift(shift.Id) ||
                !employeeById.TryGetValue(shift.EmployeeId, out var currentEmp) ||
                !currentEmp.PrefersDayOff(shift.Date.DayOfWeek))
            {
                continue;
            }

            var demandLevel = requirementByShiftKey.TryGetValue(
                (shift.Date, shift.StartTime, shift.EndTime, shift.AssignedRole),
                out var req)
                ? req.DemandLevel
                : DemandClassification.Moderate;

            Shift? candidateCounterpart = null;
            foreach (var other in roster.Shifts)
            {
                if (other.Id == shift.Id ||
                    other.Date == shift.Date ||
                    other.AssignedRole != shift.AssignedRole ||
                    !employeeById.TryGetValue(other.EmployeeId, out var otherEmp) ||
                    otherEmp.PrefersDayOff(shift.Date.DayOfWeek) ||
                    currentEmp.PrefersDayOff(other.Date.DayOfWeek) ||
                    workIndex.WorksOnDate(otherEmp.Id, shift.Date) ||
                    workIndex.WorksOnDate(currentEmp.Id, other.Date))
                {
                    continue;
                }

                candidateCounterpart = other;
                break;
            }

            if (candidateCounterpart is null)
            {
                continue;
            }

            var counterpartDemandLevel = requirementByShiftKey.TryGetValue(
                (candidateCounterpart.Date, candidateCounterpart.StartTime, candidateCounterpart.EndTime, candidateCounterpart.AssignedRole),
                out var counterpartReq)
                ? counterpartReq.DemandLevel
                : DemandClassification.Moderate;

            var counterpartEmp = employeeById[candidateCounterpart.EmployeeId];
            boundaryContexts.TryGetValue(counterpartEmp.Id, out var counterpartBoundary);
            boundaryContexts.TryGetValue(currentEmp.Id, out var currentBoundary);

            workIndex.TrySwapShiftEmployees(
                roster,
                shift,
                counterpartEmp,
                candidateCounterpart,
                currentEmp,
                demandLevel,
                counterpartDemandLevel,
                counterpartBoundary,
                currentBoundary);
        }
    }
}
