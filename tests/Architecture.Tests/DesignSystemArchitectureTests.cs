using System.Reflection;
using System.Text.RegularExpressions;
using WebApp.Blazor.Components;

namespace Architecture.Tests;

public partial class DesignSystemArchitectureTests
{
    private static readonly Assembly PresentationAssembly = typeof(App).Assembly;

    [Fact]
    public void WebAppBlazor_WhenInspected_ShouldNotReferenceThirdPartyUiComponentLibraries()
    {
        // Arrange
        var forbiddenUiLibraries = new[]
        {
            "MudBlazor",
            "Radzen",
            "Radzen.Blazor",
            "AntDesign",
            "Blazorise",
            "Syncfusion",
            "Telerik",
            "MatBlazor",
            "Blazored"
        };

        // Act
        var referencedAssemblies = PresentationAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        // Assert
        foreach (var forbidden in forbiddenUiLibraries)
        {
            referencedAssemblies.Should().NotContain(
                name => name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase),
                $"o agents.md proíbe bibliotecas visuais externas ({forbidden}) no Blazor");
        }
    }

    [Fact]
    public void DesignTokensFile_WhenInspected_ShouldExistAndDeclareRequiredBaselineTokens()
    {
        // Arrange
        var webRootCss = Path.Combine(ResolvePresentationRoot(), "wwwroot", "css", "tokens.css");
        var requiredTokens = new[]
        {
            "--color-primary",
            "--color-primary-hover",
            "--color-surface-bg",
            "--color-surface-card",
            "--color-text-primary",
            "--color-text-secondary",
            "--color-border",
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
            "--shadow-md"
        };

        // Act
        File.Exists(webRootCss).Should().BeTrue("tokens.css deve existir em wwwroot/css/tokens.css");
        var content = File.ReadAllText(webRootCss);
        var declaredTokens = ExtractDeclaredTokens(content);

        // Assert
        foreach (var required in requiredTokens)
        {
            declaredTokens.Should().Contain(required, $"token obrigatório {required} deve estar declarado em tokens.css");
        }
    }

    [Fact]
    public void DesignSystemComponents_WhenInspected_ShouldFollowFolderConventionAndUseOnlyDeclaredTokens()
    {
        // Arrange
        var presentationRoot = ResolvePresentationRoot();
        var tokensPath = Path.Combine(presentationRoot, "wwwroot", "css", "tokens.css");
        var designSystemRoot = Path.Combine(presentationRoot, "Components", "DesignSystem");

        File.Exists(tokensPath).Should().BeTrue("tokens.css deve existir antes de validar os componentes");
        Directory.Exists(designSystemRoot).Should().BeTrue("a pasta Components/DesignSystem deve existir");

        var declaredTokens = ExtractDeclaredTokens(File.ReadAllText(tokensPath));
        var componentDirectories = Directory.GetDirectories(designSystemRoot);
        componentDirectories.Should().NotBeEmpty("o Design System deve conter os componentes da Fase 4");

        // Act & Assert
        foreach (var componentDir in componentDirectories)
        {
            var componentName = Path.GetFileName(componentDir);
            var razorFile = Path.Combine(componentDir, $"{componentName}.razor");
            var cssFile = Path.Combine(componentDir, $"{componentName}.razor.css");

            File.Exists(razorFile).Should().BeTrue($"o componente {componentName} deve ter {componentName}.razor");
            File.Exists(cssFile).Should().BeTrue($"o componente {componentName} deve ter CSS isolado {componentName}.razor.css");

            var razorContent = File.ReadAllText(razorFile);
            InlineStyleAttributeRegex().IsMatch(razorContent).Should().BeFalse(
                $"estilos inline são proibidos em {componentName}.razor");

            ValidateCssFileUsesOnlyDeclaredTokens(cssFile, componentName, declaredTokens);
        }
    }

    [Fact]
    public void LayoutStylesheets_WhenInspected_ShouldUseOnlyDeclaredTokensAndNoHardcodedColors()
    {
        // Arrange
        var presentationRoot = ResolvePresentationRoot();
        var tokensPath = Path.Combine(presentationRoot, "wwwroot", "css", "tokens.css");
        var layoutRoot = Path.Combine(presentationRoot, "Components", "Layout");
        var declaredTokens = ExtractDeclaredTokens(File.ReadAllText(tokensPath));
        var layoutCssFiles = Directory.GetFiles(layoutRoot, "*.razor.css", SearchOption.AllDirectories);

        layoutCssFiles.Should().NotBeEmpty();

        // Act & Assert
        foreach (var cssFile in layoutCssFiles)
        {
            ValidateCssFileUsesOnlyDeclaredTokens(cssFile, Path.GetFileName(cssFile), declaredTokens);
        }
    }

    [Fact]
    public void PageComponents_WhenInspected_ShouldUseOnlyDeclaredTokensAndNoInlineStyles()
    {
        // Arrange
        var presentationRoot = ResolvePresentationRoot();
        var tokensPath = Path.Combine(presentationRoot, "wwwroot", "css", "tokens.css");
        var pagesRoot = Path.Combine(presentationRoot, "Components", "Pages");
        var declaredTokens = ExtractDeclaredTokens(File.ReadAllText(tokensPath));

        var razorFiles = Directory.GetFiles(pagesRoot, "*.razor", SearchOption.AllDirectories);
        var cssFiles = Directory.GetFiles(pagesRoot, "*.razor.css", SearchOption.AllDirectories);

        razorFiles.Should().NotBeEmpty();

        // Act & Assert
        foreach (var razorFile in razorFiles)
        {
            var razorContent = File.ReadAllText(razorFile);
            InlineStyleAttributeRegex().IsMatch(razorContent).Should().BeFalse(
                $"estilos inline são proibidos em {Path.GetFileName(razorFile)}");
        }

        foreach (var cssFile in cssFiles)
        {
            ValidateCssFileUsesOnlyDeclaredTokens(cssFile, Path.GetFileName(cssFile), declaredTokens);
        }
    }

    private static void ValidateCssFileUsesOnlyDeclaredTokens(
        string cssFile,
        string sourceName,
        HashSet<string> declaredTokens)
    {
        var cssContent = StripCssComments(File.ReadAllText(cssFile));
        HexColorRegex().IsMatch(cssContent).Should().BeFalse(
            $"cores hex literais são proibidas em {sourceName}");
        FunctionalColorRegex().IsMatch(cssContent).Should().BeFalse(
            $"funções rgb/rgba/hsl literais são proibidas em {sourceName}");
        NamedColorValueRegex().IsMatch(cssContent).Should().BeFalse(
            $"nomes de cores literais são proibidos em {sourceName}; use tokens de tokens.css");

        var referencedVariables = CssVariableReferenceRegex()
            .Matches(cssContent)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        foreach (var variableName in referencedVariables)
        {
            declaredTokens.Should().Contain(
                variableName,
                $"a variável {variableName} usada em {sourceName} deve estar declarada em tokens.css");
        }
    }

    private static string ResolvePresentationRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Presentation", "WebApp.Blazor");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar src/Presentation/WebApp.Blazor a partir do diretório de execução.");
    }

    private static HashSet<string> ExtractDeclaredTokens(string cssContent) =>
        CssTokenDeclarationRegex()
            .Matches(cssContent)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string StripCssComments(string css) =>
        CssCommentRegex().Replace(css, string.Empty);

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssCommentRegex();

    [GeneratedRegex(@"(--[a-z0-9-]+)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex CssTokenDeclarationRegex();

    [GeneratedRegex(@"var\(\s*(--[a-z0-9-]+)\s*(?:,[^)]+)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssVariableReferenceRegex();

    [GeneratedRegex(@"#[0-9a-f]{3,8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"\b(?:rgb|rgba|hsl|hsla)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionalColorRegex();

    [GeneratedRegex(@":[^;]*\b(?:white|black|red|green|blue|yellow|gray|grey|orange|purple|pink|lightyellow)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NamedColorValueRegex();

    [GeneratedRegex(@"\bstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleAttributeRegex();
}

