namespace Application.Common.Interfaces;

/// <summary>
/// Contrato de unidade de trabalho para confirmação transacional de alterações persistidas.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

