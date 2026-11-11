using System.Text.RegularExpressions;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public partial class DesignTokensTests
{
    [Fact]
    public void TokensCss_WhenLoaded_ShouldDeclareAllSemanticGroupsAndBaselineTokens()
    {
        // Arrange
        var tokensPath = ResolveTokensCssPath();
        File.Exists(tokensPath).Should().BeTrue("tokens.css deve existir em wwwroot/css/tokens.css");
        var css = File.ReadAllText(tokensPath);

        var expectedTokens = new[]
        {
            // Baseline agents.md
            "--color-primary",
            "--color-primary-hover",
            "--color-surface-bg",
            "--color-surface-card",
            "--color-text-primary",
            "--color-text-secondary",
            "--color-border",
            "--color-border-subtle",
            "--color-border-strong",
            "--color-danger",
            "--color-warning",
            "--color-success",
            "--space-1",
            "--space-2",
            "--space-3",
            "--space-4",
            "--space-6",
            "--space-8",
            "--font-family-base",
            "--font-size-xs",
            "--font-size-sm",
            "--font-size-base",
            "--font-size-lg",
            "--font-size-xl",
            "--font-weight-regular",
            "--font-weight-medium",
            "--font-weight-semibold",
            "--radius-sm",
            "--radius-md",
            "--radius-lg",
            "--shadow-sm",
            "--shadow-md",

            // CLT & Roster 5x2 semantics
            "--color-clt-compliant",
            "--color-clt-compliant-bg",
            "--color-clt-warning",
            "--color-clt-warning-bg",
            "--color-clt-violation",
            "--color-clt-violation-bg",
            "--color-shift-primary",
            "--color-shift-primary-bg",
            "--color-shift-polyvalence",
            "--color-shift-polyvalence-bg",
            "--color-day-off",
            "--color-day-off-bg",

            // Demand Heatmap semantics
            "--color-heatmap-closed-bg",
            "--color-heatmap-closed-text",
            "--color-heatmap-idle-bg",
            "--color-heatmap-idle-text",
            "--color-heatmap-low-bg",
            "--color-heatmap-low-text",
            "--color-heatmap-moderate-bg",
            "--color-heatmap-moderate-text",
            "--color-heatmap-peak-bg",
            "--color-heatmap-peak-text",

            // Motion & Layering
            "--transition-fast",
            "--transition-base",
            "--z-modal",
            "--z-toast"
        };

        // Act
        var declared = TokenDeclarationRegex()
            .Matches(css)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Assert
        css.Should().Contain(":root");
        foreach (var token in expectedTokens)
        {
            declared.Should().Contain(token, $"o token {token} é obrigatório no Design System");
        }
    }

    private static string ResolveTokensCssPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Presentation", "WebApp.Blazor", "wwwroot", "css", "tokens.css");
            var presentationDir = Path.Combine(current.FullName, "src", "Presentation", "WebApp.Blazor");
            if (Directory.Exists(presentationDir))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar src/Presentation/WebApp.Blazor.");
    }

    [GeneratedRegex(@"(--[a-z0-9-]+)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex TokenDeclarationRegex();
}

