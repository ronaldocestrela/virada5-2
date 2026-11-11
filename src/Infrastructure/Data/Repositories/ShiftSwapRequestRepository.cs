using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Implementação EF Core do repositório específico da agregação <see cref="ShiftSwapRequest"/>.
/// </summary>
public sealed class ShiftSwapRequestRepository : IShiftSwapRequestRepository
{
    private readonly AppDbContext _dbContext;

    public ShiftSwapRequestRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.ShiftSwapRequests
            .FirstOrDefaultAsync(request => request.Id == id, ct);
    }

    public async Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default)
    {
        if (rosterId == Guid.Empty)
        {
            return [];
        }

        return await _dbContext.ShiftSwapRequests
            .AsNoTracking()
            .Where(request => request.RosterId == rosterId)
            .OrderByDescending(request => request.RequestedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        if (companyId == Guid.Empty)
        {
            return [];
        }

        return await _dbContext.ShiftSwapRequests
            .AsNoTracking()
            .Where(request =>
                request.CompanyId == companyId &&
                (request.Status == ShiftSwapStatus.PendingPeerApproval ||
                 request.Status == ShiftSwapStatus.PendingManagerApproval))
            .OrderBy(request => request.RequestedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default)
    {
        if (shiftId == Guid.Empty)
        {
            return false;
        }

        return await _dbContext.ShiftSwapRequests
            .AsNoTracking()
            .AnyAsync(
                request =>
                    (request.Status == ShiftSwapStatus.PendingPeerApproval ||
                     request.Status == ShiftSwapStatus.PendingManagerApproval) &&
                    (request.RequesterShiftId == shiftId || request.TargetShiftId == shiftId),
                ct);
    }

    public async Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _dbContext.ShiftSwapRequests.AddAsync(request, ct);
    }

    public void Update(ShiftSwapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_dbContext.Entry(request).State == EntityState.Detached)
        {
            _dbContext.ShiftSwapRequests.Update(request);
        }
    }
}

