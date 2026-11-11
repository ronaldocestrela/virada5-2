using Application.Common.Interfaces;
using Application.DTOs.Companies;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Companies;

/// <summary>
/// Cria (onboarding) ou atualiza as configurações operacionais e de escala do estabelecimento do tenant atual.
/// </summary>
public sealed class SaveCompanySettingsUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public SaveCompanySettingsUseCase(
        ICompanyRepository companyRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CompanySettingsDto>> ExecuteAsync(
        SaveCompanySettingsCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<CompanySettingsDto>(
                Error.Validation("CompanySettings.InvalidCommand", "Comando de configuração da casa inválido."));
        }

        var tenantId = _currentUserService.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<CompanySettingsDto>(
                Error.Validation(
                    "CompanySettings.EmptyTenantId",
                    "O identificador do tenant autenticado é obrigatório."));
        }

        var candidateResult = Company.Create(
            tenantId,
            command.Name,
            command.BusinessType,
            command.OpeningTime,
            command.ClosingTime,
            command.ScheduleModel,
            command.ClosedDays);

        if (candidateResult.IsFailure)
        {
            return Result.Failure<CompanySettingsDto>(candidateResult.Error);
        }

        var existing = await _companyRepository.GetCurrentAsync(ct);
        if (existing is null)
        {
            var created = candidateResult.Value;
            await _companyRepository.AddAsync(created, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success(CompanySettingsMapper.ToDto(created));
        }

        var profileResult = existing.UpdateProfile(command.Name, command.BusinessType);
        if (profileResult.IsFailure)
        {
            return Result.Failure<CompanySettingsDto>(profileResult.Error);
        }

        var hoursResult = existing.UpdateOperatingHours(command.OpeningTime, command.ClosingTime);
        if (hoursResult.IsFailure)
        {
            return Result.Failure<CompanySettingsDto>(hoursResult.Error);
        }

        var scheduleResult = existing.UpdateScheduleModel(command.ScheduleModel, command.ClosedDays);
        if (scheduleResult.IsFailure)
        {
            return Result.Failure<CompanySettingsDto>(scheduleResult.Error);
        }

        _companyRepository.Update(existing);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(CompanySettingsMapper.ToDto(existing));
    }
}
