using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters.Engine;

internal sealed record ValidatedSharedRosterScope(
    RosterShareTokenPayloadDto Payload,
    Roster Roster,
    IDisposable TenantScope) : IDisposable
{
    public void Dispose() => TenantScope.Dispose();
}

internal static class SharedTokenValidationHelper
{
    internal static async Task<Result<ValidatedSharedRosterScope>> OpenValidatedScopeAsync(
        string? token,
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        IRosterRepository rosterRepository,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<ValidatedSharedRosterScope>(
                Error.Validation("RosterShare.EmptyToken", "O token de compartilhamento é obrigatório."));
        }

        var tokenResult = shareLinkService.ValidateToken(token.Trim());
        if (tokenResult.IsFailure)
        {
            return Result.Failure<ValidatedSharedRosterScope>(tokenResult.Error);
        }

        var payload = tokenResult.Value;
        var tenantScope = scopedTenantContext.BeginScope(payload.TenantId);

        try
        {
            var roster = await rosterRepository.GetByIdWithShiftsAsync(payload.RosterId, ct);
            if (roster is null)
            {
                tenantScope.Dispose();
                return Result.Failure<ValidatedSharedRosterScope>(
                    Error.NotFound("Roster.NotFound", "Escala não encontrada para o link informado."));
            }

            if (roster.CompanyId != payload.CompanyId || roster.WeekStartDate != payload.WeekStartDate)
            {
                tenantScope.Dispose();
                return Result.Failure<ValidatedSharedRosterScope>(
                    Error.Validation("RosterShare.InvalidToken", "O link de compartilhamento não corresponde à escala informada."));
            }

            if (roster.Status == RosterStatus.Archived)
            {
                tenantScope.Dispose();
                return Result.Failure<ValidatedSharedRosterScope>(
                    Error.Conflict("RosterShare.ArchivedRoster", "Esta escala foi arquivada e não está mais disponível."));
            }

            return Result.Success(new ValidatedSharedRosterScope(payload, roster, tenantScope));
        }
        catch
        {
            tenantScope.Dispose();
            throw;
        }
    }

    internal static async Task<Result<ShiftSwapRequest>> ResolveRosterSwapRequestAsync(
        Guid shiftSwapRequestId,
        RosterShareTokenPayloadDto payload,
        IShiftSwapRequestRepository swapRepository,
        CancellationToken ct)
    {
        if (shiftSwapRequestId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.Validation("ShiftSwap.InvalidRequestId", "O identificador da solicitação de troca é obrigatório."));
        }

        var swapRequest = await swapRepository.GetByIdAsync(shiftSwapRequestId, ct);
        if (swapRequest is null ||
            swapRequest.RosterId != payload.RosterId ||
            swapRequest.CompanyId != payload.CompanyId)
        {
            return Result.Failure<ShiftSwapRequest>(
                Error.NotFound("ShiftSwap.NotFound", "Solicitação de troca não encontrada para esta escala."));
        }

        return Result.Success(swapRequest);
    }
}

