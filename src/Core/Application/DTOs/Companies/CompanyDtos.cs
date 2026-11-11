using Domain.Enums;

namespace Application.DTOs.Companies;

public sealed record CompanySettingsDto(
    Guid CompanyId,
    string Name,
    BusinessType BusinessType,
    TimeOnly OpeningTime,
    TimeOnly ClosingTime,
    ScheduleModel ScheduleModel,
    IReadOnlyList<DayOfWeek> ClosedDays,
    bool OperatesOvernight);

