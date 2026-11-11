using Application.DTOs.Demand;
using Bunit;
using Domain.Enums;
using Microsoft.AspNetCore.Components;
using WebApp.Blazor.Components.DesignSystem.DemandHeatmap;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class DemandHeatmapTests : BunitContext
{
    [Fact]
    public void Render_WhenDiagnosisIsNull_ShouldRenderEmptyState()
    {
        // Act
        var cut = Render<DemandHeatmap>(parameters => parameters
            .Add(p => p.Diagnosis, null));

        // Assert
        cut.Find(".demand-heatmap__empty").TextContent.Should().Contain("Nenhum mapa de demanda");
    }

    [Fact]
    public void Render_WhenDiagnosisProvided_ShouldClassifyCellsAndHighlightRecommendedDaysOff()
    {
        // Arrange
        var diagnosis = CreateSampleDiagnosis();

        // Act
        var cut = Render<DemandHeatmap>(parameters => parameters
            .Add(p => p.Diagnosis, diagnosis)
            .Add(p => p.ShowSummaryHeader, true));

        // Assert
        cut.Find(".demand-heatmap__summary").TextContent.Should().Contain("Bistrô Alfa");
        cut.Find(".demand-heatmap__summary").TextContent.Should().Contain("35");

        var peakCells = cut.FindAll(".demand-heatmap__cell--peak");
        var idleCells = cut.FindAll(".demand-heatmap__cell--idle");
        var lowCells = cut.FindAll(".demand-heatmap__cell--low");
        var moderateCells = cut.FindAll(".demand-heatmap__cell--moderate");
        var closedCells = cut.FindAll(".demand-heatmap__cell--closed");

        peakCells.Should().ContainSingle();
        idleCells.Should().ContainSingle();
        lowCells.Should().ContainSingle();
        moderateCells.Should().ContainSingle();
        closedCells.Should().ContainSingle();

        var recommendedHeaders = cut.FindAll(".demand-heatmap__day-header--recommended");
        recommendedHeaders.Should().HaveCount(2);
    }

    [Fact]
    public void Click_WhenInteractiveAndSlotClicked_ShouldInvokeOnSlotSelected()
    {
        // Arrange
        var diagnosis = CreateSampleDiagnosis();
        DemandSlotSnapshotDto? selected = null;

        var cut = Render<DemandHeatmap>(parameters => parameters
            .Add(p => p.Diagnosis, diagnosis)
            .Add(p => p.Interactive, true)
            .Add(p => p.OnSlotSelected, EventCallback.Factory.Create<DemandSlotSnapshotDto>(this, slot => selected = slot)));

        // Act
        cut.Find(".demand-heatmap__cell--peak").Click();

        // Assert
        selected.Should().NotBeNull();
        selected!.Classification.Should().Be(DemandClassification.Peak);
        selected.DayOfWeek.Should().Be(DayOfWeek.Friday);
        selected.Hour.Should().Be(20);
    }

    private static DemandDiagnosisDto CreateSampleDiagnosis()
    {
        var idleSlot = new DemandSlotSnapshotDto(Guid.NewGuid(), DayOfWeek.Monday, 12, 10m, 0.10m, DemandClassification.Idle, true);
        var lowSlot = new DemandSlotSnapshotDto(Guid.NewGuid(), DayOfWeek.Tuesday, 12, 25m, 0.25m, DemandClassification.Low, true);
        var moderateSlot = new DemandSlotSnapshotDto(Guid.NewGuid(), DayOfWeek.Wednesday, 20, 60m, 0.60m, DemandClassification.Moderate, true);
        var peakSlot = new DemandSlotSnapshotDto(Guid.NewGuid(), DayOfWeek.Friday, 20, 100m, 1.00m, DemandClassification.Peak, true);
        var closedSlot = new DemandSlotSnapshotDto(Guid.NewGuid(), DayOfWeek.Monday, 20, 0m, 0m, DemandClassification.Idle, false);

        return new DemandDiagnosisDto(
            Guid.NewGuid(),
            "Bistrô Alfa",
            100m,
            195m,
            35m,
            peakSlot,
            [idleSlot, lowSlot, moderateSlot, peakSlot, closedSlot],
            [peakSlot],
            [idleSlot, lowSlot],
            [
                new DailyDemandSummaryDto(DayOfWeek.Monday, true, 1, 0, 1, 10m, 10m),
                new DailyDemandSummaryDto(DayOfWeek.Tuesday, true, 1, 0, 1, 25m, 25m),
                new DailyDemandSummaryDto(DayOfWeek.Wednesday, true, 1, 0, 0, 60m, 60m),
                new DailyDemandSummaryDto(DayOfWeek.Friday, true, 1, 1, 0, 100m, 100m)
            ],
            [DayOfWeek.Monday, DayOfWeek.Tuesday]);
    }
}

