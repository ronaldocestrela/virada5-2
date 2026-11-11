using Domain.Enums;

namespace Application.DTOs.Rosters;

public enum RosterAlertSeverity
{
    Info = 1,
    Warning = 2,
    Error = 3
}

public enum RosterAlertCategory
{
    CltHardConstraint = 1,
    PeakCoverage = 2,
    StationStaffing = 3,
    SoftConstraint = 4,
    PolyvalenceUsage = 5,
    BoundaryContext = 6
}

public enum ShiftCoverageStatus
{
    FullyCovered = 1,
    PartiallyCovered = 2,
    Uncovered = 3
}

public sealed record ShiftTemplateInputDto(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record StationStaffingRuleDto(
    StationRole Role,
    int PeakHeadcount,
    int ModerateHeadcount,
    int LowOrIdleHeadcount);

public sealed record ComplianceAlertDto(
    string Code,
    RosterAlertSeverity Severity,
    RosterAlertCategory Category,
    string Message,
    DateOnly? Date = null,
    StationRole? Role = null,
    Guid? EmployeeId = null);

public sealed record ShiftRequirementCoverageDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    string TemplateName,
    TimeOnly StartTime,
    TimeOnly EndTime,
    StationRole Role,
    DemandClassification DemandLevel,
    int RequiredCount,
    int AssignedPrimaryCount,
    int AssignedSecondaryCount,
    ShiftCoverageStatus Status);

public sealed record EmployeeRosterSummaryDto(
    Guid EmployeeId,
    string EmployeeName,
    StationRole PrimaryRole,
    int WorkedDaysCount,
    int DaysOffCount,
    IReadOnlyList<DateOnly> DaysOff,
    int PrimaryRoleShiftsCount,
    int SecondarySkillShiftsCount,
    bool WorkedSunday,
    int ConsecutiveSundaysAfterWeek);

public sealed record RosterComplianceReportDto(
    Guid RosterId,
    Guid CompanyId,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    RosterStatus Status,
    bool IsCltCompliant,
    decimal PeakCoveragePercentage,
    decimal OverallCoveragePercentage,
    int TotalShifts,
    int PrimaryCoverageShifts,
    int SecondaryCoverageShifts,
    int PreferredDaysOffHonoredCount,
    int PreferredDaysOffRequestedCount,
    IReadOnlyList<ShiftRequirementCoverageDto> CoverageDetails,
    IReadOnlyList<EmployeeRosterSummaryDto> EmployeeSummaries,
    IReadOnlyList<ComplianceAlertDto> Alerts);

public sealed record GeneratedShiftDto(
    Guid ShiftId,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    StationRole AssignedRole,
    CoverageOrigin CoverageOrigin);

public sealed record GeneratedRosterResultDto(
    Guid RosterId,
    Guid CompanyId,
    string CompanyName,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    RosterStatus Status,
    bool ReplacedPreviousDraft,
    bool UsedInferredShiftTemplates,
    bool UsedInferredStaffingRules,
    RosterComplianceReportDto ComplianceReport,
    IReadOnlyList<GeneratedShiftDto> Shifts);

public sealed record RosterEmployeeSecondarySkillDto(
    StationRole Role,
    SkillProficiency Proficiency);

public sealed record RosterEmployeeOptionDto(
    Guid EmployeeId,
    string Name,
    StationRole PrimaryRole,
    IReadOnlyList<RosterEmployeeSecondarySkillDto> SecondarySkills,
    IReadOnlyList<DayOfWeek> UnavailableDays,
    IReadOnlyList<DayOfWeek> PreferredDaysOff);

public sealed record RosterWeekSummaryDto(
    Guid RosterId,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    RosterStatus Status,
    bool IsCltCompliant,
    decimal PeakCoveragePercentage);

public sealed record RosterWorkspaceDto(
    Guid CompanyId,
    string CompanyName,
    DateOnly SelectedWeekStartDate,
    DateOnly SelectedWeekEndDate,
    IReadOnlyList<DayOfWeek> ClosedDays,
    GeneratedRosterResultDto? ActiveRoster,
    IReadOnlyList<RosterEmployeeOptionDto> ActiveEmployees,
    IReadOnlyList<ShiftTemplateInputDto> AvailableShiftTemplates,
    IReadOnlyList<RosterWeekSummaryDto> NearbyRosters);

public sealed record RosterCsvExportDto(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record RosterShareLinkDto(
    Guid RosterId,
    string Token,
    string ShareUrl,
    DateTime ExpiresAtUtc);

