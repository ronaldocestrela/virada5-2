using Application.DTOs.Gtm;
using Domain.Common;

namespace Application.Common.Interfaces;

/// <summary>
/// Abstração para criação de uma nova conta de proprietário (<c>Owner</c>) com <c>TenantId</c> isolado.
/// </summary>
public interface ITenantRegistrationService
{
    Task<Result<TenantRegistrationDto>> RegisterOwnerAsync(
        string fullName,
        string email,
        string password,
        CancellationToken ct = default);
}

