using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para troca manual atômica de dois turnos na escala em rascunho com validação CLT.
/// </summary>
public sealed class SwapManualShiftsUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public SwapManualShiftsUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _telemetry = telemetry;
    }

    public Task<Result<GeneratedRosterResultDto>> ExecuteAsync(
        SwapManualShiftsCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "SwapManualShifts",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<GeneratedRosterResultDto>> ExecuteCoreAsync(
        SwapManualShiftsCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.InvalidCommand", "Comando de troca manual inválido."));
        }

        if (command.FirstShiftId == Guid.Empty || command.SecondShiftId == Guid.Empty)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.EmptyShiftId", "Informe os dois turnos para realizar a troca."));
        }

        var loadedResult = await RosterResultMapper.LoadRosterContextAsync(
            command.RosterId,
            requireDraft: true,
            "RosterAdjustment.EmptyTenant",
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _currentUserService,
            ct);

        if (loadedResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(loadedResult.Error);
        }

        var ctx = loadedResult.Value;
        var firstShift = ctx.Roster.Shifts.FirstOrDefault(s => s.Id == command.FirstShiftId);
        var secondShift = ctx.Roster.Shifts.FirstOrDefault(s => s.Id == command.SecondShiftId);

        if (firstShift is null || secondShift is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.NotFound("Roster.ShiftNotFound", "Um ou mais turnos informados para troca não foram encontrados."));
        }

        var firstOwner = ctx.AllRelevantEmployees.FirstOrDefault(e => e.Id == firstShift.EmployeeId);
        var secondOwner = ctx.AllRelevantEmployees.FirstOrDefault(e => e.Id == secondShift.EmployeeId);

        if (firstOwner is null || secondOwner is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.NotFound("Employee.NotFound", "Um ou mais colaboradores dos turnos selecionados não foram encontrados."));
        }

        var firstDemand = RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(ctx.Company, firstShift, ctx.DemandSlots);
        var secondDemand = RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(ctx.Company, secondShift, ctx.DemandSlots);

        ctx.BoundaryContexts.TryGetValue(secondOwner.Id, out var firstReplacementBoundary);
        ctx.BoundaryContexts.TryGetValue(firstOwner.Id, out var secondReplacementBoundary);

        var swapResult = ctx.Roster.SwapShiftEmployees(
            firstShift.Id,
            secondOwner,
            secondShift.Id,
            firstOwner,
            firstDemand,
            secondDemand,
            firstReplacementBoundary,
            secondReplacementBoundary);

        if (swapResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(swapResult.Error);
        }

        _rosterRepository.Update(ctx.Roster);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(RosterResultMapper.EvaluateAndMapResult(
            ctx.Roster,
            ctx.Company,
            ctx.AllRelevantEmployees,
            ctx.DemandSlots,
            ctx.BoundaryContexts));
    }
}
