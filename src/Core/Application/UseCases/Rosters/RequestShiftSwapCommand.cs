namespace Application.UseCases.Rosters;

public sealed record RequestShiftSwapCommand(
    Guid RosterId,
    Guid RequesterEmployeeId,
    Guid RequesterShiftId,
    Guid TargetEmployeeId,
    Guid? TargetShiftId = null,
    string? Reason = null,
    bool RequirePeerApproval = true,
    Guid? CompanyId = null);

