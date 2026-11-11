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

internal sealed class FakeShiftSwapRequestRepository : IShiftSwapRequestRepository
{
    private readonly List<ShiftSwapRequest> _requests = [];

    public List<ShiftSwapRequest> AddedRequests { get; } = [];
    public List<ShiftSwapRequest> UpdatedRequests { get; } = [];

    public IReadOnlyList<ShiftSwapRequest> PersistedRequests => _requests.AsReadOnly();

    public void Seed(params ShiftSwapRequest[] requests) => _requests.AddRange(requests);

    public Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_requests.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<ShiftSwapRequest>> GetByRosterAsync(Guid rosterId, CancellationToken ct = default)
    {
        IReadOnlyList<ShiftSwapRequest> result = _requests
            .Where(r => r.RosterId == rosterId)
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ShiftSwapRequest>> GetPendingByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        IReadOnlyList<ShiftSwapRequest> result = _requests
            .Where(r => r.CompanyId == companyId && r.IsPending)
            .OrderBy(r => r.RequestedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<bool> HasActiveRequestForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(_requests.Any(r =>
            r.IsPending &&
            (r.RequesterShiftId == shiftId || r.TargetShiftId == shiftId)));

    public Task AddAsync(ShiftSwapRequest request, CancellationToken ct = default)
    {
        _requests.Add(request);
        AddedRequests.Add(request);
        return Task.CompletedTask;
    }

    public void Update(ShiftSwapRequest request)
    {
        if (!UpdatedRequests.Contains(request))
        {
            UpdatedRequests.Add(request);
        }
    }
}

