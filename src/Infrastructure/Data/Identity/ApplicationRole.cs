using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Data.Identity;

/// <summary>
/// Perfil de acesso da aplicação no ASP.NET Core Identity.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public const int MaxDescriptionLength = 250;

    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName, string? description = null)
        : base(roleName)
    {
        Id = Guid.NewGuid();
        Description = description?.Trim();
    }

    public string? Description { get; set; }
}

