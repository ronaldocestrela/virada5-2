using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para envio de solicitação de troca de turno a partir de um link tokenizado validado.
/// </summary>
public sealed class RequestSharedShiftSwapUseCase
{
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IScopedTenantContext _scopedTenantContext;
    private readonly IRosterRepository _rosterRepository;
    private readonly RequestShiftSwapUseCase _requestShiftSwapUseCase;
    private readonly IResultTelemetry? _telemetry;

    public RequestSharedShiftSwapUseCase(
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        IRosterRepository rosterRepository,
        RequestShiftSwapUseCase requestShiftSwapUseCase,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(shareLinkService);
        ArgumentNullException.ThrowIfNull(scopedTenantContext);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(requestShiftSwapUseCase);

        _shareLinkService = shareLinkService;
        _scopedTenantContext = scopedTenantContext;
        _rosterRepository = rosterRepository;
        _requestShiftSwapUseCase = requestShiftSwapUseCase;
        _telemetry = telemetry;
    }

    public Task<Result<ShiftSwapActionResultDto>> ExecuteAsync(
        RequestSharedShiftSwapCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "RequestSharedShiftSwap",
            _scopedTenantContext.ScopedTenantId,
            _telemetry,
            telemetryContext => ExecuteCoreAsync(command, telemetryContext, ct));

    private async Task<Result<ShiftSwapActionResultDto>> ExecuteCoreAsync(
        RequestSharedShiftSwapCommand command,
        TelemetryExecutionContext telemetryContext,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de solicitação de troca inválido."));
        }

        var scopeResult = await SharedTokenValidationHelper.OpenValidatedScopeAsync(
            command.Token,
            _shareLinkService,
            _scopedTenantContext,
            _rosterRepository,
            ct);

        if (scopeResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(scopeResult.Error);
        }

        using var scope = scopeResult.Value;
        telemetryContext.SetTenantId(scope.Payload.TenantId);

        return await _requestShiftSwapUseCase.ExecuteAsync(
            new RequestShiftSwapCommand(
                scope.Payload.RosterId,
                command.RequesterEmployeeId,
                command.RequesterShiftId,
                command.TargetEmployeeId,
                command.TargetShiftId,
                command.Reason,
                RequirePeerApproval: true,
                CompanyId: scope.Payload.CompanyId),
            ct);
    }
}
