using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Common;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso somente-leitura para carregar o workspace do painel interativo da escala 5x2.
/// </summary>
public sealed class GetCurrentRosterWorkspaceUseCase
{
    private const int NearbyLookbackWeeks = 4;
    private const int NearbyLookaheadWeeks = 8;

    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IResultTelemetry? _telemetry;

    public GetCurrentRosterWorkspaceUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _currentUserService = currentUserService;
        _telemetry = telemetry;
    }

    public Task<Result<RosterWorkspaceDto>> ExecuteAsync(
        GetCurrentRosterWorkspaceCommand? command = null,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "GetCurrentRosterWorkspace",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<RosterWorkspaceDto>> ExecuteCoreAsync(
        GetCurrentRosterWorkspaceCommand? command,
        CancellationToken ct)
    {
        var companyResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            command?.CompanyId,
            _companyRepository,
            _currentUserService,
            "RosterWorkspace.EmptyTenant",
            ct);

        if (companyResult.IsFailure)
        {
            return Result.Failure<RosterWorkspaceDto>(companyResult.Error);
        }

        var (_, company) = companyResult.Value;

        var rawTargetDate = command?.WeekStartDate is { } explicitDate && explicitDate != default
            ? explicitDate
            : ResolveDefaultNextMonday();
        var selectedWeekStart = RosterResultMapper.NormalizeToMonday(rawTargetDate);
        var selectedWeekEnd = selectedWeekStart.AddDays(Roster.CycleLengthDays - 1);

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var demandSlots = await _demandSlotRepository.GetByCompanyAsync(company.Id, ct);

        var rangeStart = selectedWeekStart.AddDays(-NearbyLookbackWeeks * Roster.CycleLengthDays);
        var rangeEnd = selectedWeekStart.AddDays(NearbyLookaheadWeeks * Roster.CycleLengthDays + Roster.CycleLengthDays - 1);
        var nearbyRosters = await _rosterRepository.GetByWeekRangeAsync(company.Id, rangeStart, rangeEnd, ct);

        var targetRoster = nearbyRosters.FirstOrDefault(r => r.WeekStartDate == selectedWeekStart)
            ?? await _rosterRepository.GetByCompanyAndWeekStartAsync(company.Id, selectedWeekStart, ct);

        GeneratedRosterResultDto? activeRosterDto = null;
        if (targetRoster is not null)
        {
            var relevantEmployees = await RosterResultMapper.MergeRelevantEmployeesAsync(
                targetRoster,
                activeEmployees,
                _employeeRepository,
                ct);

            var boundaryResult = RosterBoundaryContextBuilder.Build(
                targetRoster.WeekStartDate,
                nearbyRosters,
                relevantEmployees.Select(e => e.Id),
                targetRoster.Id);

            if (boundaryResult.IsFailure)
            {
                return Result.Failure<RosterWorkspaceDto>(boundaryResult.Error);
            }

            activeRosterDto = RosterResultMapper.EvaluateAndMapResult(
                targetRoster,
                company,
                relevantEmployees,
                demandSlots,
                boundaryResult.Value);
        }

        var basePlanningContext = RosterDemandAndTemplateResolver.BuildContext(
            company,
            selectedWeekStart,
            activeEmployees,
            demandSlots,
            explicitTemplates: null,
            explicitStaffingRules: null);

        var availableTemplates = basePlanningContext.Templates
            .Select(t => new ShiftTemplateInputDto(t.Name, t.StartTime, t.EndTime))
            .ToList()
            .AsReadOnly();

        var employeeOptions = activeEmployees
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new RosterEmployeeOptionDto(
                e.Id,
                e.Name,
                e.PrimaryRole,
                e.SecondarySkills
                    .Select(s => new RosterEmployeeSecondarySkillDto(s.Role, s.Proficiency))
                    .ToList()
                    .AsReadOnly(),
                e.UnavailableDays.ToList().AsReadOnly(),
                e.PreferredDaysOff.ToList().AsReadOnly()))
            .ToList()
            .AsReadOnly();

        var nearbySummaries = new List<RosterWeekSummaryDto>(nearbyRosters.Count);
        foreach (var nearby in nearbyRosters.OrderBy(r => r.WeekStartDate))
        {
            if (activeRosterDto is not null && nearby.Id == activeRosterDto.RosterId)
            {
                nearbySummaries.Add(new RosterWeekSummaryDto(
                    activeRosterDto.RosterId,
                    activeRosterDto.WeekStartDate,
                    activeRosterDto.WeekEndDate,
                    activeRosterDto.Status,
                    activeRosterDto.ComplianceReport.IsCltCompliant,
                    activeRosterDto.ComplianceReport.PeakCoveragePercentage));
                continue;
            }

            var relevant = await RosterResultMapper.MergeRelevantEmployeesAsync(
                nearby,
                activeEmployees,
                _employeeRepository,
                ct);
            var boundary = RosterBoundaryContextBuilder.Build(
                nearby.WeekStartDate,
                nearbyRosters,
                relevant.Select(e => e.Id),
                nearby.Id);

            if (boundary.IsFailure)
            {
                return Result.Failure<RosterWorkspaceDto>(boundary.Error);
            }

            var evaluated = RosterResultMapper.EvaluateAndMapResult(
                nearby,
                company,
                relevant,
                demandSlots,
                boundary.Value);

            nearbySummaries.Add(new RosterWeekSummaryDto(
                evaluated.RosterId,
                evaluated.WeekStartDate,
                evaluated.WeekEndDate,
                evaluated.Status,
                evaluated.ComplianceReport.IsCltCompliant,
                evaluated.ComplianceReport.PeakCoveragePercentage));
        }

        var closedDays = company.ClosedDays
            .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
            .ToList()
            .AsReadOnly();

        return Result.Success(new RosterWorkspaceDto(
            company.Id,
            company.Name,
            selectedWeekStart,
            selectedWeekEnd,
            closedDays,
            activeRosterDto,
            employeeOptions,
            availableTemplates,
            nearbySummaries.AsReadOnly()));
    }

    private static DateOnly ResolveDefaultNextMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return daysUntilMonday == 0 ? today.AddDays(7) : today.AddDays(daysUntilMonday);
    }
}
