using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters.Engine;

internal sealed record LoadedRosterContext(
    Roster Roster,
    Company Company,
    IReadOnlyList<Employee> ActiveEmployees,
    IReadOnlyList<Employee> AllRelevantEmployees,
    IReadOnlyList<DemandSlot> DemandSlots,
    IReadOnlyList<Roster> AdjacentRosters,
    IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> BoundaryContexts);

internal static class RosterResultMapper
{
    private const int ForwardSundayLookaheadDays = 28;

    internal static DateOnly NormalizeToMonday(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    internal static async Task<Result<LoadedRosterContext>> LoadRosterContextAsync(
        Guid rosterId,
        bool requireDraft,
        string emptyTenantErrorCode,
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        CancellationToken ct)
    {
        if (currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<LoadedRosterContext>(
                Error.Validation(emptyTenantErrorCode, "O identificador do tenant autenticado é obrigatório."));
        }

        if (rosterId == Guid.Empty)
        {
            return Result.Failure<LoadedRosterContext>(
                Error.Validation("Roster.InvalidId", "O identificador da escala é obrigatório."));
        }

        var roster = await rosterRepository.GetByIdWithShiftsAsync(rosterId, ct);
        if (roster is null)
        {
            return Result.Failure<LoadedRosterContext>(
                Error.NotFound("Roster.NotFound", "Escala não encontrada para o tenant atual."));
        }

        if (requireDraft && roster.Status != RosterStatus.Draft)
        {
            return Result.Failure<LoadedRosterContext>(
                Error.Conflict("Roster.NotEditable", "Somente escalas em rascunho podem ser ajustadas manualmente."));
        }

        var company = await companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<LoadedRosterContext>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var activeEmployees = await employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var allRelevantEmployees = await MergeRelevantEmployeesAsync(roster, activeEmployees, employeeRepository, ct);
        var demandSlots = await demandSlotRepository.GetByCompanyAsync(company.Id, ct);

        var adjacentRosters = await rosterRepository.GetByWeekRangeAsync(
            company.Id,
            roster.WeekStartDate.AddDays(-RosterBoundaryContextBuilder.LookbackDays),
            roster.WeekStartDate.AddDays(ForwardSundayLookaheadDays),
            ct);

        var boundaryResult = RosterBoundaryContextBuilder.Build(
            roster.WeekStartDate,
            adjacentRosters,
            allRelevantEmployees.Select(e => e.Id),
            roster.Id);

        if (boundaryResult.IsFailure)
        {
            return Result.Failure<LoadedRosterContext>(boundaryResult.Error);
        }

        return Result.Success(new LoadedRosterContext(
            roster,
            company,
            activeEmployees,
            allRelevantEmployees,
            demandSlots,
            adjacentRosters,
            boundaryResult.Value));
    }

    internal static async Task<IReadOnlyList<Employee>> MergeRelevantEmployeesAsync(
        Roster roster,
        IReadOnlyList<Employee> activeEmployees,
        IEmployeeRepository employeeRepository,
        CancellationToken ct)
    {
        var missingIds = roster.Shifts
            .Select(s => s.EmployeeId)
            .Distinct()
            .Where(id => activeEmployees.All(e => e.Id != id))
            .ToList();

        if (missingIds.Count == 0)
        {
            return activeEmployees;
        }

        var extraEmployees = await employeeRepository.GetByIdsAsync(missingIds, ct);
        return activeEmployees.Concat(extraEmployees).ToList();
    }

    internal static GeneratedRosterResultDto EvaluateAndMapResult(
        Roster roster,
        Company company,
        IReadOnlyList<Employee> allRelevantEmployees,
        IReadOnlyList<DemandSlot> demandSlots,
        IReadOnlyDictionary<Guid, EmployeeWeekBoundaryContext> boundaryContexts,
        bool replacedPreviousDraft = false)
    {
        var planningContext = RosterDemandAndTemplateResolver.BuildContext(
            company,
            roster.WeekStartDate,
            allRelevantEmployees,
            demandSlots,
            explicitTemplates: null,
            explicitStaffingRules: null);

        var complianceReport = RosterComplianceEvaluator.Evaluate(
            roster,
            company,
            allRelevantEmployees,
            planningContext,
            boundaryContexts);

        return MapToResultDto(
            roster,
            company,
            allRelevantEmployees,
            planningContext,
            complianceReport,
            replacedPreviousDraft);
    }

    internal static GeneratedRosterResultDto MapToResultDto(
        Roster roster,
        Company company,
        IReadOnlyList<Employee> employees,
        RosterPlanningContext planningContext,
        RosterComplianceReportDto complianceReport,
        bool replacedPreviousDraft = false)
    {
        var employeeNames = employees.ToDictionary(e => e.Id, e => e.Name);
        var shiftDtos = roster.Shifts
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ThenBy(s => s.AssignedRole)
            .Select(s => new GeneratedShiftDto(
                s.Id,
                s.EmployeeId,
                employeeNames.GetValueOrDefault(s.EmployeeId, string.Empty),
                s.Date,
                s.Date.DayOfWeek,
                s.StartTime,
                s.EndTime,
                s.AssignedRole,
                s.CoverageOrigin))
            .ToList()
            .AsReadOnly();

        return new GeneratedRosterResultDto(
            roster.Id,
            company.Id,
            company.Name,
            roster.WeekStartDate,
            roster.WeekEndDate,
            roster.Status,
            replacedPreviousDraft,
            planningContext.UsedInferredShiftTemplates,
            planningContext.UsedInferredStaffingRules,
            complianceReport,
            shiftDtos);
    }
}

