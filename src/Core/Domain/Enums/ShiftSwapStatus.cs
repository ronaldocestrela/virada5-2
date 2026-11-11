namespace Domain.Enums;

/// <summary>
/// Estados do ciclo de vida de uma solicitação de troca ou cessão de turno.
/// </summary>
public enum ShiftSwapStatus
{
    PendingPeerApproval = 1,
    PendingManagerApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5
}

