using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UnitTests.Fakes;

internal sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = Guid.NewGuid();
    public bool IsAuthenticated { get; set; } = true;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeCompanyRepository : ICompanyRepository
{
    private readonly List<Company> _companies = [];

    public void Seed(Company company) => _companies.Add(company);

    public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_companies.FirstOrDefault(c => c.Id == id));

    public Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
        Task.FromResult(_companies.SingleOrDefault());

    public Task AddAsync(Company company, CancellationToken ct = default)
    {
        _companies.Add(company);
        return Task.CompletedTask;
    }

    public void Update(Company company)
    {
    }
}

internal sealed class FakeDemandSlotRepository : IDemandSlotRepository
{
    private readonly List<DemandSlot> _slots = [];

    public List<DemandSlot> AddedSlots { get; } = [];
    public List<DemandSlot> UpdatedSlots { get; } = [];
    public List<DemandSlot> RemovedSlots { get; } = [];

    public IReadOnlyList<DemandSlot> PersistedSlots => _slots.AsReadOnly();

    public void Seed(params DemandSlot[] slots) => _slots.AddRange(slots);

    public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_slots.FirstOrDefault(s => s.Id == id));

    public Task<DemandSlot?> GetByCompanyDayAndHourAsync(
        Guid companyId,
        DayOfWeek dayOfWeek,
        int hour,
        CancellationToken ct = default) =>
        Task.FromResult(_slots.FirstOrDefault(s =>
            s.CompanyId == companyId &&
            s.DayOfWeek == dayOfWeek &&
            s.Hour == hour));

    public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        IReadOnlyList<DemandSlot> result = _slots
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.Hour)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default)
    {
        _slots.Add(demandSlot);
        AddedSlots.Add(demandSlot);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default)
    {
        foreach (var slot in demandSlots)
        {
            _slots.Add(slot);
            AddedSlots.Add(slot);
        }

        return Task.CompletedTask;
    }

    public void Update(DemandSlot demandSlot)
    {
        if (!UpdatedSlots.Contains(demandSlot))
        {
            UpdatedSlots.Add(demandSlot);
        }
    }

    public void RemoveRange(IEnumerable<DemandSlot> demandSlots)
    {
        foreach (var slot in demandSlots.ToList())
        {
            _slots.Remove(slot);
            RemovedSlots.Add(slot);
        }
    }
}

