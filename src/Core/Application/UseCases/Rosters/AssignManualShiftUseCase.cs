using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para alocação manual de um turno em uma escala em rascunho com validação CLT integral.
/// </summary>
public sealed class AssignManualShiftUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public AssignManualShiftUseCase(
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
        AssignManualShiftCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "AssignManualShift",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<GeneratedRosterResultDto>> ExecuteCoreAsync(
        AssignManualShiftCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.InvalidCommand", "Comando de alocação manual inválido."));
        }

        if (command.EmployeeId == Guid.Empty)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterAdjustment.EmptyEmployeeId", "O colaborador do turno é obrigatório."));
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

        if (!ctx.Company.IsOpenOn(command.Date.DayOfWeek))
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("Roster.ShiftOnClosedDay", "Não é possível alocar turno em dia de fechamento do estabelecimento."));
        }

        var employee = ctx.AllRelevantEmployees.FirstOrDefault(e => e.Id == command.EmployeeId)
            ?? await _employeeRepository.GetByIdAsync(command.EmployeeId, ct);

        if (employee is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.NotFound("Employee.NotFound", "Colaborador não encontrado para o tenant atual."));
        }

        var previewShiftResult = Shift.Create(
            ctx.Roster.TenantId,
            ctx.Roster.Id,
            employee.Id,
            command.Date,
            command.StartTime,
            command.EndTime,
            command.AssignedRole,
            Domain.Enums.CoverageOrigin.PrimaryRole);

        if (previewShiftResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(previewShiftResult.Error);
        }

        var demandLevel = RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(
            ctx.Company,
            previewShiftResult.Value,
            ctx.DemandSlots);

        ctx.BoundaryContexts.TryGetValue(employee.Id, out var boundaryContext);

        var assignResult = ctx.Roster.AssignShift(
            employee,
            command.Date,
            command.StartTime,
            command.EndTime,
            command.AssignedRole,
            demandLevel,
            boundaryContext);

        if (assignResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(assignResult.Error);
        }

        _rosterRepository.Update(ctx.Roster);
        await _unitOfWork.SaveChangesAsync(ct);

        var relevantEmployees = ctx.AllRelevantEmployees.Any(e => e.Id == employee.Id)
            ? ctx.AllRelevantEmployees
            : ctx.AllRelevantEmployees.Append(employee).ToList();

        return Result.Success(RosterResultMapper.EvaluateAndMapResult(
            ctx.Roster,
            ctx.Company,
            relevantEmployees,
            ctx.DemandSlots,
            ctx.BoundaryContexts));
    }
}
