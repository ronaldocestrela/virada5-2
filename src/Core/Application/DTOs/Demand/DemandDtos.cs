using Application.DTOs.Companies;
using Domain.Enums;

namespace Application.DTOs.Demand;

public enum DemandImportSource
{
    Csv = 1,
    Xlsx = 2,
    ManualMatrix = 3
}

public sealed record ManualDemandEntryDto(
    DayOfWeek DayOfWeek,
    int Hour,
    decimal ExpectedVolume);

public sealed record DemandSlotSnapshotDto(
    Guid Id,
    DayOfWeek DayOfWeek,
    int Hour,
    decimal ExpectedVolume,
    decimal RelativeVolume,
    DemandClassification Classification,
    bool IsWithinOperatingHours);

public sealed record ParseSalesHistoryResultDto(
    Guid CompanyId,
    DemandImportSource Source,
    int RawRowsProcessed,
    int ImportedSlotsCount,
    decimal TotalExpectedWeeklyVolume,
    IReadOnlyList<DemandSlotSnapshotDto> Slots,
    IReadOnlyList<string> Warnings);

public sealed record DailyDemandSummaryDto(
    DayOfWeek DayOfWeek,
    bool IsOpenDay,
    int OpenHoursCount,
    int PeakHoursCount,
    int IdleOrLowHoursCount,
    decimal TotalExpectedVolume,
    decimal AverageExpectedVolume);

public sealed record DemandDiagnosisDto(
    Guid CompanyId,
    string CompanyName,
    decimal PeakVolume,
    decimal TotalWeeklyVolume,
    decimal IdlenessPercentage,
    DemandSlotSnapshotDto? GlobalPeakSlot,
    IReadOnlyList<DemandSlotSnapshotDto> Slots,
    IReadOnlyList<DemandSlotSnapshotDto> PeakSlots,
    IReadOnlyList<DemandSlotSnapshotDto> ValleySlots,
    IReadOnlyList<DailyDemandSummaryDto> DailySummaries,
    IReadOnlyList<DayOfWeek> RecommendedTwoDaysOff);

public sealed record DemandOperatingSlotDto(
    DayOfWeek DayOfWeek,
    int Hour,
    bool IsOpen);

public sealed record DemandWorkspaceDto(
    CompanySettingsDto Company,
    IReadOnlyList<int> OperatingHours,
    IReadOnlyList<DemandOperatingSlotDto> OperatingSlots,
    DemandDiagnosisDto? Diagnosis);

