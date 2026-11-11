using Domain.Enums;

namespace Application.DTOs.Employees;

public sealed record SecondarySkillDto(
    StationRole Role,
    SkillProficiency Proficiency);

public sealed record EmployeeDto(
    Guid EmployeeId,
    Guid CompanyId,
    string Name,
    StationRole PrimaryRole,
    bool IsActive,
    IReadOnlyList<SecondarySkillDto> SecondarySkills,
    IReadOnlyList<DayOfWeek> PreferredDaysOff,
    IReadOnlyList<DayOfWeek> UnavailableDays);

public sealed record EmployeesWorkspaceDto(
    Guid CompanyId,
    string CompanyName,
    ScheduleModel ScheduleModel,
    IReadOnlyList<DayOfWeek> ClosedDays,
    IReadOnlyList<EmployeeDto> Employees);

