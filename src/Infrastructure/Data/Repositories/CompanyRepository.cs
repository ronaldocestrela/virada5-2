using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Implementação EF Core do repositório específico da agregação <see cref="Company"/>.
/// </summary>
public sealed class CompanyRepository : ICompanyRepository
{
    private readonly AppDbContext _dbContext;

    public CompanyRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Companies
            .FirstOrDefaultAsync(company => company.Id == id, ct);
    }

    public async Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
        await _dbContext.Companies.SingleOrDefaultAsync(ct);

    public async Task AddAsync(Company company, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        await _dbContext.Companies.AddAsync(company, ct);
    }

    public void Update(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (_dbContext.Entry(company).State == EntityState.Detached)
        {
            _dbContext.Companies.Update(company);
        }
    }
}

