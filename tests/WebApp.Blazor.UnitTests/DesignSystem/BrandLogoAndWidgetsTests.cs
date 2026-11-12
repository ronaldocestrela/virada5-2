using Bunit;
using WebApp.Blazor.Components.DesignSystem.BrandLogo;
using WebApp.Blazor.Components.DesignSystem.HeroBanner;
using WebApp.Blazor.Components.DesignSystem.MetricTile;
using WebApp.Blazor.Components.DesignSystem.StepProgressBar;
using WebApp.Blazor.Components.DesignSystem.ThemeToggle;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class BrandLogoAndWidgetsTests : BunitContext
{
    [Fact]
    public void BrandLogo_WhenRenderedWithDefaultParameters_ShouldRenderSvgSymbolAndBrandText()
    {
        // Act
        var cut = Render<BrandLogo>();

        // Assert
        var symbol = cut.Find("svg.brand-logo__symbol");
        symbol.Should().NotBeNull();
        symbol.GetAttribute("viewBox").Should().Be("0 0 32 32");

        var brandText = cut.Find(".brand-logo__text");
        brandText.TextContent.Should().Contain("Virada");
        brandText.TextContent.Should().Contain("5x2");
    }

    [Fact]
    public void BrandLogo_WhenShowTextIsFalse_ShouldRenderOnlySymbol()
    {
        // Act
        var cut = Render<BrandLogo>(parameters => parameters
            .Add(p => p.ShowText, false));

        // Assert
        cut.Find("svg.brand-logo__symbol").Should().NotBeNull();
        cut.FindAll(".brand-logo__text").Should().BeEmpty();
    }

    [Fact]
    public void ThemeToggle_WhenRendered_ShouldRenderButtonWithSunAndMoonIcons()
    {
        // Act
        var cut = Render<ThemeToggle>();

        // Assert
        var button = cut.Find("button.theme-toggle");
        button.GetAttribute("aria-label").Should().Be("Alternar tema claro e escuro");
        cut.FindAll("svg.theme-toggle__icon--sun").Should().ContainSingle();
        cut.FindAll("svg.theme-toggle__icon--moon").Should().ContainSingle();
    }

    [Fact]
    public void HeroBanner_WhenRendered_ShouldDisplayCountdownTitleAndWeekStrip()
    {
        // Act
        var cut = Render<HeroBanner>();

        // Assert
        cut.Find(".hero-banner__title").TextContent.Should().Be("A virada é agora.");
        cut.Find(".hero-banner__days-number").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.Find(".hero-banner__deadline-date").TextContent.Should().Contain("16/12/2026");

        var daySlots = cut.FindAll(".hero-banner__day-slot");
        daySlots.Should().HaveCount(7, "a faixa da semana deve conter 7 dias (S T Q Q S S D)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void StepProgressBar_WhenStepProvided_ShouldMarkCompletedAndCurrentProperly(int currentStep)
    {
        // Act
        var cut = Render<StepProgressBar>(parameters => parameters
            .Add(p => p.CurrentStep, currentStep));

        // Assert
        var items = cut.FindAll(".step-progress__item");
        items.Should().HaveCount(5, "o fluxo possui 5 passos oficiais: Empresa, Horário, Equipe, Dinheiro, Plano");

        var currentItem = cut.Find(".step-progress__item--current");
        currentItem.Should().NotBeNull();
        currentItem.GetAttribute("aria-current").Should().Be("step");

        var completedCount = cut.FindAll(".step-progress__item--completed").Count;
        completedCount.Should().Be(currentStep - 1);
    }

    [Theory]
    [InlineData(MetricTile.MetricTileStripe.Orange, "metric-tile--orange")]
    [InlineData(MetricTile.MetricTileStripe.Navy, "metric-tile--navy")]
    [InlineData(MetricTile.MetricTileStripe.Green, "metric-tile--green")]
    [InlineData(MetricTile.MetricTileStripe.Alert, "metric-tile--alert")]
    public void MetricTile_WhenRenderedWithStripeAndValues_ShouldApplyCorrectClassAndRenderContent(
        MetricTile.MetricTileStripe stripe,
        string expectedClass)
    {
        // Act
        var cut = Render<MetricTile>(parameters => parameters
            .Add(p => p.Label, "Prazo")
            .Add(p => p.Value, "34")
            .Add(p => p.Unit, "dias")
            .Add(p => p.Description, "Até 16/12/2026")
            .Add(p => p.Stripe, stripe));

        // Assert
        var tile = cut.Find(".metric-tile");
        tile.ClassList.Should().Contain(expectedClass);
        cut.Find(".metric-tile__label").TextContent.Should().Be("Prazo");
        cut.Find(".metric-tile__value").TextContent.Should().Be("34");
        cut.Find(".metric-tile__unit").TextContent.Should().Be("dias");
        cut.Find(".metric-tile__description").TextContent.Should().Be("Até 16/12/2026");
    }
}

