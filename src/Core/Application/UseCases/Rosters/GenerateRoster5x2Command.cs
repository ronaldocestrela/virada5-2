using Application.DTOs.Rosters;

namespace Application.UseCases.Rosters;

public sealed record GenerateRoster5x2Command(
    DateOnly WeekStartDate,
    Guid? CompanyId = null,
    int? MaxConsecutiveSundaysWorked = null,
    bool ReplaceExistingDraft = false,
    IReadOnlyList<ShiftTemplateInputDto>? ShiftTemplates = null,
    IReadOnlyList<StationStaffingRuleDto>? StaffingRules = null);

