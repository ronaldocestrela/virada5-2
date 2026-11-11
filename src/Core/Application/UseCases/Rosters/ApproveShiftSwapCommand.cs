namespace Application.UseCases.Rosters;

public sealed record ApproveShiftSwapCommand(
    Guid ShiftSwapRequestId,
    string? Notes = null);

