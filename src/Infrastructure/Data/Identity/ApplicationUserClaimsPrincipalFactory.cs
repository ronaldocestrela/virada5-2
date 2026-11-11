using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Infrastructure.Data.Identity;

/// <summary>
/// Enriquece o <see cref="ClaimsPrincipal"/> gerado pelo Identity com as claims de tenant e nome completo.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user);

        if (user.TenantId != Guid.Empty)
        {
            identity.AddClaim(new Claim(TenantClaimTypes.TenantId, user.TenantId.ToString("D")));
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            identity.AddClaim(new Claim(TenantClaimTypes.FullName, user.FullName));
        }

        return identity;
    }
}

