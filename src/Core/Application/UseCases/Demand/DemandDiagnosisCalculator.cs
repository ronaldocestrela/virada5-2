using Application.DTOs.Demand;
using Domain.Entities;
using Domain.Enums;

namespace Application.UseCases.Demand;

internal static class DemandDiagnosisCalculator
{
    internal static Result<DemandDiagnosisDto> Calculate(
        Company company,
        IReadOnlyList<DemandSlot> slots,
        Action<DemandSlot>? onSlotEvaluated = null)
    {
        var openSlots = slots
            .Where(slot => company.IsOpenAt(slot.DayOfWeek, slot.Hour))
            .ToList();

        var operationalPeak = openSlots.Count > 0
            ? openSlots.Max(slot => slot.ExpectedVolume)
            : slots.Max(slot => slot.ExpectedVolume);

        foreach (var slot in slots)
        {
            var referencePeak = Math.Max(operationalPeak, slot.ExpectedVolume);
            var evalResult = slot.EvaluateRelativeDemand(referencePeak);
            if (evalResult.IsFailure)
            {
                return Result.Failure<DemandDiagnosisDto>(evalResult.Error);
            }

            onSlotEvaluated?.Invoke(slot);
        }

        var snapshots = DemandUseCaseHelpers.MapToOrderedSnapshots(slots, company);
        var openSnapshots = snapshots.Where(s => s.IsWithinOperatingHours).ToList();

        var peakSlots = openSnapshots
            .Where(s => s.Classification == DemandClassification.Peak)
            .OrderByDescending(s => s.ExpectedVolume)
            .ThenBy(s => s.DayOfWeek)
            .ThenBy(s => s.Hour)
            .ToList();

        var valleySlots = openSnapshots
            .Where(s => s.Classification is DemandClassification.Idle or DemandClassification.Low)
            .OrderBy(s => s.ExpectedVolume)
            .ThenBy(s => s.DayOfWeek)
            .ThenBy(s => s.Hour)
            .ToList();

        var globalPeakSlot = peakSlots.FirstOrDefault()
            ?? openSnapshots.OrderByDescending(s => s.ExpectedVolume).FirstOrDefault();

        var idlenessPercentage = openSnapshots.Count > 0
            ? Math.Round(valleySlots.Count * 100m / openSnapshots.Count, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var dailySummaries = BuildDailySummaries(company, openSnapshots);
        var recommendedTwoDaysOff = dailySummaries
            .OrderBy(d => d.IsOpenDay)
            .ThenBy(d => d.TotalExpectedVolume)
            .ThenBy(d => d.DayOfWeek)
            .Take(2)
            .Select(d => d.DayOfWeek)
            .ToList();

        return Result.Success(new DemandDiagnosisDto(
            company.Id,
            company.Name,
            operationalPeak,
            openSnapshots.Sum(s => s.ExpectedVolume),
            idlenessPercentage,
            globalPeakSlot,
            snapshots,
            peakSlots,
            valleySlots,
            dailySummaries,
            recommendedTwoDaysOff));
    }

    private static List<DailyDemandSummaryDto> BuildDailySummaries(
        Company company,
        IReadOnlyList<DemandSlotSnapshotDto> openSnapshots)
    {
        var summaries = new List<DailyDemandSummaryDto>(7);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var isOpenDay = company.IsOpenOn(day);
            var daySlots = openSnapshots.Where(s => s.DayOfWeek == day).ToList();
            var totalVolume = daySlots.Sum(s => s.ExpectedVolume);
            var averageVolume = daySlots.Count > 0
                ? Math.Round(totalVolume / daySlots.Count, 2, MidpointRounding.AwayFromZero)
                : 0m;

            summaries.Add(new DailyDemandSummaryDto(
                day,
                isOpenDay,
                daySlots.Count,
                daySlots.Count(s => s.Classification == DemandClassification.Peak),
                daySlots.Count(s => s.Classification is DemandClassification.Idle or DemandClassification.Low),
                totalVolume,
                averageVolume));
        }

        return summaries;
    }
}

