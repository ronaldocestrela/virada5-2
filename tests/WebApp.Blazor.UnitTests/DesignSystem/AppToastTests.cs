using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.DesignSystem.AppToast;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppToastTests : BunitContext
{
    [Fact]
    public void Render_WhenMessagesAddedAndDismissed_ShouldDisplayToastsAndRemoveOnCloseClick()
    {
        // Arrange
        var toastService = new ToastService();
        Services.AddSingleton<IToastService>(toastService);

        var cut = Render<AppToast>();

        // Act 1: Add toast via service
        toastService.Show("Atenção CLT", "Domingo consecutivo detectado.", AppToastSeverity.Warning, durationMs: 0);

        // Assert 1
        var toast = cut.Find(".app-toast__item");
        toast.ClassList.Should().Contain("app-toast__item--warning");
        toast.TextContent.Should().Contain("Atenção CLT");
        toast.TextContent.Should().Contain("Domingo consecutivo detectado.");

        // Act 2: Dismiss via close button
        cut.Find(".app-toast__close").Click();

        // Assert 2
        cut.FindAll(".app-toast__item").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenMultipleToastsWithDifferentSeveritiesExist_ShouldUseAppropriateAriaRoles()
    {
        // Arrange
        using var toastService = new ToastService();
        Services.AddSingleton<IToastService>(toastService);
        var cut = Render<AppToast>();

        // Act
        toastService.Show("Info", "Detalhe informativo", AppToastSeverity.Info, durationMs: 0);
        toastService.Show("Erro CLT", "Violação detectada", AppToastSeverity.Error, durationMs: 0);

        // Assert
        var items = cut.FindAll(".app-toast__item");
        items.Should().HaveCount(2);
        items[0].GetAttribute("role").Should().Be("status");
        items[1].GetAttribute("role").Should().Be("alert");
    }
}

