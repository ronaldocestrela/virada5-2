using Bunit;
using WebApp.Blazor.Components.DesignSystem.AppBadge;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppBadgeTests : BunitContext
{
    [Theory]
    [InlineData(AppBadgeVariant.Neutral, "app-badge--neutral")]
    [InlineData(AppBadgeVariant.Primary, "app-badge--primary")]
    [InlineData(AppBadgeVariant.Success, "app-badge--success")]
    [InlineData(AppBadgeVariant.Warning, "app-badge--warning")]
    [InlineData(AppBadgeVariant.Danger, "app-badge--danger")]
    [InlineData(AppBadgeVariant.Info, "app-badge--info")]
    public void Render_WhenVariantAndTextProvided_ShouldApplySemanticClassAndText(
        AppBadgeVariant variant,
        string expectedClass)
    {
        // Act
        var cut = Render<AppBadge>(parameters => parameters
            .Add(p => p.Variant, variant)
            .Add(p => p.Text, "Conforme CLT")
            .Add(p => p.Dot, true));

        // Assert
        var badge = cut.Find(".app-badge");
        badge.ClassList.Should().Contain(expectedClass);
        badge.TextContent.Trim().Should().Be("Conforme CLT");
        cut.FindAll(".app-badge__dot").Should().ContainSingle();
    }

    [Fact]
    public void Render_WhenChildContentProvidedWithoutText_ShouldRenderChildContent()
    {
        // Act
        var cut = Render<AppBadge>(parameters => parameters
            .Add(p => p.Variant, AppBadgeVariant.Info)
            .AddChildContent("Polivalência"));

        // Assert
        cut.Find(".app-badge").TextContent.Trim().Should().Be("Polivalência");
        cut.FindAll(".app-badge__dot").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenAriaLabelAndAdditionalAttributesProvided_ShouldForwardToSpanElement()
    {
        // Act
        var cut = Render<AppBadge>(parameters => parameters
            .Add(p => p.Text, "CLT OK")
            .Add(p => p.AriaLabel, "Status de conformidade CLT")
            .AddUnmatched("data-testid", "clt-badge"));

        // Assert
        var badge = cut.Find(".app-badge");
        badge.GetAttribute("aria-label").Should().Be("Status de conformidade CLT");
        badge.GetAttribute("data-testid").Should().Be("clt-badge");
    }
}

