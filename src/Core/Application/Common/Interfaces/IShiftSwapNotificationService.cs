namespace Application.Common.Interfaces;

/// <summary>
/// Canal de notificação in-process para atualização reativa de pendências de troca de turno por tenant.
/// </summary>
public interface IShiftSwapNotificationService
{
    event Action<Guid>? OnTenantSwapsChanged;

    void NotifyTenantSwapsChanged(Guid tenantId);
}

