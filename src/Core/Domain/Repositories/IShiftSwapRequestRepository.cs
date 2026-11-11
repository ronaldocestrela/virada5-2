using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Contrato de repositório específico para a agregação <see cref="ShiftSwapRequest"/>.
/// </summary>
public interface IShiftSwapRequestRepository
{
    Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default);
    Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default);
    Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default);
    void Update(ShiftSwapRequest request);
}

