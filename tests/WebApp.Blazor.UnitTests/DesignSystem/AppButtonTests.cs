using Bunit;
using Microsoft.AspNetCore.Components.Web;
using WebApp.Blazor.Components.DesignSystem.AppButton;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppButtonTests : BunitContext
{
    [Theory]
    [InlineData(AppButtonVariant.Primary, "app-btn--primary")]
    [InlineData(AppButtonVariant.Secondary, "app-btn--secondary")]
    [InlineData(AppButtonVariant.Ghost, "app-btn--ghost")]
    [InlineData(AppButtonVariant.Danger, "app-btn--danger")]
    public void Render_WhenVariantProvided_ShouldApplySemanticVariantClassAndChildContent(
        AppButtonVariant variant,
        string expectedCssClass)
    {
        // Act
        var cut = Render<AppButton>(parameters => parameters
            .Add(p => p.Variant, variant)
            .AddChildContent("Salvar"));

        // Assert
        var button = cut.Find("button");
        button.ClassList.Should().Contain("app-btn");
        button.ClassList.Should().Contain(expectedCssClass);
        button.TextContent.Trim().Should().Be("Salvar");
        button.GetAttribute("type").Should().Be("button");
    }

    [Theory]
    [InlineData(AppButtonSize.Small, "app-btn--sm")]
    [InlineData(AppButtonSize.Medium, "app-btn--md")]
    [InlineData(AppButtonSize.Large, "app-btn--lg")]
    public void Render_WhenSizeAndFullWidthProvided_ShouldApplyLayoutClasses(
        AppButtonSize size,
        string expectedSizeClass)
    {
        // Act
        var cut = Render<AppButton>(parameters => parameters
            .Add(p => p.Size, size)
            .Add(p => p.FullWidth, true)
            .Add(p => p.Type, "submit")
            .AddChildContent("Continuar"));

        // Assert
        var button = cut.Find("button");
        button.ClassList.Should().Contain(expectedSizeClass);
        button.ClassList.Should().Contain("app-btn--full");
        button.GetAttribute("type").Should().Be("submit");
    }

    [Fact]
    public void Click_WhenEnabled_ShouldInvokeOnClickCallback()
    {
        // Arrange
        var clicked = false;
        var cut = Render<AppButton>(parameters => parameters
            .Add(p => p.OnClick, (MouseEventArgs _) => clicked = true)
            .AddChildContent("Confirmar"));

        // Act
        cut.Find("button").Click();

        // Assert
        clicked.Should().BeTrue();
    }

    [Fact]
    public void Click_WhenDisabledOrLoading_ShouldDisableButtonAndNotInvokeOnClick()
    {
        // Arrange
        var clickCount = 0;
        var disabledCut = Render<AppButton>(parameters => parameters
            .Add(p => p.Disabled, true)
            .Add(p => p.OnClick, (MouseEventArgs _) => clickCount++)
            .AddChildContent("Desabilitado"));

        var loadingCut = Render<AppButton>(parameters => parameters
            .Add(p => p.Loading, true)
            .Add(p => p.AriaLabel, "Processando")
            .Add(p => p.OnClick, (MouseEventArgs _) => clickCount++)
            .AddChildContent("Salvando"));

        // Act
        disabledCut.Find("button").Click();
        loadingCut.Find("button").Click();

        // Assert
        clickCount.Should().Be(0);
        disabledCut.Find("button").HasAttribute("disabled").Should().BeTrue();

        var loadingBtn = loadingCut.Find("button");
        loadingBtn.HasAttribute("disabled").Should().BeTrue();
        loadingBtn.GetAttribute("aria-busy").Should().Be("true");
        loadingBtn.GetAttribute("aria-label").Should().Be("Processando");
        loadingCut.FindAll(".app-btn__spinner").Should().ContainSingle();
    }

    [Fact]
    public void Render_WhenAdditionalAttributesAndResetTypeProvided_ShouldForwardToButtonElement()
    {
        // Act
        var cut = Render<AppButton>(parameters => parameters
            .Add(p => p.Type, "reset")
            .AddUnmatched("data-testid", "reset-btn")
            .AddChildContent("Limpar"));

        // Assert
        var button = cut.Find("button");
        button.GetAttribute("type").Should().Be("reset");
        button.GetAttribute("data-testid").Should().Be("reset-btn");
    }
}

