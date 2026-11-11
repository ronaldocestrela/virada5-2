using Domain.Entities;
using Domain.Enums;

namespace Domain.UnitTests.Entities;

public class ShiftSwapRequestTests
{
    private static readonly Guid ValidTenantId = Guid.NewGuid();
    private static readonly Guid ValidCompanyId = Guid.NewGuid();
    private static readonly Guid ValidRosterId = Guid.NewGuid();
    private static readonly Guid ValidRequesterEmployeeId = Guid.NewGuid();
    private static readonly Guid ValidRequesterShiftId = Guid.NewGuid();
    private static readonly Guid ValidTargetEmployeeId = Guid.NewGuid();
    private static readonly Guid ValidTargetShiftId = Guid.NewGuid();
    private static readonly Guid ValidReviewerUserId = Guid.NewGuid();
    private static readonly DateTime FixedUtcNow = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WhenMutualSwapWithPeerApprovalRequired_ShouldInitializePendingPeerApproval()
    {
        // Act
        var result = ShiftSwapRequest.Create(
            ValidTenantId,
            ValidCompanyId,
            ValidRosterId,
            ValidRequesterEmployeeId,
            ValidRequesterShiftId,
            ValidTargetEmployeeId,
            ValidTargetShiftId,
            "  Consulta médica pela manhã  ",
            requirePeerApproval: true,
            utcNow: FixedUtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var swap = result.Value;
        swap.Id.Should().NotBe(Guid.Empty);
        swap.TenantId.Should().Be(ValidTenantId);
        swap.CompanyId.Should().Be(ValidCompanyId);
        swap.RosterId.Should().Be(ValidRosterId);
        swap.RequesterEmployeeId.Should().Be(ValidRequesterEmployeeId);
        swap.RequesterShiftId.Should().Be(ValidRequesterShiftId);
        swap.TargetEmployeeId.Should().Be(ValidTargetEmployeeId);
        swap.TargetShiftId.Should().Be(ValidTargetShiftId);
        swap.IsMutualSwap.Should().BeTrue();
        swap.IsPending.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);
        swap.Reason.Should().Be("Consulta médica pela manhã");
        swap.DecisionNotes.Should().BeNull();
        swap.RequestedAtUtc.Should().Be(FixedUtcNow);
        swap.PeerRespondedAtUtc.Should().BeNull();
        swap.ReviewedByUserId.Should().BeNull();
        swap.ReviewedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_WhenOneWayCoverWithoutPeerApproval_ShouldInitializePendingManagerApproval()
    {
        // Act
        var result = ShiftSwapRequest.Create(
            ValidTenantId,
            ValidCompanyId,
            ValidRosterId,
            ValidRequesterEmployeeId,
            ValidRequesterShiftId,
            ValidTargetEmployeeId,
            targetShiftId: null,
            reason: "   ",
            requirePeerApproval: false,
            utcNow: FixedUtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var swap = result.Value;
        swap.TargetShiftId.Should().BeNull();
        swap.IsMutualSwap.Should().BeFalse();
        swap.IsPending.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        swap.Reason.Should().BeNull();
    }

    [Fact]
    public void Create_WhenRequiredIdentifiersAreEmpty_ShouldReturnValidationFailure()
    {
        // Act & Assert
        ShiftSwapRequest.Create(Guid.Empty, ValidCompanyId, ValidRosterId, ValidRequesterEmployeeId, ValidRequesterShiftId, ValidTargetEmployeeId, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyTenantId");

        ShiftSwapRequest.Create(ValidTenantId, Guid.Empty, ValidRosterId, ValidRequesterEmployeeId, ValidRequesterShiftId, ValidTargetEmployeeId, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyCompanyId");

        ShiftSwapRequest.Create(ValidTenantId, ValidCompanyId, Guid.Empty, ValidRequesterEmployeeId, ValidRequesterShiftId, ValidTargetEmployeeId, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyRosterId");

        ShiftSwapRequest.Create(ValidTenantId, ValidCompanyId, ValidRosterId, Guid.Empty, ValidRequesterShiftId, ValidTargetEmployeeId, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyRequesterEmployeeId");

        ShiftSwapRequest.Create(ValidTenantId, ValidCompanyId, ValidRosterId, ValidRequesterEmployeeId, Guid.Empty, ValidTargetEmployeeId, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyRequesterShiftId");

        ShiftSwapRequest.Create(ValidTenantId, ValidCompanyId, ValidRosterId, ValidRequesterEmployeeId, ValidRequesterShiftId, Guid.Empty, ValidTargetShiftId)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyTargetEmployeeId");

        ShiftSwapRequest.Create(ValidTenantId, ValidCompanyId, ValidRosterId, ValidRequesterEmployeeId, ValidRequesterShiftId, ValidTargetEmployeeId, Guid.Empty)
            .Error.Code.Should().Be("ShiftSwapRequest.EmptyTargetShiftId");
    }

    [Fact]
    public void Create_WhenSameEmployeeOrSameShiftOrReasonTooLong_ShouldReturnValidationFailure()
    {
        // Arrange
        var longReason = new string('A', ShiftSwapRequest.MaxNotesLength + 1);

        // Act
        var sameEmployee = ShiftSwapRequest.Create(
            ValidTenantId, ValidCompanyId, ValidRosterId,
            ValidRequesterEmployeeId, ValidRequesterShiftId,
            ValidRequesterEmployeeId, ValidTargetShiftId);

        var sameShift = ShiftSwapRequest.Create(
            ValidTenantId, ValidCompanyId, ValidRosterId,
            ValidRequesterEmployeeId, ValidRequesterShiftId,
            ValidTargetEmployeeId, ValidRequesterShiftId);

        var tooLongReason = ShiftSwapRequest.Create(
            ValidTenantId, ValidCompanyId, ValidRosterId,
            ValidRequesterEmployeeId, ValidRequesterShiftId,
            ValidTargetEmployeeId, ValidTargetShiftId,
            longReason);

        // Assert
        sameEmployee.IsFailure.Should().BeTrue();
        sameEmployee.Error.Code.Should().Be("ShiftSwapRequest.SameEmployee");

        sameShift.IsFailure.Should().BeTrue();
        sameShift.Error.Code.Should().Be("ShiftSwapRequest.SameShift");

        tooLongReason.IsFailure.Should().BeTrue();
        tooLongReason.Error.Code.Should().Be("ShiftSwapRequest.ReasonTooLong");
    }

    [Fact]
    public void AcceptByPeer_WhenTargetEmployeeAndPendingPeer_ShouldTransitionToPendingManagerApproval()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var peerTime = FixedUtcNow.AddMinutes(20);

        // Act
        var result = swap.AcceptByPeer(ValidTargetEmployeeId, peerTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        swap.PeerRespondedAtUtc.Should().Be(peerTime);
        swap.IsPending.Should().BeTrue();
    }

    [Fact]
    public void AcceptByPeer_WhenUnauthorizedOrInvalidState_ShouldReturnFailure()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var managerReady = CreatePendingManagerSwap();

        // Act
        var wrongPeer = swap.AcceptByPeer(ValidRequesterEmployeeId);
        var invalidState = managerReady.AcceptByPeer(ValidTargetEmployeeId);

        // Assert
        wrongPeer.IsFailure.Should().BeTrue();
        wrongPeer.Error.Code.Should().Be("ShiftSwapRequest.UnauthorizedPeer");

        invalidState.IsFailure.Should().BeTrue();
        invalidState.Error.Code.Should().Be("ShiftSwapRequest.InvalidStateTransition");
    }

    [Fact]
    public void RejectByPeer_WhenTargetEmployeeAndPendingPeer_ShouldTransitionToRejected()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var peerTime = FixedUtcNow.AddMinutes(15);

        // Act
        var result = swap.RejectByPeer(ValidTargetEmployeeId, "  Tenho compromisso nesse dia  ", peerTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.Rejected);
        swap.DecisionNotes.Should().Be("Tenho compromisso nesse dia");
        swap.PeerRespondedAtUtc.Should().Be(peerTime);
        swap.IsPending.Should().BeFalse();
    }

    [Fact]
    public void RejectByPeer_WhenUnauthorizedInvalidStateOrNotesTooLong_ShouldReturnFailure()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var managerReady = CreatePendingManagerSwap();
        var longNotes = new string('B', ShiftSwapRequest.MaxNotesLength + 1);

        // Act & Assert
        swap.RejectByPeer(Guid.NewGuid()).Error.Code.Should().Be("ShiftSwapRequest.UnauthorizedPeer");
        managerReady.RejectByPeer(ValidTargetEmployeeId).Error.Code.Should().Be("ShiftSwapRequest.InvalidStateTransition");
        swap.RejectByPeer(ValidTargetEmployeeId, longNotes).Error.Code.Should().Be("ShiftSwapRequest.NotesTooLong");
    }

    [Fact]
    public void Approve_WhenPendingManagerApproval_ShouldTransitionToApproved()
    {
        // Arrange
        var swap = CreatePendingManagerSwap();
        var reviewTime = FixedUtcNow.AddHours(1);

        // Act
        var result = swap.Approve(ValidReviewerUserId, "  Aprovado conforme CLT  ", reviewTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.Approved);
        swap.ReviewedByUserId.Should().Be(ValidReviewerUserId);
        swap.ReviewedAtUtc.Should().Be(reviewTime);
        swap.DecisionNotes.Should().Be("Aprovado conforme CLT");
        swap.IsPending.Should().BeFalse();
    }

    [Fact]
    public void Approve_WhenPendingPeerApprovalOrAlreadyFinalizedOrNotesTooLong_ShouldReturnFailure()
    {
        // Arrange
        var pendingPeer = CreatePendingPeerSwap();
        var approved = CreatePendingManagerSwap();
        approved.Approve(ValidReviewerUserId).IsSuccess.Should().BeTrue();
        var pendingManager = CreatePendingManagerSwap();
        var longNotes = new string('C', ShiftSwapRequest.MaxNotesLength + 1);

        // Act & Assert
        pendingPeer.Approve(ValidReviewerUserId).Error.Code.Should().Be("ShiftSwapRequest.NotPendingManagerApproval");
        approved.Approve(ValidReviewerUserId).Error.Code.Should().Be("ShiftSwapRequest.NotPendingManagerApproval");
        pendingManager.Approve(ValidReviewerUserId, longNotes).Error.Code.Should().Be("ShiftSwapRequest.NotesTooLong");
    }

    [Fact]
    public void RejectByManager_WhenPending_ShouldTransitionToRejected()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var reviewTime = FixedUtcNow.AddMinutes(45);

        // Act
        var result = swap.RejectByManager(ValidReviewerUserId, "Cobertura insuficiente na praça", reviewTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.Rejected);
        swap.ReviewedByUserId.Should().Be(ValidReviewerUserId);
        swap.ReviewedAtUtc.Should().Be(reviewTime);
        swap.DecisionNotes.Should().Be("Cobertura insuficiente na praça");
        swap.IsPending.Should().BeFalse();
    }

    [Fact]
    public void RejectByManager_WhenAlreadyFinalizedOrNotesTooLong_ShouldReturnFailure()
    {
        // Arrange
        var finalized = CreatePendingManagerSwap();
        finalized.Approve(ValidReviewerUserId).IsSuccess.Should().BeTrue();
        var pending = CreatePendingManagerSwap();

        // Act & Assert
        finalized.RejectByManager(ValidReviewerUserId).Error.Code.Should().Be("ShiftSwapRequest.AlreadyFinalized");
        pending.RejectByManager(ValidReviewerUserId, new string('D', ShiftSwapRequest.MaxNotesLength + 1))
            .Error.Code.Should().Be("ShiftSwapRequest.NotesTooLong");
    }

    [Fact]
    public void Cancel_WhenRequesterAndPending_ShouldTransitionToCancelled()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var cancelTime = FixedUtcNow.AddMinutes(10);

        // Act
        var result = swap.Cancel(ValidRequesterEmployeeId, "Não preciso mais trocar", cancelTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        swap.Status.Should().Be(ShiftSwapStatus.Cancelled);
        swap.ReviewedAtUtc.Should().Be(cancelTime);
        swap.DecisionNotes.Should().Be("Não preciso mais trocar");
        swap.IsPending.Should().BeFalse();
    }

    [Fact]
    public void Cancel_WhenUnauthorizedAlreadyFinalizedOrNotesTooLong_ShouldReturnFailure()
    {
        // Arrange
        var swap = CreatePendingPeerSwap();
        var finalized = CreatePendingManagerSwap();
        finalized.RejectByManager(ValidReviewerUserId).IsSuccess.Should().BeTrue();

        // Act & Assert
        swap.Cancel(ValidTargetEmployeeId).Error.Code.Should().Be("ShiftSwapRequest.UnauthorizedCancellation");
        finalized.Cancel(ValidRequesterEmployeeId).Error.Code.Should().Be("ShiftSwapRequest.AlreadyFinalized");
        swap.Cancel(ValidRequesterEmployeeId, new string('E', ShiftSwapRequest.MaxNotesLength + 1))
            .Error.Code.Should().Be("ShiftSwapRequest.NotesTooLong");
    }

    private static ShiftSwapRequest CreatePendingPeerSwap() =>
        ShiftSwapRequest.Create(
            ValidTenantId,
            ValidCompanyId,
            ValidRosterId,
            ValidRequesterEmployeeId,
            ValidRequesterShiftId,
            ValidTargetEmployeeId,
            ValidTargetShiftId,
            "Motivo válido",
            requirePeerApproval: true,
            utcNow: FixedUtcNow).Value;

    private static ShiftSwapRequest CreatePendingManagerSwap() =>
        ShiftSwapRequest.Create(
            ValidTenantId,
            ValidCompanyId,
            ValidRosterId,
            ValidRequesterEmployeeId,
            ValidRequesterShiftId,
            ValidTargetEmployeeId,
            ValidTargetShiftId,
            "Motivo válido",
            requirePeerApproval: false,
            utcNow: FixedUtcNow).Value;
}

