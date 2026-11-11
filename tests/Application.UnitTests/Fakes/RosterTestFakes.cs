using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UnitTests.Fakes;

internal sealed class FakeRosterRepository : IRosterRepository
{
    private readonly List<Roster> _rosters = [];

    public List<Roster> AddedRosters { get; } = [];
    public List<Roster> UpdatedRosters { get; } = [];

    public IReadOnlyList<Roster> PersistedRosters => _rosters.AsReadOnly();

    public void Seed(params Roster[] rosters) => _rosters.AddRange(rosters);

    public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_rosters.FirstOrDefault(r => r.Id == id));

    public Task<Roster?> GetByCompanyAndWeekStartAsync(
        Guid companyId,
        DateOnly weekStartDate,
        CancellationToken ct = default) =>
        Task.FromResult(_rosters.FirstOrDefault(r =>
            r.CompanyId == companyId &&
            r.WeekStartDate == weekStartDate &&
            r.Status != RosterStatus.Archived));

    public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(
        Guid companyId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        IReadOnlyList<Roster> result = _rosters
            .Where(r =>
                r.CompanyId == companyId &&
                r.Status != RosterStatus.Archived &&
                r.WeekStartDate <= endDate &&
                r.WeekEndDate >= startDate)
            .OrderBy(r => r.WeekStartDate)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Roster roster, CancellationToken ct = default)
    {
        _rosters.Add(roster);
        AddedRosters.Add(roster);
        return Task.CompletedTask;
    }

    public void Update(Roster roster)
    {
        if (!UpdatedRosters.Contains(roster))
        {
            UpdatedRosters.Add(roster);
        }
    }
}

