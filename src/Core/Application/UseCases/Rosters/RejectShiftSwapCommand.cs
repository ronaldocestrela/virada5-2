namespace Application.UseCases.Rosters;

public sealed record RejectShiftSwapCommand(
    Guid ShiftSwapRequestId,
    string? Notes = null,
    Guid? ActingEmployeeId = null,
    bool IsCancellationByRequester = false);

