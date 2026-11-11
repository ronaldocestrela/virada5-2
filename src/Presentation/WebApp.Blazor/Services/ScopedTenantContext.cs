using Application.Common.Interfaces;

namespace WebApp.Blazor.Services;

/// <summary>
/// Armazena temporariamente o TenantId validado por token criptográfico dentro do fluxo assíncrono atual.
/// </summary>
public sealed class ScopedTenantContext : IScopedTenantContext
{
    private readonly Lock _sync = new();
    private readonly List<ScopeNode> _scopes = [];

    public Guid? ScopedTenantId
    {
        get
        {
            lock (_sync)
            {
                return _scopes.Count > 0 ? _scopes[^1].TenantId : null;
            }
        }
    }

    public IDisposable BeginScope(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("O TenantId do escopo não pode ser vazio.", nameof(tenantId));
        }

        var node = new ScopeNode(tenantId);
        lock (_sync)
        {
            _scopes.Add(node);
        }

        return new ScopeHandle(this, node);
    }

    private sealed class ScopeNode(Guid tenantId)
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class ScopeHandle(ScopedTenantContext owner, ScopeNode node) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (owner._sync)
            {
                owner._scopes.Remove(node);
            }
        }
    }
}
