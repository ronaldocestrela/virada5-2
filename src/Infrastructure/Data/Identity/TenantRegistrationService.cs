using Application.Common.Interfaces;
using Application.DTOs.Gtm;
using Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Data.Identity;

/// <summary>
/// Implementação via ASP.NET Core Identity para criação de um novo proprietário (<c>Owner</c>)
/// com <c>TenantId</c> próprio e isolado.
/// </summary>
public sealed class TenantRegistrationService : ITenantRegistrationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public TenantRegistrationService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(roleManager);
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<Result<TenantRegistrationDto>> RegisterOwnerAsync(
        string fullName,
        string email,
        string password,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var existing = await _userManager.FindByEmailAsync(email.Trim());
        if (existing is not null)
        {
            return Result.Failure<TenantRegistrationDto>(
                Error.Conflict("TenantRegistration.DuplicateEmail", "Já existe uma conta cadastrada com este e-mail."));
        }

        var tenantId = Guid.NewGuid();
        var userResult = ApplicationUser.Create(tenantId, email, fullName);
        if (userResult.IsFailure)
        {
            return Result.Failure<TenantRegistrationDto>(userResult.Error);
        }

        var user = userResult.Value;
        user.EmailConfirmed = true;

        if (!await _roleManager.RoleExistsAsync(ApplicationRoles.Owner))
        {
            var roleCreate = await _roleManager.CreateAsync(
                new ApplicationRole(ApplicationRoles.Owner, "Proprietário do estabelecimento"));
            if (!roleCreate.Succeeded)
            {
                return Result.Failure<TenantRegistrationDto>(
                    Error.Failure("TenantRegistration.RoleCreationFailed", "Não foi possível provisionar o perfil de proprietário."));
            }
        }

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var isDuplicate = createResult.Errors.Any(e =>
                e.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

            var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<TenantRegistrationDto>(
                isDuplicate
                    ? Error.Conflict("TenantRegistration.DuplicateEmail", message)
                    : Error.Validation("TenantRegistration.IdentityValidationFailed", message));
        }

        var addRoleResult = await _userManager.AddToRoleAsync(user, ApplicationRoles.Owner);
        if (!addRoleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return Result.Failure<TenantRegistrationDto>(
                Error.Failure("TenantRegistration.RoleAssignmentFailed", "Não foi possível atribuir o perfil de proprietário."));
        }

        return Result.Success(new TenantRegistrationDto(
            user.Id,
            tenantId,
            user.Email!,
            user.FullName));
    }
}

