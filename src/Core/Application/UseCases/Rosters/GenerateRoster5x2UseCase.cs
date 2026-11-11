using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Common;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso de geração automática de escala semanal 5x2 com otimização por pesos e validação CLT.
/// </summary>
public sealed class GenerateRoster5x2UseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateRoster5x2UseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<GeneratedRosterResultDto>> ExecuteAsync(
        GenerateRoster5x2Command command,
        CancellationToken ct = default)
    {
        var commandValidation = ValidateCommand(command);
        if (commandValidation.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(commandValidation.Error);
        }

        var (sundayPolicy, explicitTemplates, explicitStaffingRules) = commandValidation.Value;

        var companyResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            command.CompanyId,
            _companyRepository,
            _currentUserService,
            "RosterGeneration.EmptyTenant",
            ct);

        if (companyResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(companyResult.Error);
        }

        var (tenantId, company) = companyResult.Value;

        var existingRoster = await _rosterRepository.GetByCompanyAndWeekStartAsync(
            company.Id,
            command.WeekStartDate,
            ct);

        if (existingRoster is not null)
        {
            if (existingRoster.Status == RosterStatus.Published)
            {
                return Result.Failure<GeneratedRosterResultDto>(
                    Error.Conflict(
                        "RosterGeneration.PublishedRosterExists",
                        "Já existe uma escala publicada para esta semana."));
            }

            if (!command.ReplaceExistingDraft)
            {
                return Result.Failure<GeneratedRosterResultDto>(
                    Error.Conflict(
                        "RosterGeneration.DraftAlreadyExists",
                        "Já existe um rascunho de escala para esta semana. Defina ReplaceExistingDraft=true para substituí-lo."));
            }
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        if (activeEmployees.Count == 0)
        {
            return Result.Failure<GeneratedRosterResultDto>(
                Error.Validation(
                    "RosterGeneration.NoActiveEmployees",
                    "É necessário possuir ao menos um colaborador ativo para gerar a escala 5x2."));
        }

        var demandSlots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);
        var adjacentRosters = await _rosterRepository.GetByWeekRangeAsync(
            company.Id,
            command.WeekStartDate.AddDays(-RosterBoundaryContextBuilder.LookbackDays),
            command.WeekStartDate.AddDays(RosterBoundaryContextBuilder.LookaheadDays),
            ct);

        var boundaryResult = RosterBoundaryContextBuilder.Build(
            command.WeekStartDate,
            adjacentRosters,
            activeEmployees.Select(e => e.Id),
            existingRoster?.Id);

        if (boundaryResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(boundaryResult.Error);
        }

        var newRosterResult = Roster.Create(
            tenantId,
            company.Id,
            command.WeekStartDate,
            sundayPolicy);

        if (newRosterResult.IsFailure)
        {
            return Result.Failure<GeneratedRosterResultDto>(newRosterResult.Error);
        }

        var roster = newRosterResult.Value;
        var planningContext = RosterDemandAndTemplateResolver.BuildContext(
            company,
            command.WeekStartDate,
            activeEmployees,
            demandSlots,
            explicitTemplates,
            explicitStaffingRules);

        Roster5x2OptimizationEngine.Optimize(
            roster,
            activeEmployees,
            planningContext,
            boundaryResult.Value);

        var complianceReport = RosterComplianceEvaluator.Evaluate(
            roster,
            company,
            activeEmployees,
            planningContext,
            boundaryResult.Value);

        var replacedPreviousDraft = false;
        if (existingRoster is not null)
        {
            var archiveResult = existingRoster.Archive();
            if (archiveResult.IsFailure)
            {
                return Result.Failure<GeneratedRosterResultDto>(archiveResult.Error);
            }

            _rosterRepository.Update(existingRoster);
            replacedPreviousDraft = true;
        }

        await _rosterRepository.AddAsync(roster, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(RosterResultMapper.MapToResultDto(
            roster,
            company,
            activeEmployees,
            planningContext,
            complianceReport,
            replacedPreviousDraft));
    }

    private static Result<(
        SundayRotationPolicy Policy,
        IReadOnlyList<ResolvedShiftTemplate>? Templates,
        IReadOnlyDictionary<StationRole, StationStaffingRuleDto>? StaffingRules)> ValidateCommand(
        GenerateRoster5x2Command command)
    {
        if (command is null)
        {
            return Result.Failure<(SundayRotationPolicy, IReadOnlyList<ResolvedShiftTemplate>?, IReadOnlyDictionary<StationRole, StationStaffingRuleDto>?)>(
                Error.Validation("RosterGeneration.InvalidCommand", "Comando de geração de escala inválido."));
        }

        if (command.WeekStartDate == default)
        {
            return Result.Failure<(SundayRotationPolicy, IReadOnlyList<ResolvedShiftTemplate>?, IReadOnlyDictionary<StationRole, StationStaffingRuleDto>?)>(
                Error.Validation("RosterGeneration.InvalidWeekStartDate", "A data inicial da semana é obrigatória."));
        }

        var sundayPolicyResult = command.MaxConsecutiveSundaysWorked.HasValue
            ? SundayRotationPolicy.Create(command.MaxConsecutiveSundaysWorked.Value)
            : Result.Success(SundayRotationPolicy.ThreeWeekCycle);

        if (sundayPolicyResult.IsFailure)
        {
            return Result.Failure<(SundayRotationPolicy, IReadOnlyList<ResolvedShiftTemplate>?, IReadOnlyDictionary<StationRole, StationStaffingRuleDto>?)>(
                sundayPolicyResult.Error);
        }

        IReadOnlyList<ResolvedShiftTemplate>? resolvedTemplates = null;
        if (command.ShiftTemplates is not null)
        {
            var templatesResult = RosterDemandAndTemplateResolver.ValidateExplicitTemplates(
                command.ShiftTemplates,
                "RosterGeneration.InvalidShiftTemplate");
            if (templatesResult.IsFailure)
            {
                return Result.Failure<(SundayRotationPolicy, IReadOnlyList<ResolvedShiftTemplate>?, IReadOnlyDictionary<StationRole, StationStaffingRuleDto>?)>(
                    templatesResult.Error);
            }

            resolvedTemplates = templatesResult.Value;
        }

        IReadOnlyDictionary<StationRole, StationStaffingRuleDto>? resolvedStaffingRules = null;
        if (command.StaffingRules is not null)
        {
            var staffingResult = RosterDemandAndTemplateResolver.ValidateExplicitStaffingRules(
                command.StaffingRules,
                "RosterGeneration.InvalidStaffingRule");
            if (staffingResult.IsFailure)
            {
                return Result.Failure<(SundayRotationPolicy, IReadOnlyList<ResolvedShiftTemplate>?, IReadOnlyDictionary<StationRole, StationStaffingRuleDto>?)>(
                    staffingResult.Error);
            }

            resolvedStaffingRules = staffingResult.Value;
        }

        return Result.Success((sundayPolicyResult.Value, resolvedTemplates, resolvedStaffingRules));
    }
}
