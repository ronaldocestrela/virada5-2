using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Implementação EF Core do repositório específico da agregação <see cref="Roster"/> e seus turnos (<see cref="Shift"/>).
/// </summary>
public sealed class RosterRepository : IRosterRepository
{
    private readonly AppDbContext _dbContext;

    public RosterRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Rosters
            .Include(roster => roster.Shifts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(roster => roster.Id == id, ct);
    }

    public async Task<Roster?> GetByCompanyAndWeekStartAsync(
        Guid companyId,
        DateOnly weekStartDate,
        CancellationToken ct = default)
    {
        if (companyId == Guid.Empty || weekStartDate == default)
        {
            return null;
        }

        return await _dbContext.Rosters
            .Include(roster => roster.Shifts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                roster => roster.CompanyId == companyId &&
                          roster.WeekStartDate == weekStartDate &&
                          roster.Status != RosterStatus.Archived,
                ct);
    }

    public async Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(
        Guid companyId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (companyId == Guid.Empty ||
            startDate == default ||
            endDate == default ||
            startDate > endDate)
        {
            return [];
        }

        return await _dbContext.Rosters
            .AsNoTracking()
            .Include(roster => roster.Shifts)
            .AsSplitQuery()
            .Where(roster =>
                roster.CompanyId == companyId &&
                roster.Status != RosterStatus.Archived &&
                roster.WeekStartDate <= endDate &&
                roster.WeekEndDate >= startDate)
            .OrderBy(roster => roster.WeekStartDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Roster roster, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roster);
        await _dbContext.Rosters.AddAsync(roster, ct);
    }

    public void Update(Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);

        if (_dbContext.Entry(roster).State == EntityState.Detached)
        {
            _dbContext.Rosters.Update(roster);
        }
    }
}

