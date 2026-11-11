using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// Contrato de repositório específico para a agregação <see cref="Company"/>.
/// </summary>
public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Company?> GetCurrentAsync(CancellationToken ct = default);
    Task AddAsync(Company company, CancellationToken ct = default);
    void Update(Company company);
}

