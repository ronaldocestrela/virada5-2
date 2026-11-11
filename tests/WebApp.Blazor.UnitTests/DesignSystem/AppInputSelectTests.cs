using Bunit;
using Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using WebApp.Blazor.Components.DesignSystem.AppInputSelect;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppInputSelectTests : BunitContext
{
    [Fact]
    public void Render_WhenOptionsAndPlaceholderProvided_ShouldRenderSelectWithAllOptions()
    {
        // Arrange
        var options = new[]
        {
            new SelectOption<StationRole>(StationRole.Kitchen, "Cozinha"),
            new SelectOption<StationRole>(StationRole.Waiter, "Salão / Garçom"),
            new SelectOption<StationRole>(StationRole.Bar, "Bar", Disabled: true)
        };

        // Act
        var cut = Render<AppInputSelect<StationRole>>(parameters => parameters
            .Add(p => p.Id, "station-role")
            .Add(p => p.Label, "Função Principal")
            .Add(p => p.PlaceholderOption, "Selecione uma função...")
            .Add(p => p.Options, options));

        // Assert
        var label = cut.Find("label");
        label.GetAttribute("for").Should().Be("station-role");

        var optionElements = cut.FindAll("select option");
        optionElements.Should().HaveCount(4);
        optionElements[0].TextContent.Trim().Should().Be("Selecione uma função...");
        optionElements[1].TextContent.Trim().Should().Be("Cozinha");
        optionElements[3].HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Change_WhenOptionSelected_ShouldInvokeValueChangedWithTypedEnum()
    {
        // Arrange
        var options = new[]
        {
            new SelectOption<StationRole>(StationRole.Kitchen, "Cozinha"),
            new SelectOption<StationRole>(StationRole.Waiter, "Salão / Garçom")
        };

        StationRole? selected = null;
        var cut = Render<AppInputSelect<StationRole>>(parameters => parameters
            .Add(p => p.Id, "station-role")
            .Add(p => p.Options, options)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<StationRole>(this, v => selected = v)));

        // Act
        cut.Find("select").Change(StationRole.Waiter.ToString());

        // Assert
        selected.Should().Be(StationRole.Waiter);
    }

    [Fact]
    public void Render_WhenErrorMessageProvided_ShouldApplyAriaInvalidAndShowError()
    {
        // Act
        var cut = Render<AppInputSelect<string>>(parameters => parameters
            .Add(p => p.Id, "model")
            .Add(p => p.ErrorMessage, "Campo obrigatório."));

        // Assert
        var select = cut.Find("select");
        select.GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".app-field__error").TextContent.Trim().Should().Be("Campo obrigatório.");
    }

    [Fact]
    public void Change_WhenPlaceholderSelectedForNullableEnum_ShouldInvokeValueChangedWithNull()
    {
        // Arrange
        var options = new[]
        {
            new SelectOption<StationRole?>(StationRole.Kitchen, "Cozinha")
        };

        StationRole? selected = StationRole.Kitchen;
        var cut = Render<AppInputSelect<StationRole?>>(parameters => parameters
            .Add(p => p.Id, "nullable-role")
            .Add(p => p.Value, selected)
            .Add(p => p.PlaceholderOption, "Sem função secundária")
            .Add(p => p.Options, options)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<StationRole?>(this, v => selected = v)));

        // Act
        cut.Find("select").Change(string.Empty);

        // Assert
        selected.Should().BeNull();
    }

    [Fact]
    public void Change_WhenGuidOptionSelected_ShouldInvokeValueChangedWithTypedGuid()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var options = new[]
        {
            new SelectOption<Guid>(targetId, "Turno Noite")
        };

        Guid selected = Guid.Empty;
        var cut = Render<AppInputSelect<Guid>>(parameters => parameters
            .Add(p => p.Id, "shift-id")
            .Add(p => p.Options, options)
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<Guid>(this, v => selected = v)));

        // Act
        cut.Find("select").Change(targetId.ToString());

        // Assert
        selected.Should().Be(targetId);
    }

    [Fact]
    public void EditContext_WhenValidationMessageExistsAndSelectionChanges_ShouldDisplayErrorAndNotifyFieldChanged()
    {
        // Arrange
        var model = new TestSelectModel();
        var editContext = new EditContext(model);
        var messages = new ValidationMessageStore(editContext);
        var field = FieldIdentifier.Create(() => model.Role);
        messages.Add(field, "Selecione uma função válida.");

        FieldIdentifier? notifiedField = null;
        editContext.OnFieldChanged += (_, args) => notifiedField = args.FieldIdentifier;

        var options = new[]
        {
            new SelectOption<StationRole?>(StationRole.Bar, "Bar")
        };

        var cut = Render<CascadingValue<EditContext>>(cascading => cascading
            .Add(p => p.Value, editContext)
            .AddChildContent<AppInputSelect<StationRole?>>(parameters => parameters
                .Add(p => p.Id, "role")
                .Add(p => p.Options, options)
                .Add(p => p.Value, model.Role)
                .Add(p => p.ValueChanged, EventCallback.Factory.Create<StationRole?>(this, v => model.Role = v))
                .Add(p => p.ValueExpression, () => model.Role)));

        // Assert initial validation message
        cut.Find("select").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".app-field__error").TextContent.Trim().Should().Be("Selecione uma função válida.");

        // Act
        cut.Find("select").Change(StationRole.Bar.ToString());

        // Assert
        notifiedField.Should().Be(field);
    }

    private sealed class TestSelectModel
    {
        public StationRole? Role { get; set; }
    }
}

