using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Companies;
using Domain.Repositories;

namespace Application.UseCases.Companies;

/// <summary>
/// Obtém as configurações atuais do estabelecimento vinculado ao tenant autenticado.
/// </summary>
public sealed class GetCurrentCompanySettingsUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IResultTelemetry? _telemetry;

    public GetCurrentCompanySettingsUseCase(
        ICompanyRepository companyRepository,
        ICurrentUserService currentUserService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _currentUserService = currentUserService;
        _telemetry = telemetry;
    }

    public Task<Result<CompanySettingsDto>> ExecuteAsync(CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "GetCurrentCompanySettings",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(ct));

    private async Task<Result<CompanySettingsDto>> ExecuteCoreAsync(CancellationToken ct)
    {
        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<CompanySettingsDto>(
                Error.Validation(
                    "CompanySettings.EmptyTenantId",
                    "O identificador do tenant autenticado é obrigatório."));
        }

        var company = await _companyRepository.GetCurrentAsync(ct);
        if (company is null)
        {
            return Result.Failure<CompanySettingsDto>(
                Error.NotFound(
                    "Company.NotFound",
                    "Estabelecimento ainda não configurado para o tenant atual."));
        }

        return Result.Success(CompanySettingsMapper.ToDto(company));
    }
}
