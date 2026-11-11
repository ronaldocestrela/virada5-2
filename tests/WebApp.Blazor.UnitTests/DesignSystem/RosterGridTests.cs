using Application.DTOs.Rosters;
using Bunit;
using Domain.Enums;
using Microsoft.AspNetCore.Components;
using WebApp.Blazor.Components.DesignSystem.RosterGrid;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class RosterGridTests : BunitContext
{
    [Fact]
    public void Render_WhenRosterIsNull_ShouldRenderEmptyState()
    {
        // Act
        var cut = Render<RosterGrid>(parameters => parameters
            .Add(p => p.Roster, null));

        // Assert
        cut.Find(".roster-grid__empty").TextContent.Should().Contain("Nenhuma escala disponível");
    }

    [Fact]
    public void Render_WhenRosterProvided_ShouldRenderSevenDaysPolyvalenceBadgesDaysOffAndCltAlerts()
    {
        // Arrange
        var roster = CreateSampleRoster(isCompliant: false);

        // Act
        var cut = Render<RosterGrid>(parameters => parameters
            .Add(p => p.Roster, roster));

        // Assert
        cut.FindAll(".roster-grid__day-header").Should().HaveCount(7);
        cut.FindAll(".roster-grid__employee-row").Should().HaveCount(2);
        cut.Find(".roster-grid__compliance-badge").TextContent.Should().Contain("Infração CLT");
        cut.FindAll(".roster-grid__shift--polyvalence").Should().ContainSingle();
        cut.FindAll(".roster-grid__day-off").Should().NotBeEmpty();
        cut.FindAll(".roster-grid__alert-item--error").Should().ContainSingle();
        cut.FindAll(".roster-grid__cell--has-alert").Should().ContainSingle();
    }

    [Fact]
    public void Filters_WhenRoleFilterAndShowOnlyAlertsSet_ShouldFilterDisplayedEmployees()
    {
        // Arrange
        var roster = CreateSampleRoster(isCompliant: false);

        // Act
        var cut = Render<RosterGrid>(parameters => parameters
            .Add(p => p.Roster, roster)
            .Add(p => p.RoleFilter, StationRole.Kitchen)
            .Add(p => p.ShowOnlyAlerts, true));

        // Assert
        var rows = cut.FindAll(".roster-grid__employee-row");
        rows.Should().ContainSingle();
        rows[0].TextContent.Should().Contain("Ana Cozinheira");
    }

    [Fact]
    public void Interactions_WhenShiftDayOffAndAlertClicked_ShouldInvokeCorrespondingCallbacks()
    {
        // Arrange
        var roster = CreateSampleRoster(isCompliant: false);
        GeneratedShiftDto? clickedShift = null;
        RosterDayOffCell? clickedDayOff = null;
        ComplianceAlertDto? clickedAlert = null;

        var cut = Render<RosterGrid>(parameters => parameters
            .Add(p => p.Roster, roster)
            .Add(p => p.OnShiftSelected, EventCallback.Factory.Create<GeneratedShiftDto>(this, s => clickedShift = s))
            .Add(p => p.OnDayOffSelected, EventCallback.Factory.Create<RosterDayOffCell>(this, d => clickedDayOff = d))
            .Add(p => p.OnAlertSelected, EventCallback.Factory.Create<ComplianceAlertDto>(this, a => clickedAlert = a)));

        // Act
        cut.Find(".roster-grid__shift").Click();
        cut.Find(".roster-grid__day-off").Click();
        cut.Find(".roster-grid__alert-item").Click();

        // Assert
        clickedShift.Should().NotBeNull();
        clickedDayOff.Should().NotBeNull();
        clickedAlert.Should().NotBeNull();
        clickedAlert!.Code.Should().Be("CLT.Interjornada");
    }

    private static GeneratedRosterResultDto CreateSampleRoster(bool isCompliant)
    {
        var weekStart = new DateOnly(2026, 11, 9); // Monday
        var weekEnd = weekStart.AddDays(6);
        var rosterId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var anaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();

        var anaShift = new GeneratedShiftDto(
            Guid.NewGuid(),
            anaId,
            "Ana Cozinheira",
            weekStart.AddDays(2),
            DayOfWeek.Wednesday,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Kitchen,
            CoverageOrigin.PrimaryRole);

        var brunoPolyShift = new GeneratedShiftDto(
            Guid.NewGuid(),
            brunoId,
            "Bruno Garçom",
            weekStart.AddDays(4),
            DayOfWeek.Friday,
            new TimeOnly(16, 0),
            new TimeOnly(23, 0),
            StationRole.Bar,
            CoverageOrigin.SecondarySkill);

        var alerts = isCompliant
            ? Array.Empty<ComplianceAlertDto>()
            :
            [
                new ComplianceAlertDto(
                    "CLT.Interjornada",
                    RosterAlertSeverity.Error,
                    RosterAlertCategory.CltHardConstraint,
                    "Intervalo interjornada inferior a 11h para Ana.",
                    weekStart.AddDays(2),
                    StationRole.Kitchen,
                    anaId)
            ];

        var coverageDetails = new[]
        {
            new ShiftRequirementCoverageDto(
                weekStart.AddDays(2),
                DayOfWeek.Wednesday,
                "Almoço",
                new TimeOnly(11, 0),
                new TimeOnly(19, 0),
                StationRole.Kitchen,
                DemandClassification.Moderate,
                1,
                1,
                0,
                ShiftCoverageStatus.FullyCovered),
            new ShiftRequirementCoverageDto(
                weekStart.AddDays(4),
                DayOfWeek.Friday,
                "Jantar",
                new TimeOnly(16, 0),
                new TimeOnly(23, 0),
                StationRole.Bar,
                DemandClassification.Peak,
                2,
                0,
                1,
                ShiftCoverageStatus.PartiallyCovered)
        };

        var employeeSummaries = new[]
        {
            new EmployeeRosterSummaryDto(
                anaId,
                "Ana Cozinheira",
                StationRole.Kitchen,
                5,
                2,
                [weekStart, weekStart.AddDays(1)],
                5,
                0,
                false,
                0),
            new EmployeeRosterSummaryDto(
                brunoId,
                "Bruno Garçom",
                StationRole.Waiter,
                5,
                2,
                [weekStart, weekStart.AddDays(1)],
                4,
                1,
                true,
                1)
        };

        var report = new RosterComplianceReportDto(
            rosterId,
            companyId,
            weekStart,
            weekEnd,
            RosterStatus.Draft,
            isCompliant,
            92.5m,
            95.0m,
            10,
            9,
            1,
            3,
            4,
            coverageDetails,
            employeeSummaries,
            alerts);

        return new GeneratedRosterResultDto(
            rosterId,
            companyId,
            "Bistrô Alfa",
            weekStart,
            weekEnd,
            RosterStatus.Draft,
            false,
            false,
            false,
            report,
            [anaShift, brunoPolyShift]);
    }
}

