using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters.Engine;

internal sealed record ShiftSwapEvaluationContext(
    Company Company,
    Roster Roster,
    Employee RequesterEmployee,
    Employee TargetEmployee,
    Shift RequesterShift,
    Shift? TargetShift,
    DemandClassification RequesterShiftDemandLevel,
    DemandClassification? TargetShiftDemandLevel,
    CoverageOrigin RequesterReplacementCoverageOrigin,
    CoverageOrigin? TargetReplacementCoverageOrigin,
    EmployeeWeekBoundaryContext RequesterBoundaryContext,
    EmployeeWeekBoundaryContext TargetBoundaryContext);

internal static class ShiftSwapValidationHelper
{
    private const int SwapSundayLookaheadDays = 28;

    internal static async Task<Result<ShiftSwapEvaluationContext>> ValidateAsync(
        Guid rosterId,
        Guid requesterEmployeeId,
        Guid requesterShiftId,
        Guid targetEmployeeId,
        Guid? targetShiftId,
        Guid? expectedCompanyId,
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        CancellationToken ct)
    {
        var tenantId = currentUserService.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Validation("ShiftSwap.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (requesterEmployeeId == targetEmployeeId)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Validation("ShiftSwapRequest.SameEmployee", "Não é possível solicitar troca de turno consigo mesmo."));
        }

        if (targetShiftId.HasValue && targetShiftId.Value == requesterShiftId)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Validation("ShiftSwapRequest.SameShift", "Os turnos informados para troca devem ser distintos."));
        }

        var roster = await rosterRepository.GetByIdWithShiftsAsync(rosterId, ct);
        if (roster is null)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.NotFound("Roster.NotFound", "Escala não encontrada para o tenant atual."));
        }

        if (roster.Status == RosterStatus.Archived)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Conflict("Roster.ArchivedNotModifiable", "Escalas arquivadas não permitem troca de turnos."));
        }

        if (expectedCompanyId.HasValue && expectedCompanyId.Value != Guid.Empty && roster.CompanyId != expectedCompanyId.Value)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Conflict("ShiftSwap.CompanyMismatch", "A escala informada não pertence ao estabelecimento especificado."));
        }

        var company = await companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var requesterShift = roster.Shifts.FirstOrDefault(s => s.Id == requesterShiftId);
        if (requesterShift is null)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.NotFound("Roster.ShiftNotFound", "Turno de origem não encontrado na escala."));
        }

        if (requesterShift.EmployeeId != requesterEmployeeId)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Conflict(
                    "ShiftSwap.ShiftOwnershipMismatch",
                    "O turno de origem não pertence mais ao colaborador solicitante."));
        }

        Shift? targetShift = null;
        if (targetShiftId.HasValue)
        {
            targetShift = roster.Shifts.FirstOrDefault(s => s.Id == targetShiftId.Value);
            if (targetShift is null)
            {
                return Result.Failure<ShiftSwapEvaluationContext>(
                    Error.NotFound("Roster.ShiftNotFound", "Turno de destino não encontrado na escala."));
            }

            if (targetShift.EmployeeId != targetEmployeeId)
            {
                return Result.Failure<ShiftSwapEvaluationContext>(
                    Error.Conflict(
                        "ShiftSwap.ShiftOwnershipMismatch",
                        "O turno de destino não pertence mais ao colaborador destinatário."));
            }
        }

        var employees = await employeeRepository.GetByIdsAsync([requesterEmployeeId, targetEmployeeId], ct);
        var requesterEmployee = employees.FirstOrDefault(e => e.Id == requesterEmployeeId);
        var targetEmployee = employees.FirstOrDefault(e => e.Id == targetEmployeeId);

        if (requesterEmployee is null || targetEmployee is null)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.NotFound("Employee.NotFound", "Um ou mais colaboradores da troca não foram encontrados."));
        }

        if (requesterEmployee.CompanyId != company.Id || targetEmployee.CompanyId != company.Id)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(
                Error.Conflict("Roster.EmployeeCompanyMismatch", "Os colaboradores devem pertencer ao estabelecimento da escala."));
        }

        var demandSlots = await demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var requesterDemand = RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(company, requesterShift, demandSlots);
        DemandClassification? targetDemand = targetShift is not null
            ? RosterDemandAndTemplateResolver.EvaluateShiftDemandClassification(company, targetShift, demandSlots)
            : null;

        var adjacentRosters = await rosterRepository.GetByWeekRangeAsync(
            company.Id,
            roster.WeekStartDate.AddDays(-RosterBoundaryContextBuilder.LookbackDays),
            roster.WeekStartDate.AddDays(SwapSundayLookaheadDays),
            ct);

        var boundaryResult = RosterBoundaryContextBuilder.Build(
            roster.WeekStartDate,
            adjacentRosters,
            [requesterEmployee.Id, targetEmployee.Id],
            roster.Id);

        if (boundaryResult.IsFailure)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(boundaryResult.Error);
        }

        var requesterBoundary = boundaryResult.Value[requesterEmployee.Id];
        var targetBoundary = boundaryResult.Value[targetEmployee.Id];

        CoverageOrigin requesterReplacementOrigin;
        CoverageOrigin? targetReplacementOrigin = null;

        if (targetShift is not null)
        {
            var swapValidation = roster.ValidateSwapShiftEmployees(
                requesterShift.Id,
                targetEmployee,
                targetShift.Id,
                requesterEmployee,
                requesterDemand,
                targetDemand!.Value,
                targetBoundary,
                requesterBoundary);

            if (swapValidation.IsFailure)
            {
                return Result.Failure<ShiftSwapEvaluationContext>(swapValidation.Error);
            }

            requesterReplacementOrigin = swapValidation.Value.FirstOrigin;
            targetReplacementOrigin = swapValidation.Value.SecondOrigin;
        }
        else
        {
            var reassignValidation = roster.ValidateReassignShiftEmployee(
                requesterShift.Id,
                targetEmployee,
                requesterDemand,
                targetBoundary);

            if (reassignValidation.IsFailure)
            {
                return Result.Failure<ShiftSwapEvaluationContext>(reassignValidation.Error);
            }

            requesterReplacementOrigin = reassignValidation.Value;
        }

        var forwardSundayResult = ValidateMultiWeekSundayRotationForSwap(
            roster,
            adjacentRosters,
            requesterEmployee,
            targetEmployee,
            requesterShift,
            targetShift,
            requesterReplacementOrigin,
            targetReplacementOrigin);

        if (forwardSundayResult.IsFailure)
        {
            return Result.Failure<ShiftSwapEvaluationContext>(forwardSundayResult.Error);
        }

        return Result.Success(new ShiftSwapEvaluationContext(
            company,
            roster,
            requesterEmployee,
            targetEmployee,
            requesterShift,
            targetShift,
            requesterDemand,
            targetDemand,
            requesterReplacementOrigin,
            targetReplacementOrigin,
            requesterBoundary,
            targetBoundary));
    }

    internal static ShiftSwapActionResultDto BuildActionResult(
        ShiftSwapRequest request,
        ShiftSwapEvaluationContext context)
    {
        var dto = MapToDto(
            request,
            context.Roster.WeekStartDate,
            context.RequesterEmployee.Name,
            context.TargetEmployee.Name,
            context.RequesterShift,
            context.TargetShift);

        var summary = new ShiftSwapValidationSummaryDto(
            IsCltCompliant: true,
            context.RequesterShiftDemandLevel,
            context.TargetShiftDemandLevel,
            context.RequesterReplacementCoverageOrigin,
            context.TargetReplacementCoverageOrigin);

        return new ShiftSwapActionResultDto(dto, summary);
    }

    internal static async Task<ShiftSwapRequestDto> LoadAndMapDtoAsync(
        ShiftSwapRequest request,
        IRosterRepository rosterRepository,
        IEmployeeRepository employeeRepository,
        CancellationToken ct)
    {
        var roster = await rosterRepository.GetByIdWithShiftsAsync(request.RosterId, ct);
        var employees = await employeeRepository.GetByIdsAsync(
            [request.RequesterEmployeeId, request.TargetEmployeeId],
            ct);

        var requesterName = employees.FirstOrDefault(e => e.Id == request.RequesterEmployeeId)?.Name ?? string.Empty;
        var targetName = employees.FirstOrDefault(e => e.Id == request.TargetEmployeeId)?.Name ?? string.Empty;
        var reqShift = roster?.Shifts.FirstOrDefault(s => s.Id == request.RequesterShiftId);
        var tgtShift = request.TargetShiftId.HasValue
            ? roster?.Shifts.FirstOrDefault(s => s.Id == request.TargetShiftId.Value)
            : null;

        return MapToDto(
            request,
            roster?.WeekStartDate ?? default,
            requesterName,
            targetName,
            reqShift,
            tgtShift);
    }

    internal static ShiftSwapRequestDto MapToDto(
        ShiftSwapRequest request,
        DateOnly weekStartDate,
        string requesterEmployeeName,
        string targetEmployeeName,
        Shift? requesterShift,
        Shift? targetShift) =>
        new(
            request.Id,
            request.CompanyId,
            request.RosterId,
            weekStartDate,
            request.RequesterEmployeeId,
            requesterEmployeeName,
            request.RequesterShiftId,
            requesterShift?.Date ?? default,
            requesterShift?.StartTime ?? default,
            requesterShift?.EndTime ?? default,
            requesterShift?.AssignedRole ?? default,
            request.TargetEmployeeId,
            targetEmployeeName,
            request.TargetShiftId,
            targetShift?.Date,
            targetShift?.StartTime,
            targetShift?.EndTime,
            targetShift?.AssignedRole,
            request.Status,
            request.Reason,
            request.DecisionNotes,
            request.RequestedAtUtc,
            request.PeerRespondedAtUtc,
            request.ReviewedByUserId,
            request.ReviewedAtUtc);

    private static Result ValidateMultiWeekSundayRotationForSwap(
        Roster targetRoster,
        IReadOnlyList<Roster> adjacentRosters,
        Employee requesterEmployee,
        Employee targetEmployee,
        Shift requesterShift,
        Shift? targetShift,
        CoverageOrigin requesterReplacementOrigin,
        CoverageOrigin? targetReplacementOrigin)
    {
        var futureRostersExist = adjacentRosters.Any(r =>
            r.Status != RosterStatus.Archived &&
            r.WeekStartDate > targetRoster.WeekStartDate);

        if (!futureRostersExist)
        {
            return Result.Success();
        }

        var simulatedWeekResult = Roster.Create(
            targetRoster.TenantId,
            targetRoster.CompanyId,
            targetRoster.WeekStartDate,
            targetRoster.SundayPolicy);

        if (simulatedWeekResult.IsFailure)
        {
            return Result.Failure(simulatedWeekResult.Error);
        }

        var simulatedWeek = simulatedWeekResult.Value;

        foreach (var shift in targetRoster.Shifts)
        {
            Employee assignedEmployee;
            if (shift.Id == requesterShift.Id)
            {
                assignedEmployee = targetEmployee;
            }
            else if (targetShift is not null && shift.Id == targetShift.Id)
            {
                assignedEmployee = requesterEmployee;
            }
            else if (shift.EmployeeId == requesterEmployee.Id)
            {
                assignedEmployee = requesterEmployee;
            }
            else if (shift.EmployeeId == targetEmployee.Id)
            {
                assignedEmployee = targetEmployee;
            }
            else
            {
                continue;
            }

            var demandForProjection = shift.CoverageOrigin == CoverageOrigin.SecondarySkill
                ? DemandClassification.Moderate
                : DemandClassification.Idle;

            if (shift.Id == requesterShift.Id && requesterReplacementOrigin == CoverageOrigin.SecondarySkill)
            {
                demandForProjection = DemandClassification.Moderate;
            }
            else if (targetShift is not null && shift.Id == targetShift.Id && targetReplacementOrigin == CoverageOrigin.SecondarySkill)
            {
                demandForProjection = DemandClassification.Moderate;
            }

            var assignResult = simulatedWeek.AssignShift(
                assignedEmployee,
                shift.Date,
                shift.StartTime,
                shift.EndTime,
                shift.AssignedRole,
                demandForProjection);

            if (assignResult.IsFailure)
            {
                return Result.Failure(assignResult.Error);
            }
        }

        var chronologicalSequence = adjacentRosters
            .Where(r => r.Status != RosterStatus.Archived && r.Id != targetRoster.Id && r.WeekStartDate != targetRoster.WeekStartDate)
            .Append(simulatedWeek)
            .GroupBy(r => r.WeekStartDate)
            .Select(g => g.First())
            .OrderBy(r => r.WeekStartDate)
            .ToList();

        var requesterCheck = Roster.ValidateMultiWeekSundayRotation(chronologicalSequence, requesterEmployee.Id);
        if (requesterCheck.IsFailure)
        {
            return requesterCheck;
        }

        return Roster.ValidateMultiWeekSundayRotation(chronologicalSequence, targetEmployee.Id);
    }
}
