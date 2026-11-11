using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Raiz de agregação que representa uma solicitação de troca mútua ou cessão unilateral de turno entre colaboradores.
/// </summary>
public sealed class ShiftSwapRequest : AggregateRoot, ITenantEntity
{
    public const int MaxNotesLength = 500;

    private ShiftSwapRequest()
    {
    }

    private ShiftSwapRequest(
        Guid tenantId,
        Guid companyId,
        Guid rosterId,
        Guid requesterEmployeeId,
        Guid requesterShiftId,
        Guid targetEmployeeId,
        Guid? targetShiftId,
        ShiftSwapStatus status,
        string? reason,
        DateTime requestedAtUtc)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        RosterId = rosterId;
        RequesterEmployeeId = requesterEmployeeId;
        RequesterShiftId = requesterShiftId;
        TargetEmployeeId = targetEmployeeId;
        TargetShiftId = targetShiftId;
        Status = status;
        Reason = reason;
        RequestedAtUtc = requestedAtUtc;
    }

    public Guid TenantId { get; set; }
    public Guid CompanyId { get; private set; }
    public Guid RosterId { get; private set; }
    public Guid RequesterEmployeeId { get; private set; }
    public Guid RequesterShiftId { get; private set; }
    public Guid TargetEmployeeId { get; private set; }
    public Guid? TargetShiftId { get; private set; }
    public ShiftSwapStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public string? DecisionNotes { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? PeerRespondedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public bool IsMutualSwap => TargetShiftId.HasValue;
    public bool IsPending => Status is ShiftSwapStatus.PendingPeerApproval or ShiftSwapStatus.PendingManagerApproval;

    /// <summary>
    /// Cria uma nova solicitação de troca ou cessão de turno válida.
    /// </summary>
    public static Result<ShiftSwapRequest> Create(
        Guid tenantId,
        Guid companyId,
        Guid rosterId,
        Guid requesterEmployeeId,
        Guid requesterShiftId,
        Guid targetEmployeeId,
        Guid? targetShiftId = null,
        string? reason = null,
        bool requirePeerApproval = true,
        DateTime? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        if (companyId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyCompanyId", "O identificador do estabelecimento é obrigatório."));
        }

        if (rosterId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyRosterId", "O identificador da escala é obrigatório."));
        }

        if (requesterEmployeeId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterEmployeeId", "O colaborador solicitante é obrigatório."));
        }

        if (requesterShiftId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterShiftId", "O turno de origem é obrigatório."));
        }

        if (targetEmployeeId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.EmptyTargetEmployeeId", "O colaborador destinatário é obrigatório."));
        }

        if (requesterEmployeeId == targetEmployeeId)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwapRequest.SameEmployee", "Não é possível solicitar troca de turno consigo mesmo."));
        }

        if (targetShiftId.HasValue)
        {
            if (targetShiftId.Value == Guid.Empty)
            {
                return Result.Failure<ShiftSwapRequest>(
                    Error.Validation("ShiftSwapRequest.EmptyTargetShiftId", "O identificador do turno de destino não pode ser vazio."));
            }

            if (targetShiftId.Value == requesterShiftId)
            {
                return Result.Failure<ShiftSwapRequest>(
                    Error.Validation("ShiftSwapRequest.SameShift", "Os turnos informados para troca devem ser distintos."));
            }
        }

        var normalizedReasonResult = NormalizeOptionalText(
            reason,
            "ShiftSwapRequest.ReasonTooLong",
            $"O motivo da solicitação não pode exceder {MaxNotesLength} caracteres.");

        if (normalizedReasonResult.IsFailure)
        {
            return Result.Failure<ShiftSwapRequest>(normalizedReasonResult.Error);
        }

        var initialStatus = requirePeerApproval
            ? ShiftSwapStatus.PendingPeerApproval
            : ShiftSwapStatus.PendingManagerApproval;

        return Result.Success(new ShiftSwapRequest(
            tenantId,
            companyId,
            rosterId,
            requesterEmployeeId,
            requesterShiftId,
            targetEmployeeId,
            targetShiftId,
            initialStatus,
            normalizedReasonResult.Value,
            utcNow ?? DateTime.UtcNow));
    }

    /// <summary>
    /// Registra o aceite do colaborador destinatário e encaminha a solicitação para aprovação do gestor.
    /// </summary>
    public Result AcceptByPeer(Guid actingEmployeeId, DateTime? utcNow = null)
    {
        if (actingEmployeeId == Guid.Empty || actingEmployeeId != TargetEmployeeId)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.UnauthorizedPeer", "Somente o colaborador destinatário pode responder à solicitação."));
        }

        if (Status != ShiftSwapStatus.PendingPeerApproval)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.InvalidStateTransition", "A solicitação não está aguardando confirmação do colega."));
        }

        Status = ShiftSwapStatus.PendingManagerApproval;
        PeerRespondedAtUtc = utcNow ?? DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Registra a recusa do colaborador destinatário encerrando a solicitação.
    /// </summary>
    public Result RejectByPeer(Guid actingEmployeeId, string? notes = null, DateTime? utcNow = null)
    {
        if (actingEmployeeId == Guid.Empty || actingEmployeeId != TargetEmployeeId)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.UnauthorizedPeer", "Somente o colaborador destinatário pode recusar a solicitação."));
        }

        if (Status != ShiftSwapStatus.PendingPeerApproval)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.InvalidStateTransition", "A solicitação não está aguardando confirmação do colega."));
        }

        var normalizedNotesResult = NormalizeOptionalText(
            notes,
            "ShiftSwapRequest.NotesTooLong",
            $"As observações da decisão não podem exceder {MaxNotesLength} caracteres.");

        if (normalizedNotesResult.IsFailure)
        {
            return Result.Failure(normalizedNotesResult.Error);
        }

        Status = ShiftSwapStatus.Rejected;
        DecisionNotes = normalizedNotesResult.Value;
        PeerRespondedAtUtc = utcNow ?? DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Aprova a solicitação pelo gestor após validação integral de conformidade CLT.
    /// </summary>
    public Result Approve(Guid? reviewedByUserId = null, string? notes = null, DateTime? utcNow = null)
    {
        if (Status != ShiftSwapStatus.PendingManagerApproval)
        {
            return Result.Failure(
                Error.Conflict(
                    "ShiftSwapRequest.NotPendingManagerApproval",
                    "A solicitação precisa estar pendente de aprovação do gestor para ser aprovada."));
        }

        var normalizedNotesResult = NormalizeOptionalText(
            notes,
            "ShiftSwapRequest.NotesTooLong",
            $"As observações da decisão não podem exceder {MaxNotesLength} caracteres.");

        if (normalizedNotesResult.IsFailure)
        {
            return Result.Failure(normalizedNotesResult.Error);
        }

        Status = ShiftSwapStatus.Approved;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = utcNow ?? DateTime.UtcNow;
        DecisionNotes = normalizedNotesResult.Value;
        return Result.Success();
    }

    /// <summary>
    /// Recusa a solicitação pelo gestor encerrando o fluxo.
    /// </summary>
    public Result RejectByManager(Guid? reviewedByUserId = null, string? notes = null, DateTime? utcNow = null)
    {
        if (!IsPending)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.AlreadyFinalized", "A solicitação já foi finalizada e não pode ser recusada."));
        }

        var normalizedNotesResult = NormalizeOptionalText(
            notes,
            "ShiftSwapRequest.NotesTooLong",
            $"As observações da decisão não podem exceder {MaxNotesLength} caracteres.");

        if (normalizedNotesResult.IsFailure)
        {
            return Result.Failure(normalizedNotesResult.Error);
        }

        Status = ShiftSwapStatus.Rejected;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = utcNow ?? DateTime.UtcNow;
        DecisionNotes = normalizedNotesResult.Value;
        return Result.Success();
    }

    /// <summary>
    /// Cancela a solicitação a pedido do colaborador solicitante enquanto ainda pendente.
    /// </summary>
    public Result Cancel(Guid actingEmployeeId, string? notes = null, DateTime? utcNow = null)
    {
        if (actingEmployeeId == Guid.Empty || actingEmployeeId != RequesterEmployeeId)
        {
            return Result.Failure(
                Error.Conflict(
                    "ShiftSwapRequest.UnauthorizedCancellation",
                    "Somente o colaborador solicitante pode cancelar esta solicitação."));
        }

        if (!IsPending)
        {
            return Result.Failure(
                Error.Conflict("ShiftSwapRequest.AlreadyFinalized", "A solicitação já foi finalizada e não pode ser cancelada."));
        }

        var normalizedNotesResult = NormalizeOptionalText(
            notes,
            "ShiftSwapRequest.NotesTooLong",
            $"As observações da decisão não podem exceder {MaxNotesLength} caracteres.");

        if (normalizedNotesResult.IsFailure)
        {
            return Result.Failure(normalizedNotesResult.Error);
        }

        Status = ShiftSwapStatus.Cancelled;
        ReviewedAtUtc = utcNow ?? DateTime.UtcNow;
        DecisionNotes = normalizedNotesResult.Value;
        return Result.Success();
    }

    private static Result<string?> NormalizeOptionalText(string? value, string errorCode, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success<string?>(null);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxNotesLength)
        {
            return Result.Failure<string?>(Error.Validation(errorCode, errorMessage));
        }

        return Result.Success<string?>(trimmed);
    }
}

