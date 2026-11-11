using Domain.Enums;

namespace Application.DTOs.Rosters;

public sealed record RosterShareTokenPayloadDto(
    Guid TenantId,
    Guid CompanyId,
    Guid RosterId,
    DateOnly WeekStartDate,
    DateTime ExpiresAtUtc);

public sealed record SharedDayOffDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    bool IsCompanyClosedDay);

public sealed record SharedEmployeeSummaryDto(
    Guid EmployeeId,
    string Name,
    StationRole PrimaryRole,
    IReadOnlyList<RosterEmployeeSecondarySkillDto> SecondarySkills,
    IReadOnlyList<GeneratedShiftDto> Shifts,
    IReadOnlyList<SharedDayOffDto> DaysOff,
    int WorkedDaysCount,
    int DaysOffCount);

public sealed record SharedRosterWorkspaceDto(
    Guid RosterId,
    Guid CompanyId,
    string CompanyName,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    RosterStatus Status,
    ScheduleModel ScheduleModel,
    DateTime TokenExpiresAtUtc,
    Guid? SelectedEmployeeId,
    IReadOnlyList<SharedEmployeeSummaryDto> Employees,
    IReadOnlyList<ShiftSwapRequestDto> SwapRequests);

public sealed record CompatibleSwapOptionDto(
    Guid TargetEmployeeId,
    string TargetEmployeeName,
    StationRole TargetEmployeePrimaryRole,
    Guid? TargetShiftId,
    DateOnly? TargetShiftDate,
    TimeOnly? TargetShiftStartTime,
    TimeOnly? TargetShiftEndTime,
    StationRole? TargetShiftRole,
    bool IsMutualSwap,
    CoverageOrigin RequesterReplacementCoverageOrigin,
    CoverageOrigin? TargetReplacementCoverageOrigin,
    DemandClassification RequesterShiftDemandLevel,
    DemandClassification? TargetShiftDemandLevel);

public sealed record ManagerShiftSwapItemDto(
    ShiftSwapRequestDto SwapRequest,
    bool CanApproveNow,
    bool IsCltCompliant,
    string? ComplianceBlockMessage,
    ShiftSwapValidationSummaryDto? ValidationSummary);

public sealed record ManagerShiftSwapQueueDto(
    Guid CompanyId,
    int PendingManagerApprovalCount,
    int PendingPeerApprovalCount,
    IReadOnlyList<ManagerShiftSwapItemDto> Items);

