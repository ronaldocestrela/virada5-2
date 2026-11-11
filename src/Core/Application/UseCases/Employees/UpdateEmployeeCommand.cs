using Application.DTOs.Employees;
using Domain.Enums;

namespace Application.UseCases.Employees;

public sealed record UpdateEmployeeCommand(
    Guid EmployeeId,
    string Name,
    StationRole PrimaryRole,
    IReadOnlyList<SecondarySkillDto>? SecondarySkills = null,
    IReadOnlyList<DayOfWeek>? PreferredDaysOff = null,
    IReadOnlyList<DayOfWeek>? UnavailableDays = null,
    Guid? CompanyId = null);

