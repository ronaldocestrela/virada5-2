using Domain.Enums;

namespace Application.DTOs.Rosters;

public sealed record ShiftSwapRequestDto(
    Guid Id,
    Guid CompanyId,
    Guid RosterId,
    DateOnly WeekStartDate,
    Guid RequesterEmployeeId,
    string RequesterEmployeeName,
    Guid RequesterShiftId,
    DateOnly RequesterShiftDate,
    TimeOnly RequesterShiftStartTime,
    TimeOnly RequesterShiftEndTime,
    StationRole RequesterShiftRole,
    Guid TargetEmployeeId,
    string TargetEmployeeName,
    Guid? TargetShiftId,
    DateOnly? TargetShiftDate,
    TimeOnly? TargetShiftStartTime,
    TimeOnly? TargetShiftEndTime,
    StationRole? TargetShiftRole,
    ShiftSwapStatus Status,
    string? Reason,
    string? DecisionNotes,
    DateTime RequestedAtUtc,
    DateTime? PeerRespondedAtUtc,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc);

public sealed record ShiftSwapValidationSummaryDto(
    bool IsCltCompliant,
    DemandClassification RequesterShiftDemandLevel,
    DemandClassification? TargetShiftDemandLevel,
    CoverageOrigin? RequesterReplacementCoverageOrigin,
    CoverageOrigin? TargetReplacementCoverageOrigin);

public sealed record ShiftSwapActionResultDto(
    ShiftSwapRequestDto SwapRequest,
    ShiftSwapValidationSummaryDto ValidationSummary);

