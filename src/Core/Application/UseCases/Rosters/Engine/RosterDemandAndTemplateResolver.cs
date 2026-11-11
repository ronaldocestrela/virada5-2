using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UseCases.Rosters.Engine;

internal sealed record ResolvedShiftTemplate(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    TimeSpan Duration);

internal sealed record DemandSlotEvaluation(
    decimal ExpectedVolume,
    DemandClassification Classification);

internal sealed record RosterPlanningContext(
    bool HasDemandMatrix,
    bool UsedInferredShiftTemplates,
    bool UsedInferredStaffingRules,
    IReadOnlyList<ResolvedShiftTemplate> Templates,
    IReadOnlyDictionary<StationRole, StationStaffingRuleDto> StaffingRulesByRole,
    IReadOnlyDictionary<(DayOfWeek Day, int Hour), DemandSlotEvaluation> DemandMatrix,
    IReadOnlyDictionary<DayOfWeek, decimal> DailyDemandTotals,
    IReadOnlySet<DayOfWeek> ValleyDays,
    IReadOnlyList<RosterRequirementSlot> Requirements);

internal sealed record RosterRequirementSlot(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    ResolvedShiftTemplate Template,
    StationRole Role,
    DemandClassification DemandLevel,
    decimal ExpectedVolumeScore,
    int RequiredCount);

internal static class RosterDemandAndTemplateResolver
{
    private const int SingleShiftThresholdHours = 10;
    private const int MinInferredSplitShiftHours = 8;

    internal static Result<IReadOnlyList<ResolvedShiftTemplate>> ValidateExplicitTemplates(
        IReadOnlyList<ShiftTemplateInputDto>? explicitTemplates,
        string errorCode)
    {
        if (explicitTemplates is null)
        {
            return Result.Success<IReadOnlyList<ResolvedShiftTemplate>>([]);
        }

        if (explicitTemplates.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ResolvedShiftTemplate>>(
                Error.Validation(errorCode, "Informe ao menos um template de turno quando especificado."));
        }

        var resolved = new List<ResolvedShiftTemplate>(explicitTemplates.Count);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in explicitTemplates)
        {
            if (template is null || string.IsNullOrWhiteSpace(template.Name))
            {
                return Result.Failure<IReadOnlyList<ResolvedShiftTemplate>>(
                    Error.Validation(errorCode, "O nome do template de turno é obrigatório."));
            }

            var trimmedName = template.Name.Trim();
            if (!seenNames.Add(trimmedName))
            {
                return Result.Failure<IReadOnlyList<ResolvedShiftTemplate>>(
                    Error.Validation(errorCode, $"Nome de template de turno duplicado: '{trimmedName}'."));
            }

            if (template.StartTime == template.EndTime)
            {
                return Result.Failure<IReadOnlyList<ResolvedShiftTemplate>>(
                    Error.Validation(errorCode, $"O turno '{trimmedName}' não pode ter início igual ao término."));
            }

            var duration = CalculateShiftDuration(template.StartTime, template.EndTime);
            if (duration > Shift.MaxShiftDuration)
            {
                return Result.Failure<IReadOnlyList<ResolvedShiftTemplate>>(
                    Error.Validation(
                        errorCode,
                        $"O turno '{trimmedName}' excede a duração máxima permitida de {Shift.MaxShiftDurationHours} horas."));
            }

            resolved.Add(new ResolvedShiftTemplate(trimmedName, template.StartTime, template.EndTime, duration));
        }

        return Result.Success<IReadOnlyList<ResolvedShiftTemplate>>(resolved);
    }

    internal static Result<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>> ValidateExplicitStaffingRules(
        IReadOnlyList<StationStaffingRuleDto>? explicitRules,
        string errorCode)
    {
        var map = new Dictionary<StationRole, StationStaffingRuleDto>();
        if (explicitRules is null)
        {
            return Result.Success<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(map);
        }

        if (explicitRules.Count == 0)
        {
            return Result.Failure<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(
                Error.Validation(errorCode, "Informe ao menos uma regra de cobertura por praça quando especificado."));
        }

        foreach (var rule in explicitRules)
        {
            if (rule is null || !Enum.IsDefined(rule.Role))
            {
                return Result.Failure<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(
                    Error.Validation(errorCode, "Praça de atuação inválida na regra de cobertura."));
            }

            if (rule.PeakHeadcount < 0 || rule.ModerateHeadcount < 0 || rule.LowOrIdleHeadcount < 0)
            {
                return Result.Failure<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(
                    Error.Validation(errorCode, "As quantidades de cobertura por praça não podem ser negativas."));
            }

            if (!map.TryAdd(rule.Role, rule))
            {
                return Result.Failure<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(
                    Error.Validation(errorCode, $"Regra de cobertura duplicada para a praça '{rule.Role}'."));
            }
        }

        return Result.Success<IReadOnlyDictionary<StationRole, StationStaffingRuleDto>>(map);
    }

    internal static RosterPlanningContext BuildContext(
        Company company,
        DateOnly weekStartDate,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<DemandSlot> demandSlots,
        IReadOnlyList<ResolvedShiftTemplate>? explicitTemplates,
        IReadOnlyDictionary<StationRole, StationStaffingRuleDto>? explicitStaffingRules)
    {
        var (hasDemandMatrix, demandMatrix) = BuildDemandMatrix(company, demandSlots);
        var dailyDemandTotals = ComputeDailyDemandTotals(company, demandMatrix, hasDemandMatrix);
        var valleyDays = ResolveValleyDays(company, dailyDemandTotals);

        var usedInferredTemplates = explicitTemplates is null;
        var templates = explicitTemplates ?? InferTemplatesFromCompany(company);

        var usedInferredRules = explicitStaffingRules is null;
        var staffingRules = explicitStaffingRules ?? InferStaffingRulesFromEmployees(employees, templates.Count);

        var requirements = BuildRequirementSlots(
            company,
            weekStartDate,
            templates,
            staffingRules,
            demandMatrix,
            hasDemandMatrix);

        return new RosterPlanningContext(
            hasDemandMatrix,
            usedInferredTemplates,
            usedInferredRules,
            templates,
            staffingRules,
            demandMatrix,
            dailyDemandTotals,
            valleyDays,
            requirements);
    }

    internal static DemandClassification EvaluateShiftDemandClassification(
        Company company,
        Shift shift,
        IReadOnlyList<DemandSlot> demandSlots)
    {
        var (hasDemandMatrix, demandMatrix) = BuildDemandMatrix(company, demandSlots);
        var syntheticTemplate = new ResolvedShiftTemplate(
            "Shift",
            shift.StartTime,
            shift.EndTime,
            shift.Duration);

        var (demandLevel, _) = EvaluateTemplateDemand(
            company,
            shift.Date.DayOfWeek,
            syntheticTemplate,
            demandMatrix,
            hasDemandMatrix);

        return demandLevel;
    }

    internal static TimeSpan CalculateShiftDuration(TimeOnly startTime, TimeOnly endTime) =>
        endTime > startTime
            ? endTime - startTime
            : TimeSpan.FromHours(24) - startTime.ToTimeSpan() + endTime.ToTimeSpan();

    private static (bool HasDemandMatrix, IReadOnlyDictionary<(DayOfWeek, int), DemandSlotEvaluation> Matrix) BuildDemandMatrix(
        Company company,
        IReadOnlyList<DemandSlot> demandSlots)
    {
        var openSlots = demandSlots
            .Where(slot => company.IsOpenAt(slot.DayOfWeek, slot.Hour))
            .ToList();

        if (openSlots.Count == 0)
        {
            return (false, new Dictionary<(DayOfWeek, int), DemandSlotEvaluation>());
        }

        var peakVolume = openSlots.Max(s => s.ExpectedVolume);
        var matrix = new Dictionary<(DayOfWeek, int), DemandSlotEvaluation>(openSlots.Count);

        foreach (var slot in openSlots)
        {
            var evalCopy = DemandSlot
                .Create(slot.TenantId, slot.CompanyId, slot.DayOfWeek, slot.Hour, slot.ExpectedVolume)
                .Value;
            evalCopy.EvaluateRelativeDemand(peakVolume);

            matrix[(slot.DayOfWeek, slot.Hour)] = new DemandSlotEvaluation(
                slot.ExpectedVolume,
                evalCopy.Classification);
        }

        return (true, matrix);
    }

    private static IReadOnlyDictionary<DayOfWeek, decimal> ComputeDailyDemandTotals(
        Company company,
        IReadOnlyDictionary<(DayOfWeek Day, int Hour), DemandSlotEvaluation> demandMatrix,
        bool hasDemandMatrix)
    {
        var totals = new Dictionary<DayOfWeek, decimal>(7);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (!company.IsOpenOn(day))
            {
                totals[day] = 0m;
                continue;
            }

            if (!hasDemandMatrix)
            {
                totals[day] = 1m;
                continue;
            }

            decimal total = 0m;
            for (var hour = DemandSlot.MinHour; hour <= DemandSlot.MaxHour; hour++)
            {
                if (company.IsOpenAt(day, hour) && demandMatrix.TryGetValue((day, hour), out var eval))
                {
                    total += eval.ExpectedVolume;
                }
            }

            totals[day] = total;
        }

        return totals;
    }

    private static IReadOnlySet<DayOfWeek> ResolveValleyDays(
        Company company,
        IReadOnlyDictionary<DayOfWeek, decimal> dailyDemandTotals)
    {
        var closedDaysCount = Enum.GetValues<DayOfWeek>().Count(d => !company.IsOpenOn(d));
        var targetValleyCount = Math.Max(0, 2 - closedDaysCount);
        if (targetValleyCount == 0)
        {
            return new HashSet<DayOfWeek>();
        }

        return Enum.GetValues<DayOfWeek>()
            .Where(company.IsOpenOn)
            .OrderBy(day => dailyDemandTotals[day])
            .ThenBy(day => day == DayOfWeek.Monday ? 0 : day == DayOfWeek.Tuesday ? 1 : 2)
            .ThenBy(day => day)
            .Take(targetValleyCount)
            .ToHashSet();
    }

    private static IReadOnlyList<ResolvedShiftTemplate> InferTemplatesFromCompany(Company company)
    {
        var operatingSpan = CalculateShiftDuration(company.OpeningTime, company.ClosingTime);

        if (operatingSpan <= TimeSpan.FromHours(SingleShiftThresholdHours))
        {
            return
            [
                new ResolvedShiftTemplate("Turno Único", company.OpeningTime, company.ClosingTime, operatingSpan)
            ];
        }

        var splitHours = Math.Min(
            Shift.MaxShiftDurationHours,
            Math.Max(MinInferredSplitShiftHours, (int)Math.Ceiling(operatingSpan.TotalHours / 2.0)));

        var splitDuration = TimeSpan.FromHours(splitHours);
        var openingEnd = company.OpeningTime.Add(splitDuration);
        var closingStart = company.ClosingTime.Add(-splitDuration);

        return
        [
            new ResolvedShiftTemplate("Abertura", company.OpeningTime, openingEnd, splitDuration),
            new ResolvedShiftTemplate("Fechamento", closingStart, company.ClosingTime, splitDuration)
        ];
    }

    private static IReadOnlyDictionary<StationRole, StationStaffingRuleDto> InferStaffingRulesFromEmployees(
        IReadOnlyList<Employee> employees,
        int templateCount)
    {
        var rules = new Dictionary<StationRole, StationStaffingRuleDto>();
        var safeTemplateCount = Math.Max(1, templateCount);

        foreach (var group in employees.GroupBy(e => e.PrimaryRole))
        {
            var totalSpecialists = group.Count();
            var peakPerTemplate = Math.Max(1, (int)Math.Ceiling(totalSpecialists / (double)safeTemplateCount));
            var moderatePerTemplate = peakPerTemplate == 1
                ? 1
                : Math.Max(1, (int)Math.Ceiling(peakPerTemplate * 0.7m));
            var lowPerTemplate = totalSpecialists >= 2
                ? Math.Max(1, peakPerTemplate / 2)
                : 0;

            rules[group.Key] = new StationStaffingRuleDto(
                group.Key,
                peakPerTemplate,
                moderatePerTemplate,
                lowPerTemplate);
        }

        return rules;
    }

    private static IReadOnlyList<RosterRequirementSlot> BuildRequirementSlots(
        Company company,
        DateOnly weekStartDate,
        IReadOnlyList<ResolvedShiftTemplate> templates,
        IReadOnlyDictionary<StationRole, StationStaffingRuleDto> staffingRules,
        IReadOnlyDictionary<(DayOfWeek Day, int Hour), DemandSlotEvaluation> demandMatrix,
        bool hasDemandMatrix)
    {
        var requirements = new List<RosterRequirementSlot>();

        for (var offset = 0; offset < Roster.CycleLengthDays; offset++)
        {
            var date = weekStartDate.AddDays(offset);
            var day = date.DayOfWeek;
            if (!company.IsOpenOn(day))
            {
                continue;
            }

            foreach (var template in templates)
            {
                var (demandLevel, volumeScore) = EvaluateTemplateDemand(
                    company,
                    day,
                    template,
                    demandMatrix,
                    hasDemandMatrix);

                foreach (var rule in staffingRules.Values.OrderBy(r => r.Role))
                {
                    var requiredCount = demandLevel switch
                    {
                        DemandClassification.Peak => rule.PeakHeadcount,
                        DemandClassification.Moderate => rule.ModerateHeadcount,
                        _ => rule.LowOrIdleHeadcount
                    };

                    if (requiredCount <= 0)
                    {
                        continue;
                    }

                    requirements.Add(new RosterRequirementSlot(
                        date,
                        day,
                        template,
                        rule.Role,
                        demandLevel,
                        volumeScore,
                        requiredCount));
                }
            }
        }

        return requirements;
    }

    private static (DemandClassification DemandLevel, decimal VolumeScore) EvaluateTemplateDemand(
        Company company,
        DayOfWeek startDay,
        ResolvedShiftTemplate template,
        IReadOnlyDictionary<(DayOfWeek Day, int Hour), DemandSlotEvaluation> demandMatrix,
        bool hasDemandMatrix)
    {
        if (!hasDemandMatrix)
        {
            return (DemandClassification.Moderate, 1m);
        }

        var highestClassification = DemandClassification.Idle;
        decimal totalVolume = 0m;
        var matchedHours = 0;

        foreach (var (slotDay, hour) in EnumerateTemplateHours(startDay, template.StartTime, template.EndTime))
        {
            if (!company.IsOpenAt(slotDay, hour))
            {
                continue;
            }

            if (demandMatrix.TryGetValue((slotDay, hour), out var eval))
            {
                if (eval.Classification > highestClassification)
                {
                    highestClassification = eval.Classification;
                }

                totalVolume += eval.ExpectedVolume;
                matchedHours++;
            }
        }

        if (matchedHours == 0)
        {
            return (DemandClassification.Moderate, 0m);
        }

        return (highestClassification, totalVolume);
    }

    private static IEnumerable<(DayOfWeek Day, int Hour)> EnumerateTemplateHours(
        DayOfWeek startDay,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        var startHour = startTime.Hour;
        var crossesMidnight = endTime <= startTime;
        var endHourInclusive = endTime.Minute > 0 ? endTime.Hour : endTime.Hour - 1;

        if (!crossesMidnight)
        {
            for (var h = startHour; h <= Math.Max(startHour, endHourInclusive); h++)
            {
                yield return (startDay, h);
            }

            yield break;
        }

        for (var h = startHour; h <= DemandSlot.MaxHour; h++)
        {
            yield return (startDay, h);
        }

        if (endTime > TimeOnly.MinValue)
        {
            var nextDay = (DayOfWeek)(((int)startDay + 1) % 7);
            for (var h = 0; h <= Math.Max(0, endHourInclusive); h++)
            {
                yield return (nextDay, h);
            }
        }
    }
}
