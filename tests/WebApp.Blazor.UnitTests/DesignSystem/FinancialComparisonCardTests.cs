using Application.DTOs.Financial;
using Bunit;
using WebApp.Blazor.Components.DesignSystem.FinancialComparisonCard;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class FinancialComparisonCardTests : BunitContext
{
    [Fact]
    public void Render_WhenSimulationIsNull_ShouldRenderEmptyState()
    {
        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, null));

        // Assert
        cut.Find(".financial-card__empty").TextContent.Should().Contain("Nenhuma simulação financeira");
    }

    [Fact]
    public void Render_WhenSimulationProvided_ShouldRenderSavingsScenariosIdleExposureAndWarnings()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: true);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowDetailedBreakdown, true));

        // Assert
        var markup = cut.Markup;
        markup.Should().Contain("Bistrô Alfa");
        markup.Should().Contain("Cenário Atual 6x1");
        markup.Should().Contain("Cenário Otimizado 5x2");
        cut.Find(".financial-card__savings-monthly").TextContent.Should().Contain("3.400,00");
        cut.Find(".financial-card__savings-annual").TextContent.Should().Contain("40.800,00");
        cut.FindAll(".financial-card__idle-exposure").Should().ContainSingle();
        cut.Find(".financial-card__warnings").TextContent.Should().Contain("Estimativa baseada em 12 colaboradores.");
    }

    [Fact]
    public void Click_WhenPrimaryCtaEnabled_ShouldInvokeOnPrimaryCtaClick()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);
        var ctaClicked = false;

        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowPrimaryCta, true)
            .Add(p => p.PrimaryCtaText, "Gerar Escala 5x2")
            .Add(p => p.OnPrimaryCtaClick, () => ctaClicked = true));

        // Act
        cut.FindAll(".financial-card__idle-exposure").Should().BeEmpty();
        cut.Find("button").Click();

        // Assert
        ctaClicked.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenSimulationProvided_ShouldRenderVisualCostComparisonBars()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation));

        // Assert
        var bars = cut.FindAll("progress.financial-card__comparison-bar");
        bars.Should().HaveCount(2);
        bars[0].GetAttribute("max").Should().Be("40812");
        bars[0].GetAttribute("value").Should().Be("40812");
        bars[1].GetAttribute("value").Should().Be("37412");
        cut.Find(".financial-card__visual-comparison").TextContent.Should().Contain("6x1");
        cut.Find(".financial-card__visual-comparison").TextContent.Should().Contain("5x2");
    }

    [Fact]
    public void Render_WhenDetailedBreakdownProvided_ShouldDisplayOvertimeHoursAndAnnualTotal()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowDetailedBreakdown, true));

        // Assert
        var markup = cut.Markup;
        markup.Should().Contain("489.744,00");
        markup.Should().Contain("448.944,00");
        markup.Should().Contain("216,0 h");
        markup.Should().Contain("75,6 h");
    }

    [Fact]
    public void Render_WhenShowDetailedBreakdownFalse_ShouldOmitScenarioCards()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowDetailedBreakdown, false));

        // Assert
        cut.FindAll(".financial-card__scenarios").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenShowPrimaryCtaFalse_ShouldNotRenderEmptyFooterElement()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowPrimaryCta, false));

        // Assert
        cut.FindAll(".app-card__footer").Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, "app-badge--neutral")]
    [InlineData(-500, "app-badge--warning")]
    [InlineData(3400, "app-badge--success")]
    public void Render_WhenSavingsChange_ShouldApplyExpectedBadgeVariant(
        decimal monthlySavings,
        string expectedBadgeClass)
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false) with
        {
            MonthlySavings = monthlySavings,
            AnnualSavings = monthlySavings * 12m,
            SavingsPercentage = monthlySavings == 0m ? 0m : -1.2m
        };

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation));

        // Assert
        var badge = cut.Find(".app-card__header-extra .app-badge");
        badge.ClassList.Should().Contain(expectedBadgeClass);
    }

    [Fact]
    public void Render_WhenPrimaryCtaLoadingOrDisabled_ShouldPropagateStatesToAppButton()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .Add(p => p.ShowPrimaryCta, true)
            .Add(p => p.PrimaryCtaLoading, true)
            .Add(p => p.PrimaryCtaDisabled, true));

        // Assert
        var button = cut.Find(".app-card__footer button");
        button.HasAttribute("disabled").Should().BeTrue();
        button.GetAttribute("aria-busy").Should().Be("true");
    }

    [Fact]
    public void Render_WhenAdditionalAttributesProvided_ShouldForwardToRootCard()
    {
        // Arrange
        var simulation = CreateSampleSimulation(hasDemandMatrix: false);

        // Act
        var cut = Render<FinancialComparisonCard>(parameters => parameters
            .Add(p => p.Simulation, simulation)
            .AddUnmatched("data-testid", "financial-card"));

        // Assert
        cut.Find("[data-testid='financial-card']").Should().NotBeNull();
    }

    private static FinancialImpactSimulationDto CreateSampleSimulation(bool hasDemandMatrix)
    {
        var assumptions = new FinancialSimulationAssumptionsDto(220m, 0.38m, 0.50m, 0.20m, 0.65m, 0.45m, 0.18m, 1.5m, 0.04m, 0.015m);
        var current = new ScenarioFinancialBreakdownDto(
            "6x1 Atual",
            24000m,
            9120m,
            18m,
            216m,
            3900m,
            1482m,
            5382m,
            1350m,
            960m,
            40812m,
            489744m);
        var optimized = new ScenarioFinancialBreakdownDto(
            "5x2 Otimizado",
            24000m,
            9120m,
            6.3m,
            75.6m,
            1365m,
            518.7m,
            1883.7m,
            540m,
            360m,
            37412m,
            448944m);

        return new FinancialImpactSimulationDto(
            Guid.NewGuid(),
            "Bistrô Alfa",
            12,
            HeadcountSource.ActiveEmployeesRoster,
            2000m,
            assumptions,
            current,
            optimized,
            3400m,
            40800m,
            8.33m,
            140.4m,
            hasDemandMatrix,
            hasDemandMatrix ? 28.5m : null,
            hasDemandMatrix ? 5100m : null,
            ["Estimativa baseada em 12 colaboradores."]);
    }
}

