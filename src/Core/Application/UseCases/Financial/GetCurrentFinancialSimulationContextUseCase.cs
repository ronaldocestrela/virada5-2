using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Financial;
using Application.UseCases.Common;
using Domain.Repositories;

namespace Application.UseCases.Financial;

/// <summary>
/// Caso de uso somente leitura para carregar o contexto operacional do simulador financeiro.
/// </summary>
public sealed class GetCurrentFinancialSimulationContextUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IResultTelemetry? _telemetry;

    public GetCurrentFinancialSimulationContextUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
        _telemetry = telemetry;
    }

    public Task<Result<FinancialSimulationContextDto>> ExecuteAsync(
        Guid? companyId = null,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "GetCurrentFinancialSimulationContext",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(companyId, ct));

    private async Task<Result<FinancialSimulationContextDto>> ExecuteCoreAsync(
        Guid? companyId,
        CancellationToken ct)
    {
        var contextResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            companyId,
            _companyRepository,
            _currentUserService,
            "FinancialSimulation.EmptyTenant",
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<FinancialSimulationContextDto>(contextResult.Error);
        }

        var (_, company) = contextResult.Value;
        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var demandSlots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var hasDemandMatrix = demandSlots.Any(slot => company.IsOpenAt(slot.DayOfWeek, slot.Hour));

        return Result.Success(new FinancialSimulationContextDto(
            company.Id,
            company.Name,
            activeEmployees.Count,
            hasDemandMatrix,
            SimulateFinancialImpactUseCase.DefaultAssumptions));
    }
}
