using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Common;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para consulta da fila de solicitações de troca pendentes e avaliação CLT em tempo real para o gestor.
/// </summary>
public sealed class GetPendingShiftSwapsUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetPendingShiftSwapsUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ManagerShiftSwapQueueDto>> ExecuteAsync(
        GetPendingShiftSwapsCommand? command = null,
        CancellationToken ct = default)
    {
        var companyResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            command?.CompanyId,
            _companyRepository,
            _currentUserService,
            "ShiftSwapQueue.EmptyTenant",
            ct);

        if (companyResult.IsFailure)
        {
            return Result.Failure<ManagerShiftSwapQueueDto>(companyResult.Error);
        }

        var (_, company) = companyResult.Value;
        var includeCltEvaluation = command?.IncludeCltEvaluation ?? true;

        var pendingRequests = await _shiftSwapRequestRepository.GetPendingByCompanyAsync(company.Id, ct);
        var pendingManagerCount = pendingRequests.Count(r => r.Status == ShiftSwapStatus.PendingManagerApproval);
        var pendingPeerCount = pendingRequests.Count(r => r.Status == ShiftSwapStatus.PendingPeerApproval);

        if (pendingRequests.Count == 0 || !includeCltEvaluation)
        {
            return Result.Success(new ManagerShiftSwapQueueDto(
                company.Id,
                pendingManagerCount,
                pendingPeerCount,
                []));
        }

        var rosterCache = new Dictionary<Guid, Roster>();
        foreach (var rosterId in pendingRequests.Select(r => r.RosterId).Distinct())
        {
            var roster = await _rosterRepository.GetByIdWithShiftsAsync(rosterId, ct);
            if (roster is not null)
            {
                rosterCache[rosterId] = roster;
            }
        }

        var employeeIds = pendingRequests
            .SelectMany(r => new[] { r.RequesterEmployeeId, r.TargetEmployeeId })
            .Distinct()
            .ToList();
        var employees = await _employeeRepository.GetByIdsAsync(employeeIds, ct);
        var employeeMap = employees.ToDictionary(e => e.Id);

        var items = new List<ManagerShiftSwapItemDto>(pendingRequests.Count);
        foreach (var request in pendingRequests)
        {
            rosterCache.TryGetValue(request.RosterId, out var roster);
            var requesterName = employeeMap.TryGetValue(request.RequesterEmployeeId, out var reqEmp)
                ? reqEmp.Name
                : string.Empty;
            var targetName = employeeMap.TryGetValue(request.TargetEmployeeId, out var tgtEmp)
                ? tgtEmp.Name
                : string.Empty;
            var reqShift = roster?.Shifts.FirstOrDefault(s => s.Id == request.RequesterShiftId);
            var tgtShift = request.TargetShiftId.HasValue
                ? roster?.Shifts.FirstOrDefault(s => s.Id == request.TargetShiftId.Value)
                : null;

            var dto = ShiftSwapValidationHelper.MapToDto(
                request,
                roster?.WeekStartDate ?? default,
                requesterName,
                targetName,
                reqShift,
                tgtShift);

            var validationResult = await ShiftSwapValidationHelper.ValidateAsync(
                request.RosterId,
                request.RequesterEmployeeId,
                request.RequesterShiftId,
                request.TargetEmployeeId,
                request.TargetShiftId,
                request.CompanyId,
                _companyRepository,
                _employeeRepository,
                _demandSlotRepository,
                _rosterRepository,
                _currentUserService,
                ct);

            var isCltCompliant = validationResult.IsSuccess;
            var canApproveNow = request.Status == ShiftSwapStatus.PendingManagerApproval && isCltCompliant;
            var blockMessage = validationResult.IsFailure ? validationResult.Error.Message : null;

            ShiftSwapValidationSummaryDto? summary = validationResult.IsSuccess
                ? new ShiftSwapValidationSummaryDto(
                    IsCltCompliant: true,
                    validationResult.Value.RequesterShiftDemandLevel,
                    validationResult.Value.TargetShiftDemandLevel,
                    validationResult.Value.RequesterReplacementCoverageOrigin,
                    validationResult.Value.TargetReplacementCoverageOrigin)
                : null;

            items.Add(new ManagerShiftSwapItemDto(
                dto,
                canApproveNow,
                isCltCompliant,
                blockMessage,
                summary));
        }

        var orderedItems = items
            .OrderBy(i => i.SwapRequest.Status == ShiftSwapStatus.PendingManagerApproval ? 0 : 1)
            .ThenBy(i => i.SwapRequest.RequestedAtUtc)
            .ToList()
            .AsReadOnly();

        return Result.Success(new ManagerShiftSwapQueueDto(
            company.Id,
            pendingManagerCount,
            pendingPeerCount,
            orderedItems));
    }
}
