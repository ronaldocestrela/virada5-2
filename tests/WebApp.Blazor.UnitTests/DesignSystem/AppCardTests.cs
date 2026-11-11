using Bunit;
using WebApp.Blazor.Components.DesignSystem.AppCard;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppCardTests : BunitContext
{
    [Theory]
    [InlineData(AppCardElevation.None, "app-card--elevation-none")]
    [InlineData(AppCardElevation.Low, "app-card--elevation-low")]
    [InlineData(AppCardElevation.Medium, "app-card--elevation-medium")]
    public void Render_WhenTitleSubtitleAndFooterProvided_ShouldRenderSectionsAndElevationClass(
        AppCardElevation elevation,
        string expectedElevationClass)
    {
        // Act
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Title, "Economia Projetada")
            .Add(p => p.Subtitle, "Comparativo 6x1 vs 5x2")
            .Add(p => p.Elevation, elevation)
            .Add(p => p.Padding, AppCardPadding.Spacious)
            .AddChildContent("<p>Conteúdo principal</p>")
            .Add(p => p.FooterContent, "<span>Rodapé</span>"));

        // Assert
        var card = cut.Find(".app-card");
        card.ClassList.Should().Contain(expectedElevationClass);
        card.ClassList.Should().Contain("app-card--padding-spacious");
        cut.Find(".app-card__title").TextContent.Trim().Should().Be("Economia Projetada");
        cut.Find(".app-card__subtitle").TextContent.Trim().Should().Be("Comparativo 6x1 vs 5x2");
        cut.Find(".app-card__body").InnerHtml.Should().Contain("Conteúdo principal");
        cut.Find(".app-card__footer").InnerHtml.Should().Contain("Rodapé");
    }

    [Fact]
    public void Click_WhenInteractive_ShouldInvokeOnClickAndApplyInteractiveClass()
    {
        // Arrange
        var clicked = false;
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Interactive, true)
            .Add(p => p.OnClick, () => clicked = true)
            .AddChildContent("Card clicável"));

        // Act
        var card = cut.Find(".app-card");
        card.Click();

        // Assert
        card.ClassList.Should().Contain("app-card--interactive");
        clicked.Should().BeTrue();
    }

    [Fact]
    public void Click_WhenNotInteractive_ShouldNotInvokeOnClick()
    {
        // Arrange
        var clicked = false;
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Interactive, false)
            .Add(p => p.OnClick, () => clicked = true)
            .AddChildContent("Card estático"));

        // Act
        cut.Find(".app-card").Click();

        // Assert
        clicked.Should().BeFalse();
    }
}

