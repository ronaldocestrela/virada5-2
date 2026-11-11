using Domain.Common;

namespace WebApp.Blazor.Services;

public sealed class ToastService : IToastService
{
    private readonly Lock _sync = new();
    private readonly List<ToastMessage> _messages = [];

    public IReadOnlyList<ToastMessage> Messages
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToList();
            }
        }
    }

    public event Action? OnChange;

    public Guid Show(
        string title,
        string message,
        AppToastSeverity severity = AppToastSeverity.Info,
        int durationMs = 5000)
    {
        var toast = new ToastMessage(
            Guid.NewGuid(),
            title,
            message,
            severity,
            durationMs,
            DateTime.UtcNow);

        lock (_sync)
        {
            _messages.Add(toast);
        }

        OnChange?.Invoke();
        return toast.Id;
    }

    public Guid? ShowFromResult(Result result, string? successTitle = null, string? successMessage = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return Show(
                "Não foi possível concluir",
                result.Error.Message,
                AppToastSeverity.Error);
        }

        if (string.IsNullOrWhiteSpace(successTitle) && string.IsNullOrWhiteSpace(successMessage))
        {
            return null;
        }

        return Show(
            successTitle ?? "Operação concluída",
            successMessage ?? string.Empty,
            AppToastSeverity.Success);
    }

    public void Dismiss(Guid id)
    {
        var removed = false;
        lock (_sync)
        {
            removed = _messages.RemoveAll(m => m.Id == id) > 0;
        }

        if (removed)
        {
            OnChange?.Invoke();
        }
    }

    public void Clear()
    {
        var hadMessages = false;
        lock (_sync)
        {
            hadMessages = _messages.Count > 0;
            _messages.Clear();
        }

        if (hadMessages)
        {
            OnChange?.Invoke();
        }
    }
}
