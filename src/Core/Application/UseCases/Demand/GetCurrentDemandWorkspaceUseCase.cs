using Application.Common.Interfaces;
using Application.DTOs.Demand;
using Application.UseCases.Companies;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Demand;

/// <summary>
/// Obtém em modo somente leitura a grade de funcionamento do estabelecimento e o diagnóstico de demanda atual.
/// </summary>
public sealed class GetCurrentDemandWorkspaceUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrentDemandWorkspaceUseCase(
        ICompanyRepository companyRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DemandWorkspaceDto>> ExecuteAsync(
        GetCurrentDemandWorkspaceCommand? command = null,
        CancellationToken ct = default)
    {
        var contextResult = await DemandUseCaseHelpers.ResolveCompanyAsync(
            command?.CompanyId,
            _companyRepository,
            _currentUserService,
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<DemandWorkspaceDto>(contextResult.Error);
        }

        var (_, company) = contextResult.Value;
        var (operatingHours, operatingSlots) = BuildOperatingGrid(company);

        var slots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        DemandDiagnosisDto? diagnosis = null;

        if (slots.Count > 0)
        {
            var diagnosisResult = DemandDiagnosisCalculator.Calculate(company, slots);
            if (diagnosisResult.IsFailure)
            {
                return Result.Failure<DemandWorkspaceDto>(diagnosisResult.Error);
            }

            diagnosis = diagnosisResult.Value;
        }

        return Result.Success(new DemandWorkspaceDto(
            CompanySettingsMapper.ToDto(company),
            operatingHours,
            operatingSlots,
            diagnosis));
    }

    private static (List<int> OperatingHours, List<DemandOperatingSlotDto> OperatingSlots) BuildOperatingGrid(Company company)
    {
        var days = Enum.GetValues<DayOfWeek>();
        var operatingHours = new List<int>();

        for (var hour = DemandSlot.MinHour; hour <= DemandSlot.MaxHour; hour++)
        {
            if (days.Any(day => company.IsOpenAt(day, hour)))
            {
                operatingHours.Add(hour);
            }
        }

        var operatingSlots = new List<DemandOperatingSlotDto>(days.Length * operatingHours.Count);
        foreach (var hour in operatingHours)
        {
            foreach (var day in days)
            {
                operatingSlots.Add(new DemandOperatingSlotDto(day, hour, company.IsOpenAt(day, hour)));
            }
        }

        return (operatingHours, operatingSlots);
    }
}

