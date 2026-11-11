using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Contrato de repositório específico para a agregação <see cref="DemandSlot"/>.
/// </summary>
public interface IDemandSlotRepository
{
    Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DemandSlot?> GetByCompanyDayAndHourAsync(Guid companyId, DayOfWeek dayOfWeek, int hour, CancellationToken ct = default);
    Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default);
    void Update(DemandSlot demandSlot);
    void RemoveRange(IEnumerable<DemandSlot> demandSlots);
}

