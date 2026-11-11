using Application.DTOs.Rosters;

namespace Application.Common.Interfaces;

/// <summary>
/// Serviço para emissão de links tokenizados de compartilhamento de escala semanal.
/// </summary>
public interface IRosterShareLinkService
{
    Result<RosterShareLinkDto> CreateShareLink(
        Guid tenantId,
        Guid companyId,
        Guid rosterId,
        DateOnly weekStartDate,
        TimeSpan? lifetime = null);
}

