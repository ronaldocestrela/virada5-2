using Domain.Common;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Services;

public class ToastServiceTests
{
    [Fact]
    public void ShowAndDismiss_WhenInvoked_ShouldManageMessagesAndRaiseOnChange()
    {
        // Arrange
        var service = new ToastService();
        var notifications = 0;
        service.OnChange += () => notifications++;

        // Act
        var id = service.Show("Escala salva", "Rascunho gerado com sucesso.", AppToastSeverity.Success);

        // Assert 1
        id.Should().NotBe(Guid.Empty);
        notifications.Should().Be(1);
        service.Messages.Should().ContainSingle(m =>
            m.Id == id &&
            m.Title == "Escala salva" &&
            m.Message == "Rascunho gerado com sucesso." &&
            m.Severity == AppToastSeverity.Success);

        // Act 2
        service.Dismiss(id);

        // Assert 2
        notifications.Should().Be(2);
        service.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ShowFromResult_WhenResultIsFailure_ShouldEnqueueErrorToastWithErrorCodeAndMessage()
    {
        // Arrange
        var service = new ToastService();
        var failure = Result.Failure(Error.Validation("Roster.CltViolation", "Intervalo interjornada inferior a 11h."));

        // Act
        var id = service.ShowFromResult(failure);

        // Assert
        id.Should().NotBeNull();
        service.Messages.Should().ContainSingle(m =>
            m.Severity == AppToastSeverity.Error &&
            m.Message == "Intervalo interjornada inferior a 11h.");
    }

    [Fact]
    public void ShowFromResult_WhenResultIsSuccessAndMessageProvided_ShouldEnqueueSuccessToast()
    {
        // Arrange
        var service = new ToastService();

        // Act
        var id = service.ShowFromResult(Result.Success(), "Troca aprovada", "A escala foi atualizada.");

        // Assert
        id.Should().NotBeNull();
        service.Messages.Should().ContainSingle(m =>
            m.Severity == AppToastSeverity.Success &&
            m.Title == "Troca aprovada" &&
            m.Message == "A escala foi atualizada.");
    }
}

