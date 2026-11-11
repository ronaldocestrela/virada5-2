using Domain.Common;

namespace WebApp.Blazor.Services;

public sealed class ToastService : IToastService, IDisposable
{
    private readonly Lock _sync = new();
    private readonly List<ToastMessage> _messages = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _dismissTimers = [];
    private bool _disposed;

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

        CancellationTokenSource? timerCts = null;

        lock (_sync)
        {
            _messages.Add(toast);
            if (durationMs > 0 && !_disposed)
            {
                timerCts = new CancellationTokenSource();
                _dismissTimers[toast.Id] = timerCts;
            }
        }

        OnChange?.Invoke();

        if (timerCts is not null)
        {
            _ = AutoDismissAsync(toast.Id, durationMs, timerCts.Token);
        }

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
        CancellationTokenSource? timerToCancel = null;

        lock (_sync)
        {
            removed = _messages.RemoveAll(m => m.Id == id) > 0;
            if (_dismissTimers.Remove(id, out var cts))
            {
                timerToCancel = cts;
            }
        }

        CancelAndDisposeTimer(timerToCancel);

        if (removed)
        {
            OnChange?.Invoke();
        }
    }

    public void Clear()
    {
        var hadMessages = false;
        List<CancellationTokenSource> timersToCancel;

        lock (_sync)
        {
            hadMessages = _messages.Count > 0;
            _messages.Clear();
            timersToCancel = _dismissTimers.Values.ToList();
            _dismissTimers.Clear();
        }

        foreach (var cts in timersToCancel)
        {
            CancelAndDisposeTimer(cts);
        }

        if (hadMessages)
        {
            OnChange?.Invoke();
        }
    }

    public void Dispose()
    {
        List<CancellationTokenSource> timersToCancel;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timersToCancel = _dismissTimers.Values.ToList();
            _dismissTimers.Clear();
        }

        foreach (var cts in timersToCancel)
        {
            CancelAndDisposeTimer(cts);
        }
    }

    private async Task AutoDismissAsync(Guid id, int durationMs, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(durationMs, cancellationToken).ConfigureAwait(false);
            Dismiss(id);
        }
        catch (OperationCanceledException)
        {
            // Cancelado por Dismiss manual, Clear ou Dispose.
        }
    }

    private static void CancelAndDisposeTimer(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Já descartado.
        }
        finally
        {
            cts.Dispose();
        }
    }
}
