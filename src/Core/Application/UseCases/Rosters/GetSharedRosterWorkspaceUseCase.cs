using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso somente-leitura para exibir turnos, dias exatos de folga e solicitações de troca na tela pública mobile via token.
/// </summary>
public sealed class GetSharedRosterWorkspaceUseCase
{
    private readonly IRosterShareLinkService _shareLinkService;
    private readonly IScopedTenantContext _scopedTenantContext;
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;

    public GetSharedRosterWorkspaceUseCase(
        IRosterShareLinkService shareLinkService,
        IScopedTenantContext scopedTenantContext,
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository)
    {
        ArgumentNullException.ThrowIfNull(shareLinkService);
        ArgumentNullException.ThrowIfNull(scopedTenantContext);
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);

        _shareLinkService = shareLinkService;
        _scopedTenantContext = scopedTenantContext;
        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
    }

    public async Task<Result<SharedRosterWorkspaceDto>> ExecuteAsync(
        GetSharedRosterWorkspaceCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<SharedRosterWorkspaceDto>(
                Error.Validation("RosterShare.InvalidCommand", "Comando de leitura da escala compartilhada inválido."));
        }

        var scopeResult = await SharedTokenValidationHelper.OpenValidatedScopeAsync(
            command.Token,
            _shareLinkService,
            _scopedTenantContext,
            _rosterRepository,
            ct);

        if (scopeResult.IsFailure)
        {
            return Result.Failure<SharedRosterWorkspaceDto>(scopeResult.Error);
        }

        using var scope = scopeResult.Value;
        var payload = scope.Payload;
        var roster = scope.Roster;

        var company = await _companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<SharedRosterWorkspaceDto>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var relevantEmployees = await RosterResultMapper.MergeRelevantEmployeesAsync(
            roster,
            activeEmployees,
            _employeeRepository,
            ct);

        var swapRequests = await _shiftSwapRequestRepository.GetByRosterAsync(roster.Id, ct);
        var extraEmployeeIds = swapRequests
            .SelectMany(r => new[] { r.RequesterEmployeeId, r.TargetEmployeeId })
            .Where(id => relevantEmployees.All(e => e.Id != id))
            .Distinct()
            .ToList();

        if (extraEmployeeIds.Count > 0)
        {
            var extraEmployees = await _employeeRepository.GetByIdsAsync(extraEmployeeIds, ct);
            relevantEmployees = relevantEmployees
                .Concat(extraEmployees)
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .ToList();
        }

        var employeeMap = relevantEmployees.ToDictionary(e => e.Id);
        var shiftMap = roster.Shifts.ToDictionary(s => s.Id);
        var closedDaysSet = company.ClosedDays.ToHashSet();

        var employeeSummaries = relevantEmployees
            .Where(e => e.IsActive || roster.Shifts.Any(s => s.EmployeeId == e.Id))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(employee => BuildEmployeeSummary(employee, roster, closedDaysSet))
            .ToList()
            .AsReadOnly();

        Guid? selectedEmployeeId = null;
        if (command.SelectedEmployeeId is { } requestedId &&
            employeeSummaries.Any(e => e.EmployeeId == requestedId))
        {
            selectedEmployeeId = requestedId;
        }
        else if (employeeSummaries.Count == 1)
        {
            selectedEmployeeId = employeeSummaries[0].EmployeeId;
        }

        var swapDtos = swapRequests
            .Select(request =>
            {
                var requesterName = employeeMap.TryGetValue(request.RequesterEmployeeId, out var reqEmp)
                    ? reqEmp.Name
                    : string.Empty;
                var targetName = employeeMap.TryGetValue(request.TargetEmployeeId, out var tgtEmp)
                    ? tgtEmp.Name
                    : string.Empty;
                shiftMap.TryGetValue(request.RequesterShiftId, out var reqShift);
                Shift? tgtShift = null;
                if (request.TargetShiftId.HasValue)
                {
                    shiftMap.TryGetValue(request.TargetShiftId.Value, out tgtShift);
                }

                return ShiftSwapValidationHelper.MapToDto(
                    request,
                    roster.WeekStartDate,
                    requesterName,
                    targetName,
                    reqShift,
                    tgtShift);
            })
            .ToList()
            .AsReadOnly();

        return Result.Success(new SharedRosterWorkspaceDto(
            roster.Id,
            company.Id,
            company.Name,
            roster.WeekStartDate,
            roster.WeekEndDate,
            roster.Status,
            company.ScheduleModel,
            payload.ExpiresAtUtc,
            selectedEmployeeId,
            employeeSummaries,
            swapDtos));
    }

    private static SharedEmployeeSummaryDto BuildEmployeeSummary(
        Employee employee,
        Roster roster,
        HashSet<DayOfWeek> closedDaysSet)
    {
        var employeeShifts = roster.Shifts
            .Where(s => s.EmployeeId == employee.Id)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .Select(s => new GeneratedShiftDto(
                s.Id,
                s.EmployeeId,
                employee.Name,
                s.Date,
                s.Date.DayOfWeek,
                s.StartTime,
                s.EndTime,
                s.AssignedRole,
                s.CoverageOrigin))
            .ToList()
            .AsReadOnly();

        var workedDates = employeeShifts
            .Select(s => s.Date)
            .ToHashSet();

        var daysOff = new List<SharedDayOffDto>();
        for (var offset = 0; offset < Roster.CycleLengthDays; offset++)
        {
            var date = roster.WeekStartDate.AddDays(offset);
            if (!workedDates.Contains(date))
            {
                daysOff.Add(new SharedDayOffDto(
                    date,
                    date.DayOfWeek,
                    closedDaysSet.Contains(date.DayOfWeek)));
            }
        }

        var secondarySkills = employee.SecondarySkills
            .Select(s => new RosterEmployeeSecondarySkillDto(s.Role, s.Proficiency))
            .ToList()
            .AsReadOnly();

        return new SharedEmployeeSummaryDto(
            employee.Id,
            employee.Name,
            employee.PrimaryRole,
            secondarySkills,
            employeeShifts,
            daysOff.AsReadOnly(),
            workedDates.Count,
            daysOff.Count);
    }
}
