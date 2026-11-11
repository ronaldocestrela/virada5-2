namespace Application.UseCases.Rosters;

public sealed record RespondShiftSwapPeerCommand(
    Guid ShiftSwapRequestId,
    Guid ActingEmployeeId,
    bool Accept,
    string? Notes = null);

