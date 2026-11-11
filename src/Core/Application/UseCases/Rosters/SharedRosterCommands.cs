namespace Application.UseCases.Rosters;

public sealed record GetSharedRosterWorkspaceCommand(
    string Token,
    Guid? SelectedEmployeeId = null);

public sealed record GetCompatibleSwapOptionsCommand(
    string Token,
    Guid RequesterEmployeeId,
    Guid RequesterShiftId);

public sealed record RequestSharedShiftSwapCommand(
    string Token,
    Guid RequesterEmployeeId,
    Guid RequesterShiftId,
    Guid TargetEmployeeId,
    Guid? TargetShiftId = null,
    string? Reason = null);

public sealed record RespondSharedShiftSwapPeerCommand(
    string Token,
    Guid ShiftSwapRequestId,
    Guid ActingEmployeeId,
    bool Accept,
    string? Notes = null);

public sealed record CancelSharedShiftSwapCommand(
    string Token,
    Guid ShiftSwapRequestId,
    Guid ActingEmployeeId,
    string? Notes = null);

public sealed record GetPendingShiftSwapsCommand(
    Guid? CompanyId = null,
    bool IncludeCltEvaluation = true);

