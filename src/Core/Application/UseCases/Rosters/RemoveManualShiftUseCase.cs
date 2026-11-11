using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para remoção manual de um turno em uma escala em rascunho.
/// </summary>
public sealed class RemoveManualShiftUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveManualShiftUseCase(
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
        RemoveManualShiftCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.InvalidCommand", "Comando de remoção de turno inválido."));
        }

        if (command.ShiftId == Guid.Empty)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.EmptyShiftId", "O identificador do turno é obrigatório."));
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
        var removeResult = ctx.Roster.RemoveShift(command.ShiftId);
        if (removeResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(removeResult.Error);
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
