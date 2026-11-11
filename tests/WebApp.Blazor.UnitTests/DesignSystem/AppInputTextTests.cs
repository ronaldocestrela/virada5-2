using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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

    [Fact]
    public void Input_WhenImmediateIsFalse_ShouldNotInvokeValueChangedOnInputEvent()
    {
        // Arrange
        var callCount = 0;
        var cut = Render<AppInputText>(parameters => parameters
            .Add(p => p.Id, "search")
            .Add(p => p.Immediate, false)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, _ => callCount++)));

        // Act
        cut.Find("input").Input("Cozinha");

        // Assert
        callCount.Should().Be(0);
    }

    [Fact]
    public void Input_WhenImmediateIsTrue_ShouldInvokeValueChangedOnInputEvent()
    {
        // Arrange
        string? latest = null;
        var cut = Render<AppInputText>(parameters => parameters
            .Add(p => p.Id, "search")
            .Add(p => p.Immediate, true)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => latest = v)));

        // Act
        cut.Find("input").Input("Cozinha");

        // Assert
        latest.Should().Be("Cozinha");
    }

    [Fact]
    public void EditContext_WhenValidationMessageExistsAndValueChanges_ShouldDisplayErrorAndNotifyFieldChanged()
    {
        // Arrange
        var model = new TestInputModel();
        var editContext = new EditContext(model);
        var messages = new ValidationMessageStore(editContext);
        var field = FieldIdentifier.Create(() => model.CompanyName);
        messages.Add(field, "Nome obrigatório pelo EditContext.");

        FieldIdentifier? notifiedField = null;
        editContext.OnFieldChanged += (_, args) => notifiedField = args.FieldIdentifier;

        var cut = Render<CascadingValue<EditContext>>(cascading => cascading
            .Add(p => p.Value, editContext)
            .AddChildContent<AppInputText>(parameters => parameters
                .Add(p => p.Id, "company")
                .Add(p => p.Value, model.CompanyName)
                .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => model.CompanyName = v))
                .Add(p => p.ValueExpression, () => model.CompanyName)));

        // Assert initial validation message from EditContext
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".app-field__error").TextContent.Trim().Should().Be("Nome obrigatório pelo EditContext.");

        // Act
        cut.Find("input").Change("Padaria Aurora");

        // Assert field notification
        notifiedField.Should().Be(field);
    }

    private sealed class TestInputModel
    {
        public string? CompanyName { get; set; }
    }
}

