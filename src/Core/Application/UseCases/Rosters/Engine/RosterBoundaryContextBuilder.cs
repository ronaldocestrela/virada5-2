using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters.Engine;

internal static class RosterBoundaryContextBuilder
{
    internal const int LookbackDays = 28;
    internal const int LookaheadDays = 13;

    internal static Result<IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext>> Build(
        DateOnly targetWeekStart,
        IReadOnlyList<Roster> adjacentRosters,
        IEnumerable<Guid> employeeIds,
        Guid? ignoredRosterId = null)
    {
        var activeRostersByWeek = adjacentRosters
            .Where(r =>
                r.Status != RosterStatus.Archived &&
                (!ignoredRosterId.HasValue || r.Id != ignoredRosterId.Value))
            .GroupBy(r => r.WeekStartDate)
            .ToDictionary(g => g.Key, g => g.First());

        var previousWeekStart = targetWeekStart.AddDays(-Roster.CycleLengthDays);
        var nextWeekStart = targetWeekStart.AddDays(Roster.CycleLengthDays);

        activeRostersByWeek.TryGetValue(previousWeekStart, out var previousRoster);
        activeRostersByWeek.TryGetValue(nextWeekStart, out var nextRoster);

        var contexts = new Dictionary<Guid, EmployeeWeekBoundaryContext>();

        foreach (var employeeId in employeeIds.Distinct())
        {
            if (employeeId == Guid.Empty)
            {
                continue;
            }

            DateTime? previousWeekLastShiftEnd = previousRoster?
                .Shifts
                .Where(s => s.EmployeeId == employeeId)
                .Select(s => (DateTime?)s.EndDateTime)
                .Max();

            DateTime? nextWeekFirstShiftStart = nextRoster?
                .Shifts
                .Where(s => s.EmployeeId == employeeId)
                .Select(s => (DateTime?)s.StartDateTime)
                .Min();

            var consecutiveSundaysBefore = CountConsecutiveSundaysBefore(
                previousWeekStart,
                activeRostersByWeek,
                employeeId);

            var contextResult = EmployeeWeekBoundaryContext.Create(
                employeeId,
                consecutiveSundaysBefore,
                previousWeekLastShiftEnd,
                nextWeekFirstShiftStart);

            if (contextResult.IsFailure)
            {
                return Result.Failure<IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext>>(contextResult.Error);
            }

            contexts[employeeId] = contextResult.Value;
        }

        return Result.Success<IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext>>(contexts);
    }

    private static int CountConsecutiveSundaysBefore(
        DateOnly cursorWeekStart,
        IReadOnlyDictionary<DateOnly, Roster> rostersByWeek,
        Guid employeeId)
    {
        var streak = 0;
        var currentWeek = cursorWeekStart;

        while (rostersByWeek.TryGetValue(currentWeek, out var roster))
        {
            var workedSunday = roster.Shifts.Any(s =>
                s.EmployeeId == employeeId &&
                s.Date.DayOfWeek == DayOfWeek.Sunday);

            if (!workedSunday)
            {
                break;
            }

            streak++;
            currentWeek = currentWeek.AddDays(-Roster.CycleLengthDays);
        }

        return streak;
    }
}

