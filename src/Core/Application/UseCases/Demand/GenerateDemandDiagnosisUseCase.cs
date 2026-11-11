using Application.Common.Interfaces;
using Application.DTOs.Demand;
using Domain.Repositories;

namespace Application.UseCases.Demand;

public sealed class GenerateDemandDiagnosisUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateDemandDiagnosisUseCase(
        ICompanyRepository companyRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<DemandDiagnosisDto>> ExecuteAsync(
        GenerateDemandDiagnosisCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<DemandDiagnosisDto>(
                Error.Validation("DemandDiagnosis.InvalidCommand", "Comando de diagnóstico inválido."));
        }

        var contextResult = await DemandUseCaseHelpers.ResolveCompanyAsync(
            command.CompanyId,
            _companyRepository,
            _currentUserService,
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<DemandDiagnosisDto>(contextResult.Error);
        }

        var (_, company) = contextResult.Value;

        var slots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        if (slots.Count == 0)
        {
            return Result.Failure<DemandDiagnosisDto>(
                Error.Validation(
                    "DemandDiagnosis.EmptyDemandSlots",
                    "Importe ou preencha a matriz de demanda antes de gerar o diagnóstico."));
        }

        var diagnosisResult = DemandDiagnosisCalculator.Calculate(
            company,
            slots,
            _demandSlotRepository.Update);

        if (diagnosisResult.IsFailure)
        {
            return diagnosisResult;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return diagnosisResult;
    }
}
