using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para geração de link tokenizado de compartilhamento da escala semanal.
/// </summary>
public sealed class GenerateRosterShareLinkUseCase
{
    private const int MinExpirationDays = 1;
    private const int MaxExpirationDays = 90;

    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IResultTelemetry? _telemetry;

    public GenerateRosterShareLinkUseCase(
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IRosterShareLinkService shareLinkService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(shareLinkService);

        _rosterRepository = rosterRepository;
        _currentUserService = currentUserService;
        _shareLinkService = shareLinkService;
        _telemetry = telemetry;
    }

    public Task<Result<RosterShareLinkDto>> ExecuteAsync(
        GenerateRosterShareLinkCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "GenerateRosterShareLink",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<RosterShareLinkDto>> ExecuteCoreAsync(
        GenerateRosterShareLinkCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Validation("RosterShare.InvalidCommand", "Comando de geração de link inválido."));
        }

        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Validation("RosterShare.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (command.ExpirationDays < MinExpirationDays || command.ExpirationDays > MaxExpirationDays)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Validation(
                    "RosterShare.InvalidExpiration",
                    $"A validade do link deve ficar entre {MinExpirationDays} e {MaxExpirationDays} dias."));
        }

        if (command.RosterId == Guid.Empty)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Validation("RosterShare.InvalidRosterId", "O identificador da escala é obrigatório."));
        }

        var roster = await _rosterRepository.GetByIdWithShiftsAsync(command.RosterId, ct);
        if (roster is null)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.NotFound("Roster.NotFound", "Escala não encontrada para o tenant atual."));
        }

        if (roster.Status == RosterStatus.Archived)
        {
            return Result.Failure<RosterShareLinkDto>(
                Error.Conflict("RosterShare.ArchivedRoster", "Escalas arquivadas não podem gerar links de compartilhamento."));
        }

        return _shareLinkService.CreateShareLink(
            _currentUserService.TenantId,
            roster.CompanyId,
            roster.Id,
            roster.WeekStartDate,
            TimeSpan.FromDays(command.ExpirationDays));
    }
}
