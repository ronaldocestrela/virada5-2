using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para resposta (aceite ou recusa) do colega destinatário a partir do link tokenizado validado.
/// </summary>
public sealed class RespondSharedShiftSwapPeerUseCase
{
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IScopedTenantContext _scopedTenantContext;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly RespondShiftSwapPeerUseCase _respondShiftSwapPeerUseCase;

    public RespondSharedShiftSwapPeerUseCase(
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        RespondShiftSwapPeerUseCase respondShiftSwapPeerUseCase)
    {
        ArgumentNullException.ThrowIfNull(shareLinkService);
        ArgumentNullException.ThrowIfNull(scopedTenantContext);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(respondShiftSwapPeerUseCase);

        _shareLinkService = shareLinkService;
        _scopedTenantContext = scopedTenantContext;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _respondShiftSwapPeerUseCase = respondShiftSwapPeerUseCase;
    }

    public async Task<Result<ShiftSwapActionResultDto>> ExecuteAsync(
        RespondSharedShiftSwapPeerCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de resposta do colega inválido."));
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

        var swapResult = await SharedTokenValidationHelper.ResolveRosterSwapRequestAsync(
            command.ShiftSwapRequestId,
            scope.Payload,
            _shiftSwapRequestRepository,
            ct);

        if (swapResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(swapResult.Error);
        }

        return await _respondShiftSwapPeerUseCase.ExecuteAsync(
            new RespondShiftSwapPeerCommand(
                swapResult.Value.Id,
                command.ActingEmployeeId,
                command.Accept,
                command.Notes),
            ct);
    }
}
