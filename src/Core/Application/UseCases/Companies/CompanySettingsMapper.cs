using Application.DTOs.Companies;
using Domain.Entities;

namespace Application.UseCases.Companies;

internal static class CompanySettingsMapper
{
    private static readonly DayOfWeek[] OperationalWeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    internal static CompanySettingsDto ToDto(Company company)
    {
        var orderedClosedDays = OperationalWeekOrder
            .Where(day => company.ClosedDays.Contains(day))
            .ToList();

        return new CompanySettingsDto(
            company.Id,
            company.Name,
            company.BusinessType,
            company.OpeningTime,
            company.ClosingTime,
            company.ScheduleModel,
            orderedClosedDays,
            company.ClosingTime < company.OpeningTime);
    }
}

