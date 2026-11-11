using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para cancelamento de solicitação de troca pelo solicitante a partir do link tokenizado validado.
/// </summary>
public sealed class CancelSharedShiftSwapUseCase
{
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IScopedTenantContext _scopedTenantContext;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly RejectShiftSwapUseCase _rejectShiftSwapUseCase;

    public CancelSharedShiftSwapUseCase(
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        RejectShiftSwapUseCase rejectShiftSwapUseCase)
    {
        ArgumentNullException.ThrowIfNull(shareLinkService);
        ArgumentNullException.ThrowIfNull(scopedTenantContext);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(rejectShiftSwapUseCase);

        _shareLinkService = shareLinkService;
        _scopedTenantContext = scopedTenantContext;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _rejectShiftSwapUseCase = rejectShiftSwapUseCase;
    }

    public async Task<Result<ShiftSwapRequestDto>> ExecuteAsync(
        CancelSharedShiftSwapCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapRequestDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de cancelamento inválido."));
        }

        var scopeResult = await SharedTokenValidationHelper.OpenValidatedScopeAsync(
            command.Token,
            _shareLinkService,
            _scopedTenantContext,
            _rosterRepository,
            ct);

        if (scopeResult.IsFailure)
        {
            return Result.Failure<ShiftSwapRequestDto>(scopeResult.Error);
        }

        using var scope = scopeResult.Value;

        var swapResult = await SharedTokenValidationHelper.ResolveRosterSwapRequestAsync(
            command.ShiftSwapRequestId,
            scope.Payload,
            _shiftSwapRequestRepository,
            ct);

        if (swapResult.IsFailure)
        {
            return Result.Failure<ShiftSwapRequestDto>(swapResult.Error);
        }

        return await _rejectShiftSwapUseCase.ExecuteAsync(
            new RejectShiftSwapCommand(
                swapResult.Value.Id,
                command.Notes,
                command.ActingEmployeeId,
                IsCancellationByRequester: true),
            ct);
    }
}
