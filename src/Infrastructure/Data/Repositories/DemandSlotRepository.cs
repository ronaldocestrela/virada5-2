using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Implementação EF Core do repositório específico da agregação <see cref="DemandSlot"/>.
/// </summary>
public sealed class DemandSlotRepository : IDemandSlotRepository
{
    private readonly AppDbContext _dbContext;

    public DemandSlotRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.DemandSlots
            .FirstOrDefaultAsync(slot => slot.Id == id, ct);
    }

    public async Task<DemandSlot?> GetByCompanyDayAndHourAsync(
        Guid companyId,
        DayOfWeek dayOfWeek,
        int hour,
        CancellationToken ct = default)
    {
        if (companyId == Guid.Empty ||
            !Enum.IsDefined(dayOfWeek) ||
            hour < DemandSlot.MinHour ||
            hour > DemandSlot.MaxHour)
        {
            return null;
        }

        return await _dbContext.DemandSlots
            .FirstOrDefaultAsync(
                slot => slot.CompanyId == companyId &&
                        slot.DayOfWeek == dayOfWeek &&
                        slot.Hour == hour,
                ct);
    }

    public async Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        if (companyId == Guid.Empty)
        {
            return [];
        }

        var slots = await _dbContext.DemandSlots
            .AsNoTracking()
            .Where(slot => slot.CompanyId == companyId)
            .ToListAsync(ct);

        return slots
            .OrderBy(slot => slot.DayOfWeek)
            .ThenBy(slot => slot.Hour)
            .ToList();
    }

    public async Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(demandSlot);
        await _dbContext.DemandSlots.AddAsync(demandSlot, ct);
    }

    public async Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(demandSlots);

        var slotList = demandSlots as ICollection<DemandSlot> ?? demandSlots.ToList();
        if (slotList.Count == 0)
        {
            return;
        }

        await _dbContext.DemandSlots.AddRangeAsync(slotList, ct);
    }

    public void Update(DemandSlot demandSlot)
    {
        ArgumentNullException.ThrowIfNull(demandSlot);

        if (_dbContext.Entry(demandSlot).State != EntityState.Detached)
        {
            return;
        }

        var trackedEntry = _dbContext.ChangeTracker
            .Entries<DemandSlot>()
            .FirstOrDefault(entry => entry.Entity.Id == demandSlot.Id);

        if (trackedEntry is not null)
        {
            trackedEntry.CurrentValues.SetValues(demandSlot);
            return;
        }

        _dbContext.DemandSlots.Update(demandSlot);
    }

    public void RemoveRange(IEnumerable<DemandSlot> demandSlots)
    {
        ArgumentNullException.ThrowIfNull(demandSlots);

        var slotList = demandSlots as ICollection<DemandSlot> ?? demandSlots.ToList();
        if (slotList.Count == 0)
        {
            return;
        }

        var trackedById = _dbContext.ChangeTracker
            .Entries<DemandSlot>()
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);

        var resolvedSlots = new List<DemandSlot>(slotList.Count);
        foreach (var slot in slotList)
        {
            if (_dbContext.Entry(slot).State == EntityState.Detached &&
                trackedById.TryGetValue(slot.Id, out var trackedSlot))
            {
                resolvedSlots.Add(trackedSlot);
            }
            else
            {
                resolvedSlots.Add(slot);
            }
        }

        _dbContext.DemandSlots.RemoveRange(resolvedSlots);
    }
}

