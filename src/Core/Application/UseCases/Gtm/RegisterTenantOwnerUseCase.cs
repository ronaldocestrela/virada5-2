using System.Net.Mail;
using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Gtm;
using Domain.Common;

namespace Application.UseCases.Gtm;

/// <summary>
/// Caso de uso para autocadastro de novo estabelecimento/proprietário a partir do funil GTM ou acesso direto.
/// </summary>
public sealed class RegisterTenantOwnerUseCase
{
    private readonly ITenantRegistrationService _registrationService;
    private readonly IResultTelemetry? _telemetry;

    public RegisterTenantOwnerUseCase(
        ITenantRegistrationService registrationService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(registrationService);
        _registrationService = registrationService;
        _telemetry = telemetry;
    }

    public Task<Result<TenantRegistrationDto>> ExecuteAsync(
        RegisterTenantOwnerCommand command,
        CancellationToken ct = default)
    {
        return ApplicationTelemetry.ObserveAsync(
            "RegisterTenantOwner",
            null,
            _telemetry,
            async telemetryContext =>
            {
                if (command is null)
                {
                    return Result.Failure<TenantRegistrationDto>(
                        Error.Validation("TenantRegistration.InvalidCommand", "Dados de cadastro inválidos."));
                }

                var fullName = command.FullName?.Trim() ?? string.Empty;
                if (fullName.Length < 3 || fullName.Length > 120)
                {
                    return Result.Failure<TenantRegistrationDto>(
                        Error.Validation("TenantRegistration.InvalidFullName", "Informe o nome completo (entre 3 e 120 caracteres)."));
                }

                var email = command.Email?.Trim() ?? string.Empty;
                if (!IsValidEmail(email))
                {
                    return Result.Failure<TenantRegistrationDto>(
                        Error.Validation("TenantRegistration.InvalidEmail", "Informe um endereço de e-mail válido."));
                }

                if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8)
                {
                    return Result.Failure<TenantRegistrationDto>(
                        Error.Validation("TenantRegistration.WeakPassword", "A senha deve possuir ao menos 8 caracteres."));
                }

                var result = await _registrationService.RegisterOwnerAsync(fullName, email, command.Password, ct);
                if (result.IsSuccess)
                {
                    telemetryContext.SetTenantId(result.Value.TenantId);
                    ApplicationTelemetry.RecordConversion(
                        "tenant_owner_registered",
                        command.UtmSource,
                        command.UtmMedium,
                        command.UtmCampaign,
                        _telemetry);
                }

                return result;
            });
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

