using Application.DTOs.Rosters;
using Bunit;
using Domain.Enums;
using Microsoft.AspNetCore.Components;
using WebApp.Blazor.Components.DesignSystem.PeakCoverageBar;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class PeakCoverageBarTests : BunitContext
{
    [Fact]
    public void Render_WhenNoPeakSlotsExist_ShouldDisplayEmptyNotice()
    {
        // Arrange
        var details = new List<ShiftRequirementCoverageDto>
        {
            new(
                new DateOnly(2026, 11, 16),
                DayOfWeek.Monday,
                "Turno Único",
                new TimeOnly(11, 0),
                new TimeOnly(19, 0),
                StationRole.Kitchen,
                DemandClassification.Moderate,
                1,
                1,
                0,
                ShiftCoverageStatus.FullyCovered)
        };

        // Act
        var cut = Render<PeakCoverageBar>(p => p.Add(x => x.CoverageDetails, details));

        // Assert
        cut.Find(".peak-coverage-bar__empty").TextContent.Should().Contain("Nenhum turno de pico");
    }

    [Fact]
    public void Render_WhenPeakSlotsExist_ShouldDisplayProgressRatioAndStatusChips()
    {
        // Arrange
        var details = new List<ShiftRequirementCoverageDto>
        {
            new(
                new DateOnly(2026, 11, 20),
                DayOfWeek.Friday,
                "Jantar",
                new TimeOnly(17, 0),
                new TimeOnly(23, 0),
                StationRole.Waiter,
                DemandClassification.Peak,
                2,
                2,
                0,
                ShiftCoverageStatus.FullyCovered),
            new(
                new DateOnly(2026, 11, 21),
                DayOfWeek.Saturday,
                "Jantar",
                new TimeOnly(17, 0),
                new TimeOnly(23, 0),
                StationRole.Kitchen,
                DemandClassification.Peak,
                2,
                1,
                0,
                ShiftCoverageStatus.PartiallyCovered)
        };

        // Act
        var cut = Render<PeakCoverageBar>(p => p.Add(x => x.CoverageDetails, details));

        // Assert
        cut.Find("[data-testid=\"peak-coverage-summary\"]").TextContent.Should().Contain("3/4");
        cut.Find("progress.peak-coverage-bar__progress").GetAttribute("value").Should().Be("75");
        cut.FindAll(".peak-coverage-bar__slot").Should().HaveCount(2);
        cut.FindAll(".peak-coverage-bar__slot--full").Should().ContainSingle();
        cut.FindAll(".peak-coverage-bar__slot--partial").Should().ContainSingle();
    }

    [Fact]
    public void Click_WhenPeakSlotClicked_ShouldInvokeOnSlotSelectedCallback()
    {
        // Arrange
        var slot = new ShiftRequirementCoverageDto(
            new DateOnly(2026, 11, 21),
            DayOfWeek.Saturday,
            "Jantar",
            new TimeOnly(17, 0),
            new TimeOnly(23, 0),
            StationRole.Bar,
            DemandClassification.Peak,
            1,
            0,
            0,
            ShiftCoverageStatus.Uncovered);

        ShiftRequirementCoverageDto? selected = null;
        var cut = Render<PeakCoverageBar>(p => p
            .Add(x => x.CoverageDetails, [slot])
            .Add(x => x.OnSlotSelected, EventCallback.Factory.Create<ShiftRequirementCoverageDto>(this, s => selected = s)));

        // Act
        cut.Find(".peak-coverage-bar__slot--uncovered").Click();

        // Assert
        selected.Should().Be(slot);
    }
}

