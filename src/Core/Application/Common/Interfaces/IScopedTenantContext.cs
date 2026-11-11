namespace Application.Common.Interfaces;

/// <summary>
/// Controla um escopo temporário e explícito de tenant para execuções anônimas previamente validadas por token criptográfico.
/// </summary>
public interface IScopedTenantContext
{
    Guid? ScopedTenantId { get; }
    IDisposable BeginScope(Guid tenantId);
}

