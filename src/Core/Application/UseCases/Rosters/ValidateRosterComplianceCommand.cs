using Application.DTOs.Rosters;

namespace Application.UseCases.Rosters;

public sealed record ValidateRosterComplianceCommand(
    Guid RosterId,
    IReadOnlyList<ShiftTemplateInputDto>? ShiftTemplates = null,
    IReadOnlyList<StationStaffingRuleDto>? StaffingRules = null);

