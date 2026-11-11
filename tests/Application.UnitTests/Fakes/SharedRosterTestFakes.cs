using Application.Common.Interfaces;
using Application.DTOs.Rosters;

namespace Application.UnitTests.Fakes;

internal sealed class FakeScopedTenantContext : IScopedTenantContext
{
    private readonly Stack<Guid> _scopes = new();

    public List<Guid> EnteredTenants { get; } = [];
    public Guid? ScopedTenantId => _scopes.Count > 0 ? _scopes.Peek() : null;

    public IDisposable BeginScope(Guid tenantId)
    {
        EnteredTenants.Add(tenantId);
        _scopes.Push(tenantId);
        return new ScopeHandle(this);
    }

    private sealed class ScopeHandle(FakeScopedTenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (owner._scopes.Count > 0)
            {
                owner._scopes.Pop();
            }
        }
    }
}

internal sealed class FakeScopedAwareCurrentUserService(IScopedTenantContext scopedTenantContext) : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public Guid BaseTenantId { get; set; } = Guid.Empty;
    public Guid TenantId => scopedTenantContext.ScopedTenantId ?? BaseTenantId;
    public bool IsAuthenticated => UserId.HasValue && BaseTenantId != Guid.Empty;
}

internal sealed class FakeRosterShareLinkService : IRosterShareLinkService
{
    private readonly Dictionary<string, Result<RosterShareTokenPayloadDto>> _tokens = new(StringComparer.Ordinal);

    public void RegisterToken(string token, RosterShareTokenPayloadDto payload) =>
        _tokens[token] = Result.Success(payload);

    public void RegisterFailure(string token, Error error) =>
        _tokens[token] = Result.Failure<RosterShareTokenPayloadDto>(error);

    public Result<RosterShareLinkDto> CreateShareLink(
        Guid tenantId,
        Guid companyId,
        Guid rosterId,
        DateOnly weekStartDate,
        TimeSpan? lifetime = null)
    {
        var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(14));
        var token = $"token-{rosterId:N}";
        RegisterToken(token, new RosterShareTokenPayloadDto(tenantId, companyId, rosterId, weekStartDate, expires));
        return Result.Success(new RosterShareLinkDto(rosterId, token, $"/shared/roster?token={token}", expires));
    }

    public Result<RosterShareTokenPayloadDto> ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<RosterShareTokenPayloadDto>(
                Error.Validation("RosterShare.EmptyToken", "O token de compartilhamento é obrigatório."));
        }

        return _tokens.TryGetValue(token, out var result)
            ? result
            : Result.Failure<RosterShareTokenPayloadDto>(
                Error.Validation("RosterShare.InvalidToken", "Token de compartilhamento inválido."));
    }
}

