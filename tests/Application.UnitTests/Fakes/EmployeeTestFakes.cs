using Domain.Entities;
using Domain.Repositories;

namespace Application.UnitTests.Fakes;

internal sealed class FakeEmployeeRepository : IEmployeeRepository
{
    private readonly List<Employee> _employees = [];

    public void Seed(params Employee[] employees) => _employees.AddRange(employees);

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        IReadOnlyList<Employee> result = _employees
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.Name)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        IReadOnlyList<Employee> result = _employees
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .OrderBy(e => e.Name)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idSet = ids.ToHashSet();
        IReadOnlyList<Employee> result = _employees
            .Where(e => idSet.Contains(e.Id))
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Employee employee, CancellationToken ct = default)
    {
        _employees.Add(employee);
        return Task.CompletedTask;
    }

    public void Update(Employee employee)
    {
    }
}

