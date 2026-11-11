using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WebApp.Blazor.Components.DesignSystem.AppModal;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppModalTests : BunitContext
{
    [Fact]
    public void Render_WhenIsOpenIsFalse_ShouldNotRenderDialogMarkup()
    {
        // Act
        var cut = Render<AppModal>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.Title, "Confirmar Troca")
            .AddChildContent("<p>Corpo</p>"));

        // Assert
        cut.FindAll("[role='dialog']").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenIsOpenIsTrue_ShouldRenderAccessibleDialogWithTitleBodyAndFooter()
    {
        // Act
        var cut = Render<AppModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Confirmar Troca de Turno")
            .Add(p => p.Size, AppModalSize.Large)
            .AddChildContent("<p>Detalhes da troca</p>")
            .Add(p => p.FooterContent, "<button type=\"button\">Aprovar</button>"));

        // Assert
        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        dialog.ClassList.Should().Contain("app-modal__dialog--lg");
        cut.Find(".app-modal__title").TextContent.Trim().Should().Be("Confirmar Troca de Turno");
        cut.Find(".app-modal__body").InnerHtml.Should().Contain("Detalhes da troca");
        cut.Find(".app-modal__footer").InnerHtml.Should().Contain("Aprovar");
    }

    [Fact]
    public void CloseInteractions_WhenCloseButtonBackdropOrEscapeTriggered_ShouldCloseAndNotify()
    {
        // Arrange
        var isOpen = true;
        var closeCount = 0;
        var cut = Render<AppModal>(parameters => parameters
            .Add(p => p.IsOpen, isOpen)
            .Add(p => p.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .Add(p => p.OnClose, () => closeCount++)
            .Add(p => p.CloseOnBackdropClick, true)
            .Add(p => p.CloseOnEscape, true)
            .Add(p => p.Title, "Modal"));

        // Act 1: Close button
        cut.Find(".app-modal__close").Click();
        isOpen.Should().BeFalse();
        closeCount.Should().Be(1);

        // Act 2: Re-render open and click backdrop
        cut.Render(parameters => parameters.Add(p => p.IsOpen, true));
        isOpen = true;
        cut.Find(".app-modal__backdrop").Click();
        isOpen.Should().BeFalse();
        closeCount.Should().Be(2);

        // Act 3: Re-render open and press Escape
        cut.Render(parameters => parameters.Add(p => p.IsOpen, true));
        isOpen = true;
        cut.Find(".app-modal__backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        isOpen.Should().BeFalse();
        closeCount.Should().Be(3);
    }

    [Fact]
    public void CloseInteractions_WhenCloseFlagsDisabled_ShouldNotCloseOnBackdropOrEscapeAndHideCloseButton()
    {
        // Arrange
        var isOpen = true;
        var closeCount = 0;
        var cut = Render<AppModal>(parameters => parameters
            .Add(p => p.IsOpen, isOpen)
            .Add(p => p.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .Add(p => p.OnClose, () => closeCount++)
            .Add(p => p.CloseOnBackdropClick, false)
            .Add(p => p.CloseOnEscape, false)
            .Add(p => p.ShowCloseButton, false)
            .Add(p => p.Title, "Modal bloqueado"));

        // Act
        cut.Find(".app-modal__backdrop").Click();
        cut.Find(".app-modal__backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // Assert
        isOpen.Should().BeTrue();
        closeCount.Should().Be(0);
        cut.FindAll(".app-modal__close").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenAdditionalAttributesAndCustomCloseLabelProvided_ShouldApplyToDialogAndCloseButton()
    {
        // Act
        var cut = Render<AppModal>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.Title, "Detalhes")
            .Add(p => p.CloseButtonAriaLabel, "Cancelar diálogo")
            .AddUnmatched("data-testid", "swap-modal")
            .AddUnmatched("aria-describedby", "modal-desc")
            .AddChildContent("<p id=\"modal-desc\">Texto</p>"));

        // Assert
        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("data-testid").Should().Be("swap-modal");
        dialog.GetAttribute("aria-describedby").Should().Be("modal-desc");
        cut.Find(".app-modal__close").GetAttribute("aria-label").Should().Be("Cancelar diálogo");
    }
}

