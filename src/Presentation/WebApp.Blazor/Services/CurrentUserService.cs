using System.Security.Claims;
using Application.Common.Interfaces;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace WebApp.Blazor.Services;

/// <summary>
/// Resolve o usuário autenticado e o <c>TenantId</c> ativo no contexto HTTP ou no circuito Blazor Server,
/// operando em modo fail-closed (<see cref="Guid.Empty"/>) caso ausente ou inválido.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider? _authenticationStateProvider;
    private readonly IScopedTenantContext? _scopedTenantContext;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider? authenticationStateProvider = null,
        IScopedTenantContext? scopedTenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
        _authenticationStateProvider = authenticationStateProvider;
        _scopedTenantContext = scopedTenantContext;
    }

    public Guid? UserId => TryParseGuidClaim(GetActivePrincipal(), ClaimTypes.NameIdentifier);

    public Guid TenantId
    {
        get
        {
            if (_scopedTenantContext?.ScopedTenantId is { } scopedTenantId && scopedTenantId != Guid.Empty)
            {
                return scopedTenantId;
            }

            var principal = GetActivePrincipal();
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return Guid.Empty;
            }

            return TryParseGuidClaim(principal, TenantClaimTypes.TenantId) ?? Guid.Empty;
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            var principal = GetActivePrincipal();
            return principal?.Identity?.IsAuthenticated == true
                && UserId.HasValue
                && TenantId != Guid.Empty;
        }
    }

    private ClaimsPrincipal? GetActivePrincipal()
    {
        var httpUser = _httpContextAccessor.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true)
        {
            return httpUser;
        }

        if (_authenticationStateProvider is not null)
        {
            var authStateTask = _authenticationStateProvider.GetAuthenticationStateAsync();
            if (authStateTask.IsCompletedSuccessfully)
            {
                return authStateTask.Result.User;
            }
        }

        return httpUser;
    }

    private static Guid? TryParseGuidClaim(ClaimsPrincipal? principal, string claimType)
    {
        var value = principal?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;
    }
}

