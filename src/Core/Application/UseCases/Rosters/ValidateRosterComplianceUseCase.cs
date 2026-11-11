using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso somente-leitura para validar conformidade CLT, cobertura e alertas de inconsistência de uma escala.
/// </summary>
public sealed class ValidateRosterComplianceUseCase
{
    private readonly IRosterRepository _rosterRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;

    public ValidateRosterComplianceUseCase(
        IRosterRepository rosterRepository,
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _rosterRepository = rosterRepository;
        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<RosterComplianceReportDto>> ExecuteAsync(
        ValidateRosterComplianceCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<RosterComplianceReportDto>(
                Error.Validation("RosterCompliance.InvalidCommand", "Comando de validação de escala inválido."));
        }

        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<RosterComplianceReportDto>(
                Error.Validation("RosterCompliance.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (command.RosterId == Guid.Empty)
        {
            return Result.Failure<RosterComplianceReportDto>(
                Error.Validation("RosterCompliance.InvalidRosterId", "O identificador da escala é obrigatório."));
        }

        IReadOnlyList<ResolvedShiftTemplate>? resolvedTemplates = null;
        if (command.ShiftTemplates is not null)
        {
            var templatesResult = RosterDemandAndTemplateResolver.ValidateExplicitTemplates(
                command.ShiftTemplates,
                "RosterCompliance.InvalidShiftTemplate");
            if (templatesResult.IsFailure)
            {
                return Result.Failure<RosterComplianceReportDto>(templatesResult.Error);
            }

            resolvedTemplates = templatesResult.Value;
        }

        IReadOnlyDictionary<StationRole, StationStaffingRuleDto>? resolvedStaffingRules = null;
        if (command.StaffingRules is not null)
        {
            var staffingResult = RosterDemandAndTemplateResolver.ValidateExplicitStaffingRules(
                command.StaffingRules,
                "RosterCompliance.InvalidStaffingRule");
            if (staffingResult.IsFailure)
            {
                return Result.Failure<RosterComplianceReportDto>(staffingResult.Error);
            }

            resolvedStaffingRules = staffingResult.Value;
        }

        var roster = await _rosterRepository.GetByIdWithShiftsAsync(command.RosterId, ct);
        if (roster is null)
        {
            return Result.Failure<RosterComplianceReportDto>(
                Error.NotFound("Roster.NotFound", "Escala não encontrada para o tenant atual."));
        }

        var company = await _companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<RosterComplianceReportDto>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var shiftEmployeeIds = roster.Shifts.Select(s => s.EmployeeId).Distinct().ToList();
        var missingActiveIds = shiftEmployeeIds
            .Where(id => activeEmployees.All(e => e.Id != id))
            .ToList();

        var allRelevantEmployees = new List<Employee>(activeEmployees);
        if (missingActiveIds.Count > 0)
        {
            var extraEmployees = await _employeeRepository.GetByIdsAsync(missingActiveIds, ct);
            allRelevantEmployees.AddRange(extraEmployees);
        }

        var demandSlots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var adjacentRosters = await _rosterRepository.GetByWeekRangeAsync(
            company.Id,
            roster.WeekStartDate.AddDays(-RosterBoundaryContextBuilder.LookbackDays),
            roster.WeekStartDate.AddDays(RosterBoundaryContextBuilder.LookaheadDays),
            ct);

        var boundaryResult = RosterBoundaryContextBuilder.Build(
            roster.WeekStartDate,
            adjacentRosters,
            allRelevantEmployees.Select(e => e.Id),
            roster.Id);

        if (boundaryResult.IsFailure)
        {
            return Result.Failure<RosterComplianceReportDto>(boundaryResult.Error);
        }

        var planningContext = RosterDemandAndTemplateResolver.BuildContext(
            company,
            roster.WeekStartDate,
            allRelevantEmployees,
            demandSlots,
            resolvedTemplates,
            resolvedStaffingRules);

        var report = RosterComplianceEvaluator.Evaluate(
            roster,
            company,
            allRelevantEmployees,
            planningContext,
            boundaryResult.Value);

        return Result.Success(report);
    }
}
