using Application.DTOs.Rosters;
using Bunit;
using Domain.Enums;
using WebApp.Blazor.Components.DesignSystem.ShiftSwapRequestCard;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class ShiftSwapRequestCardTests : BunitContext
{
    [Fact]
    public void Render_WhenManagerCanApprove_ShouldDisplayCltBadgeAndInvokeOneClickApproveCallback()
    {
        // Arrange
        var swap = CreateSwapDto(ShiftSwapStatus.PendingManagerApproval);
        var approved = false;

        var cut = Render<ShiftSwapRequestCard>(parameters => parameters
            .Add(p => p.SwapRequest, swap)
            .Add(p => p.ShowCltBadge, true)
            .Add(p => p.IsCltCompliant, true)
            .Add(p => p.ShowManagerApprove, true)
            .Add(p => p.CanManagerApprove, true)
            .Add(p => p.ShowManagerReject, true)
            .Add(p => p.OnManagerApprove, () => approved = true));

        // Act
        cut.Find($"[data-testid=\"swap-clt-badge-{swap.Id}\"]").TextContent.Should().Contain("Conforme CLT");
        cut.Find($"[data-testid=\"manager-approve-btn-{swap.Id}\"]").Click();

        // Assert
        approved.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenPeerApprovalPending_ShouldEmitAcceptOrRejectBoolean()
    {
        // Arrange
        var swap = CreateSwapDto(ShiftSwapStatus.PendingPeerApproval);
        bool? peerDecision = null;

        var cut = Render<ShiftSwapRequestCard>(parameters => parameters
            .Add(p => p.SwapRequest, swap)
            .Add(p => p.ShowPeerActions, true)
            .Add(p => p.OnPeerRespond, val => peerDecision = val));

        // Act
        cut.Find($"[data-testid=\"peer-accept-btn-{swap.Id}\"]").Click();

        // Assert
        peerDecision.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenValidationSummaryHasPolyvalenceAndPeak_ShouldDisplayOperationalBadgesAndWeekHref()
    {
        // Arrange
        var swap = CreateSwapDto(ShiftSwapStatus.PendingManagerApproval) with
        {
            PeerRespondedAtUtc = new DateTime(2026, 11, 16, 14, 30, 0, DateTimeKind.Utc)
        };
        var summary = new ShiftSwapValidationSummaryDto(
            IsCltCompliant: true,
            RequesterShiftDemandLevel: DemandClassification.Peak,
            TargetShiftDemandLevel: DemandClassification.Moderate,
            RequesterReplacementCoverageOrigin: CoverageOrigin.SecondarySkill,
            TargetReplacementCoverageOrigin: CoverageOrigin.PrimaryRole);

        // Act
        var cut = Render<ShiftSwapRequestCard>(parameters => parameters
            .Add(p => p.SwapRequest, swap)
            .Add(p => p.ShowCltBadge, true)
            .Add(p => p.IsCltCompliant, true)
            .Add(p => p.ValidationSummary, summary)
            .Add(p => p.WeekHref, "/roster?week=2026-11-16")
            .Add(p => p.ShowManagerApprove, true)
            .Add(p => p.CanManagerApprove, true)
            .Add(p => p.ShowManagerReject, true));

        // Assert
        cut.Find($"[data-testid=\"swap-polyvalence-badge-{swap.Id}\"]").TextContent.Should().Contain("Polivalência");
        cut.Find($"[data-testid=\"swap-peak-badge-{swap.Id}\"]").TextContent.Should().Contain("Turno de pico");
        cut.Find($"[data-testid=\"swap-clt-summary-{swap.Id}\"]").TextContent.Should().Contain("11h");
        cut.Find($"[data-testid=\"swap-week-link-{swap.Id}\"]").GetAttribute("href").Should().Be("/roster?week=2026-11-16");
        cut.Markup.Should().Contain("Colega confirmou");
    }

    [Fact]
    public void Render_WhenCltViolationBlocksApproval_ShouldDisableOneClickApproveAndEmitRejectCallback()
    {
        // Arrange
        var swap = CreateSwapDto(ShiftSwapStatus.PendingManagerApproval);
        var rejected = false;

        var cut = Render<ShiftSwapRequestCard>(parameters => parameters
            .Add(p => p.SwapRequest, swap)
            .Add(p => p.ShowCltBadge, true)
            .Add(p => p.IsCltCompliant, false)
            .Add(p => p.ComplianceMessage, "Intervalo interjornada inferior a 11h.")
            .Add(p => p.ShowManagerApprove, true)
            .Add(p => p.CanManagerApprove, false)
            .Add(p => p.ShowManagerReject, true)
            .Add(p => p.OnManagerReject, () => rejected = true));

        // Act & Assert
        cut.Find($"[data-testid=\"swap-clt-badge-{swap.Id}\"]").TextContent.Should().Contain("Conflito CLT");
        cut.Find($"[data-testid=\"swap-clt-alert-{swap.Id}\"]").TextContent.Should().Contain("11h");
        cut.Find($"[data-testid=\"manager-approve-btn-{swap.Id}\"]").HasAttribute("disabled").Should().BeTrue();
        cut.Find($"[data-testid=\"manager-reject-btn-{swap.Id}\"]").Click();
        rejected.Should().BeTrue();
    }

    private static ShiftSwapRequestDto CreateSwapDto(ShiftSwapStatus status) =>
        new(
            Id: Guid.NewGuid(),
            CompanyId: Guid.NewGuid(),
            RosterId: Guid.NewGuid(),
            WeekStartDate: new DateOnly(2026, 11, 16),
            RequesterEmployeeId: Guid.NewGuid(),
            RequesterEmployeeName: "Ana Garçom",
            RequesterShiftId: Guid.NewGuid(),
            RequesterShiftDate: new DateOnly(2026, 11, 17),
            RequesterShiftStartTime: new TimeOnly(11, 0),
            RequesterShiftEndTime: new TimeOnly(19, 0),
            RequesterShiftRole: StationRole.Waiter,
            TargetEmployeeId: Guid.NewGuid(),
            TargetEmployeeName: "Bruno Bar",
            TargetShiftId: Guid.NewGuid(),
            TargetShiftDate: new DateOnly(2026, 11, 19),
            TargetShiftStartTime: new TimeOnly(16, 0),
            TargetShiftEndTime: new TimeOnly(23, 0),
            TargetShiftRole: StationRole.Bar,
            Status: status,
            Reason: "Consulta médica",
            DecisionNotes: null,
            RequestedAtUtc: DateTime.UtcNow,
            PeerRespondedAtUtc: null,
            ReviewedByUserId: null,
            ReviewedAtUtc: null);
}

