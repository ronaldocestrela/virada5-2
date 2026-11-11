using Bunit;
using Microsoft.AspNetCore.Components;
using WebApp.Blazor.Components.DesignSystem.AppInputText;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppInputTextTests : BunitContext
{
    [Fact]
    public void Render_WhenLabelAndHelperProvided_ShouldAssociateLabelAndRenderHelperText()
    {
        // Act
        var cut = Render<AppInputText>(parameters => parameters
            .Add(p => p.Id, "company-name")
            .Add(p => p.Label, "Nome do estabelecimento")
            .Add(p => p.Placeholder, "Ex.: Bistrô Central")
            .Add(p => p.HelperText, "Usado na identificação da escala.")
            .Add(p => p.Required, true));

        // Assert
        var label = cut.Find("label");
        label.GetAttribute("for").Should().Be("company-name");
        label.TextContent.Should().Contain("Nome do estabelecimento");

        var input = cut.Find("input");
        input.Id.Should().Be("company-name");
        input.GetAttribute("placeholder").Should().Be("Ex.: Bistrô Central");
        input.HasAttribute("required").Should().BeTrue();
        cut.Find(".app-field__helper").TextContent.Trim().Should().Be("Usado na identificação da escala.");
    }

    [Fact]
    public void Input_WhenChanged_ShouldInvokeValueChangedCallback()
    {
        // Arrange
        string? latestValue = null;
        var cut = Render<AppInputText>(parameters => parameters
            .Add(p => p.Id, "email")
            .Add(p => p.Value, string.Empty)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => latestValue = v)));

        // Act
        cut.Find("input").Change("gestor@casa.com.br");

        // Assert
        latestValue.Should().Be("gestor@casa.com.br");
    }

    [Fact]
    public void Render_WhenErrorMessageProvided_ShouldMarkAriaInvalidAndDisplayErrorMessage()
    {
        // Act
        var cut = Render<AppInputText>(parameters => parameters
            .Add(p => p.Id, "salary")
            .Add(p => p.ErrorMessage, "Informe um salário válido."));

        // Assert
        var input = cut.Find("input");
        input.GetAttribute("aria-invalid").Should().Be("true");
        input.GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();
        cut.Find(".app-field__error").TextContent.Trim().Should().Be("Informe um salário válido.");
    }
}

