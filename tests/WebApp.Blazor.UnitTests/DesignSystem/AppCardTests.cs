using Bunit;
using Microsoft.AspNetCore.Components.Web;
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

    [Theory]
    [InlineData(AppCardBorder.None, "app-card--border-none")]
    [InlineData(AppCardBorder.Subtle, "app-card--border-subtle")]
    [InlineData(AppCardBorder.Default, "app-card--border-default")]
    [InlineData(AppCardBorder.Strong, "app-card--border-strong")]
    [InlineData(AppCardBorder.Primary, "app-card--border-primary")]
    [InlineData(AppCardBorder.Success, "app-card--border-success")]
    [InlineData(AppCardBorder.Warning, "app-card--border-warning")]
    [InlineData(AppCardBorder.Danger, "app-card--border-danger")]
    public void Render_WhenBorderTokenProvided_ShouldApplyExpectedBorderClass(
        AppCardBorder border,
        string expectedBorderClass)
    {
        // Act
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Border, border)
            .AddChildContent("Conteúdo"));

        // Assert
        cut.Find(".app-card").ClassList.Should().Contain(expectedBorderClass);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void KeyDown_WhenInteractiveAndActivationKeyPressed_ShouldInvokeOnClick(string key)
    {
        // Arrange
        var clicked = false;
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Interactive, true)
            .Add(p => p.OnClick, () => clicked = true)
            .AddChildContent("Card interativo"));

        // Act
        cut.Find(".app-card").KeyDown(new KeyboardEventArgs { Key = key });

        // Assert
        clicked.Should().BeTrue();
    }

    [Fact]
    public void KeyDown_WhenNotInteractive_ShouldNotInvokeOnClick()
    {
        // Arrange
        var clicked = false;
        var cut = Render<AppCard>(parameters => parameters
            .Add(p => p.Interactive, false)
            .Add(p => p.OnClick, () => clicked = true)
            .AddChildContent("Card estático"));

        // Act
        cut.Find(".app-card").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        // Assert
        clicked.Should().BeFalse();
    }
}

