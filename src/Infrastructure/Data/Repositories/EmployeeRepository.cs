using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Implementação EF Core do repositório específico da agregação <see cref="Employee"/>.
/// </summary>
public sealed class EmployeeRepository : IEmployeeRepository
{
    private readonly AppDbContext _dbContext;

    public EmployeeRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Employees
            .FirstOrDefaultAsync(employee => employee.Id == id, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        if (companyId == Guid.Empty)
        {
            return [];
        }

        return await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.CompanyId == companyId)
            .OrderBy(employee => employee.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        if (companyId == Guid.Empty)
        {
            return [];
        }

        return await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.CompanyId == companyId && employee.IsActive)
            .OrderBy(employee => employee.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (idList.Count == 0)
        {
            return [];
        }

        return await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => idList.Contains(employee.Id))
            .OrderBy(employee => employee.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Employee employee, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        await _dbContext.Employees.AddAsync(employee, ct);
    }

    public void Update(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (_dbContext.Entry(employee).State == EntityState.Detached)
        {
            _dbContext.Employees.Update(employee);
        }
    }
}

