using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para reatribuição manual de um turno em rascunho para outro colaborador elegível.
/// </summary>
public sealed class ReassignManualShiftUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public ReassignManualShiftUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
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
    }

    public async Task<Result<GeneratedRosterResultDto>> ExecuteAsync(
        ReassignManualShiftCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.InvalidCommand", "Comando de reatribuição manual inválido."));
        }

        if (command.ShiftId == Guid.Empty)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.EmptyShiftId", "O identificador do turno é obrigatório."));
        }

        if (command.TargetEmployeeId == Guid.Empty)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.EmptyEmployeeId", "O colaborador substituto é obrigatório."));
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
        var shift = ctx.Roster.Shifts.FirstOrDefault(s => s.Id == command.ShiftId);
        if (shift is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.NotFound("Roster.ShiftNotFound", "Turno não encontrado na escala."));
        }

        var replacement = ctx.AllRelevantEmployees.FirstOrDefault(e => e.Id == command.TargetEmployeeId)
            ?? await _employeeRepository.GetByIdAsync(command.TargetEmployeeId, ct);

        if (replacement is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.NotFound("Employee.NotFound", "Colaborador substituto não encontrado para o tenant atual."));
        }

        var demandLevel = RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(
            ctx.Company,
            shift,
            ctx.DemandSlots);

        ctx.BoundaryContexts.TryGetValue(replacement.Id, out var replacementBoundary);

        var reassignResult = ctx.Roster.ReassignShiftEmployee(
            shift.Id,
            replacement,
            demandLevel,
            replacementBoundary);

        if (reassignResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(reassignResult.Error);
        }

        _rosterRepository.Update(ctx.Roster);
        await _unitOfWork.SaveChangesAsync(ct);

        var relevantEmployees = ctx.AllRelevantEmployees.Any(e => e.Id == replacement.Id)
            ? ctx.AllRelevantEmployees
            : ctx.AllRelevantEmployees.Append(replacement).ToList();

        return Result.Success(RosterResultMapper.EvaluateAndMapResult(
            ctx.Roster,
            ctx.Company,
            relevantEmployees,
            ctx.DemandSlots,
            ctx.BoundaryContexts));
    }
}
