using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Contrato de repositório específico para a agregação <see cref="Roster"/> e seus turnos (<see cref="Shift"/>).
/// </summary>
public interface IRosterRepository
{
    Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default);
    Task<Roster?> GetByCompanyAndWeekStartAsync(Guid companyId, DateOnly weekStartDate, CancellationToken ct = default);
    Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(Guid companyId, DateOnly startDate, DateOnly endDate, CancellationToken ct = default);
    Task AddAsync(Roster roster, CancellationToken ct = default);
    void Update(Roster roster);
}

