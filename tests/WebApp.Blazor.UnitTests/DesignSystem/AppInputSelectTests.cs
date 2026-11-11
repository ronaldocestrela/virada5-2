using Bunit;
using Domain.Enums;
using Microsoft.AspNetCore.Components;
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
}

