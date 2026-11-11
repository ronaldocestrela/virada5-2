using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso que lista pares compatíveis (função principal, polivalência e regras CLT) para solicitação de troca de turno.
/// </summary>
public sealed class GetCompatibleSwapOptionsUseCase
{
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IScopedTenantContext _scopedTenantContext;
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetCompatibleSwapOptionsUseCase(
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(shareLinkService);
        ArgumentNullException.ThrowIfNull(scopedTenantContext);
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _shareLinkService = shareLinkService;
        _scopedTenantContext = scopedTenantContext;
        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<IReadOnlyList<CompatibleSwapOptionDto>>> ExecuteAsync(
        GetCompatibleSwapOptionsCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de busca de pares compatíveis inválido."));
        }

        if (command.RequesterEmployeeId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterEmployeeId", "O colaborador solicitante é obrigatório."));
        }

        if (command.RequesterShiftId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterShiftId", "O turno de origem é obrigatório."));
        }

        var scopeResult = await SharedTokenValidationHelper.OpenValidatedScopeAsync(
            command.Token,
            _shareLinkService,
            _scopedTenantContext,
            _rosterRepository,
            ct);

        if (scopeResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(scopeResult.Error);
        }

        using var scope = scopeResult.Value;
        var roster = scope.Roster;

        var requesterShift = roster.Shifts.FirstOrDefault(s => s.Id == command.RequesterShiftId);
        if (requesterShift is null)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.NotFound("Roster.ShiftNotFound", "Turno de origem não encontrado na escala."));
        }

        if (requesterShift.EmployeeId != command.RequesterEmployeeId)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.Conflict("ShiftSwap.ShiftOwnershipMismatch", "O turno informado não pertence ao colaborador selecionado."));
        }

        if (await _shiftSwapRequestRepository.HasActiveRequestForShiftAsync(requesterShift.Id, ct))
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.Conflict("ShiftSwap.ActiveRequestExists", "Já existe uma solicitação de troca pendente envolvendo este turno."));
        }

        var company = await _companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<IReadOnlyList<CompatibleSwapOptionDto>>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(roster.CompanyId, ct);
        var employeeMap = activeEmployees.ToDictionary(e => e.Id);
        if (!employeeMap.ContainsKey(command.RequesterEmployeeId))
        {
            var requesterEmployees = await _employeeRepository.GetByIdsAsync([command.RequesterEmployeeId], ct);
            if (requesterEmployees.Count > 0)
            {
                employeeMap[requesterEmployees[0].Id] = requesterEmployees[0];
            }
        }

        var demandSlots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var adjacentRosters = await _rosterRepository.GetByWeekRangeAsync(
            company.Id,
            roster.WeekStartDate.AddDays(-RosterBoundaryContextBuilder.LookbackDays),
            roster.WeekStartDate.AddDays(ShiftSwapValidationHelper.SwapSundayLookaheadDays),
            ct);

        var rosterSwaps = await _shiftSwapRequestRepository.GetByRosterAsync(roster.Id, ct);
        var busyShiftIds = rosterSwaps
            .Where(r => r.IsPending)
            .SelectMany(r => r.TargetShiftId.HasValue
                ? new[] { r.RequesterShiftId, r.TargetShiftId.Value }
                : new[] { r.RequesterShiftId })
            .ToHashSet();

        var tenantId = _currentUserService.TenantId;
        var options = new List<CompatibleSwapOptionDto>();

        foreach (var targetEmployee in activeEmployees
            .Where(e => e.Id != command.RequesterEmployeeId)
            .OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var candidateShifts = roster.Shifts
                .Where(s => s.EmployeeId == targetEmployee.Id && !busyShiftIds.Contains(s.Id))
                .OrderBy(s => s.Date)
                .ThenBy(s => s.StartTime)
                .ToList();

            foreach (var targetShift in candidateShifts)
            {
                var mutualValidation = ShiftSwapValidationHelper.ValidateLoadedContext(
                    tenantId,
                    company,
                    roster,
                    employeeMap,
                    demandSlots,
                    adjacentRosters,
                    command.RequesterEmployeeId,
                    requesterShift.Id,
                    targetEmployee.Id,
                    targetShift.Id);

                if (mutualValidation.IsSuccess)
                {
                    var ctx = mutualValidation.Value;
                    options.Add(new CompatibleSwapOptionDto(
                        targetEmployee.Id,
                        targetEmployee.Name,
                        targetEmployee.PrimaryRole,
                        targetShift.Id,
                        targetShift.Date,
                        targetShift.StartTime,
                        targetShift.EndTime,
                        targetShift.AssignedRole,
                        IsMutualSwap: true,
                        ctx.RequesterReplacementCoverageOrigin,
                        ctx.TargetReplacementCoverageOrigin,
                        ctx.RequesterShiftDemandLevel,
                        ctx.TargetShiftDemandLevel));
                }
            }

            var targetWorkedDaysCount = roster.Shifts
                .Where(s => s.EmployeeId == targetEmployee.Id)
                .Select(s => s.Date)
                .Distinct()
                .Count();

            var pendingOneWayCheck = ShiftSwapValidationHelper.ValidatePendingOneWayCoverageConflict(
                roster,
                rosterSwaps,
                targetEmployee.Id,
                requesterShift.Date);

            if (targetWorkedDaysCount < Roster.MaxWorkDaysPerWeek && pendingOneWayCheck.IsSuccess)
            {
                var oneWayValidation = ShiftSwapValidationHelper.ValidateLoadedContext(
                    tenantId,
                    company,
                    roster,
                    employeeMap,
                    demandSlots,
                    adjacentRosters,
                    command.RequesterEmployeeId,
                    requesterShift.Id,
                    targetEmployee.Id,
                    targetShiftId: null);

                if (oneWayValidation.IsSuccess)
                {
                    var ctx = oneWayValidation.Value;
                    options.Add(new CompatibleSwapOptionDto(
                        targetEmployee.Id,
                        targetEmployee.Name,
                        targetEmployee.PrimaryRole,
                        TargetShiftId: null,
                        TargetShiftDate: null,
                        TargetShiftStartTime: null,
                        TargetShiftEndTime: null,
                        TargetShiftRole: null,
                        IsMutualSwap: false,
                        ctx.RequesterReplacementCoverageOrigin,
                        TargetReplacementCoverageOrigin: null,
                        ctx.RequesterShiftDemandLevel,
                        TargetShiftDemandLevel: null));
                }
            }
        }

        var ordered = options
            .OrderByDescending(o => o.IsMutualSwap)
            .ThenBy(o => o.RequesterReplacementCoverageOrigin)
            .ThenBy(o => o.TargetEmployeeName, StringComparer.Ordinal)
            .ThenBy(o => o.TargetShiftDate)
            .ToList()
            .AsReadOnly();

        return Result.Success<IReadOnlyList<CompatibleSwapOptionDto>>(ordered);
    }
}
