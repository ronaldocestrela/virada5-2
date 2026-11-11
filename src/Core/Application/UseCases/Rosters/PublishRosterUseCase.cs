using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para validação CLT final (incluindo fronteiras semanais) e publicação de uma escala em rascunho.
/// </summary>
public sealed class PublishRosterUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public PublishRosterUseCase(
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
        PublishRosterCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "PublishRoster",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<GeneratedRosterResultDto>> ExecuteCoreAsync(
        PublishRosterCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation("RosterPublish.InvalidCommand", "Comando de publicação da escala inválido."));
        }

        var loadedResult = await RosterResultMapper.LoadRosterContextAsync(
            command.RosterId,
            requireDraft: false,
            "RosterPublish.EmptyTenant",
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
        var publishResult = ctx.Roster.Publish(ctx.BoundaryContexts.Values);
        if (publishResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(publishResult.Error);
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
