using System.Net.Mail;
using Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Data.Identity;

/// <summary>
/// Usuário autenticado da plataforma vinculado a um estabelecimento (tenant).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public const int MaxFullNameLength = 150;

    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Cria uma instância validada de <see cref="ApplicationUser"/> vinculada ao tenant.
    /// </summary>
    public static Result<ApplicationUser> Create(
        Guid tenantId,
        string email,
        string fullName,
        DateTime? createdAtUtc = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<ApplicationUser>(
                Error.Validation("Identity.User.InvalidTenantId", "O usuário deve estar vinculado a um tenant válido."));
        }

        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !IsValidEmail(normalizedEmail))
        {
            return Result.Failure<ApplicationUser>(
                Error.Validation("Identity.User.InvalidEmail", "O e-mail informado é inválido."));
        }

        var normalizedFullName = fullName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            return Result.Failure<ApplicationUser>(
                Error.Validation("Identity.User.InvalidFullName", "O nome completo do usuário é obrigatório."));
        }

        if (normalizedFullName.Length > MaxFullNameLength)
        {
            return Result.Failure<ApplicationUser>(
                Error.Validation("Identity.User.FullNameTooLong", $"O nome completo não pode exceder {MaxFullNameLength} caracteres."));
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FullName = normalizedFullName,
            IsActive = true,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        return Result.Success(user);
    }

    private static bool IsValidEmail(string email) =>
        MailAddress.TryCreate(email, out var address) &&
        string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
}

