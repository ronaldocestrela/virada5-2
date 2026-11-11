using Application.DTOs.Employees;
using Domain.Enums;

namespace Application.UseCases.Employees;

public sealed record CreateEmployeeCommand(
    string Name,
    StationRole PrimaryRole,
    IReadOnlyList<SecondarySkillDto>? SecondarySkills = null,
    IReadOnlyList<DayOfWeek>? PreferredDaysOff = null,
    IReadOnlyList<DayOfWeek>? UnavailableDays = null,
    Guid? CompanyId = null);

