using Domain.Common;

namespace WebApp.Blazor.Services;

public enum AppToastSeverity
{
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4
}

public sealed record ToastMessage(
    Guid Id,
    string Title,
    string Message,
    AppToastSeverity Severity,
    int DurationMs,
    DateTime CreatedAtUtc);

public interface IToastService
{
    IReadOnlyList<ToastMessage> Messages { get; }
    event Action? OnChange;

    Guid Show(string title, string message, AppToastSeverity severity = AppToastSeverity.Info, int durationMs = 5000);
    Guid? ShowFromResult(Result result, string? successTitle = null, string? successMessage = null);
    void Dismiss(Guid id);
    void Clear();
}

