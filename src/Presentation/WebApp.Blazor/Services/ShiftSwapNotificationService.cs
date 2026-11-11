using Application.Common.Interfaces;

namespace WebApp.Blazor.Services;

/// <summary>
/// Implementação singleton in-process de notificação de trocas pendentes por tenant para circuitos Blazor Server.
/// </summary>
public sealed class ShiftSwapNotificationService : IShiftSwapNotificationService
{
    public event Action<Guid>? OnTenantSwapsChanged;

    public void NotifyTenantSwapsChanged(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        OnTenantSwapsChanged?.Invoke(tenantId);
    }
}

