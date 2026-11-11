using Domain.Enums;

namespace Application.UseCases.Companies;

public sealed record SaveCompanySettingsCommand(
    string Name,
    BusinessType BusinessType,
    TimeOnly OpeningTime,
    TimeOnly ClosingTime,
    ScheduleModel ScheduleModel,
    IReadOnlyCollection<DayOfWeek>? ClosedDays = null);

