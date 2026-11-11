using System.Text.Json;
using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Domain.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;

namespace WebApp.Blazor.Services;

/// <summary>
/// Serviço baseado em ASP.NET Core DataProtection para geração e validação de tokens de compartilhamento da escala.
/// </summary>
public sealed class RosterShareLinkService : IRosterShareLinkService
{
    public const string ProtectorPurpose = "Virada5x2.RosterShare.v1";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly NavigationManager? _navigationManager;

    public RosterShareLinkService(
        IDataProtectionProvider dataProtectionProvider,
        NavigationManager? navigationManager = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        _navigationManager = navigationManager;
    }

    public Result<RosterShareLinkDto> CreateShareLink(
        Guid tenantId,
        Guid companyId,
        Guid rosterId,
        DateOnly weekStartDate,
        TimeSpan? lifetime = null)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || rosterId == Guid.Empty || weekStartDate == default)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Validation("RosterShare.InvalidPayload", "Dados inválidos para gerar o link de compartilhamento."));
        }

        var resolvedLifetime = lifetime is { TotalSeconds: > 0 } ? lifetime.Value : DefaultLifetime;
        var expiresAtUtc = DateTime.UtcNow.Add(resolvedLifetime);
        var payload = new RosterShareTokenPayloadDto(tenantId, companyId, rosterId, weekStartDate, expiresAtUtc);
        var json = JsonSerializer.Serialize(payload);
        var rawProtected = _protector.Protect(json, resolvedLifetime);
        var encodedToken = Uri.EscapeDataString(rawProtected);

        var relativePath = $"/shared/roster?token={encodedToken}";
        var shareUrl = ResolveAbsoluteOrRelativeUrl(relativePath);

        return Result.Success(new RosterShareLinkDto(
            rosterId,
            rawProtected,
            shareUrl,
            expiresAtUtc));
    }

    public Result<RosterShareTokenPayloadDto> ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<RosterShareTokenPayloadDto>(
                Error.Validation("RosterShare.EmptyToken", "O token de compartilhamento é obrigatório."));
        }

        try
        {
            var json = _protector.Unprotect(token, out var expiration);
            var payload = JsonSerializer.Deserialize<RosterShareTokenPayloadDto>(json);
            if (payload is null ||
                payload.TenantId == Guid.Empty ||
                payload.CompanyId == Guid.Empty ||
                payload.RosterId == Guid.Empty)
            {
                return Result.Failure<RosterShareTokenPayloadDto>(
                    Error.Validation("RosterShare.InvalidToken", "Token de compartilhamento inválido."));
            }

            if (expiration.UtcDateTime <= DateTime.UtcNow || payload.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return Result.Failure<RosterShareTokenPayloadDto>(
                    Error.Conflict("RosterShare.ExpiredToken", "Este link de compartilhamento expirou."));
            }

            return Result.Success(payload);
        }
        catch (Exception)
        {
            return Result.Failure<RosterShareTokenPayloadDto>(
                Error.Validation("RosterShare.InvalidToken", "Token de compartilhamento inválido ou adulterado."));
        }
    }

    private string ResolveAbsoluteOrRelativeUrl(string relativePath)
    {
        if (_navigationManager is null)
        {
            return relativePath;
        }

        try
        {
            var baseUri = _navigationManager.BaseUri;
            if (!string.IsNullOrWhiteSpace(baseUri))
            {
                return new Uri(new Uri(baseUri, UriKind.Absolute), relativePath.TrimStart('/')).ToString();
            }
        }
        catch (InvalidOperationException)
        {
            // NavigationManager ainda não inicializado fora de circuito Blazor ativo.
        }

        return relativePath;
    }
}
