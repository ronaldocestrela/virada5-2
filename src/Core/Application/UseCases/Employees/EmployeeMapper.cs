using Application.DTOs.Employees;
using Domain.Entities;
using Domain.ValueObjects;

namespace Application.UseCases.Employees;

internal static class EmployeeMapper
{
    internal static EmployeeDto ToDto(Employee employee)
    {
        var skills = employee.SecondarySkills
            .OrderBy(skill => skill.Role)
            .Select(skill => new SecondarySkillDto(skill.Role, skill.Proficiency))
            .ToList();

        var preferredDaysOff = employee.PreferredDaysOff
            .OrderBy(ToOperationalOrder)
            .ToList();

        var unavailableDays = employee.UnavailableDays
            .OrderBy(ToOperationalOrder)
            .ToList();

        return new EmployeeDto(
            employee.Id,
            employee.CompanyId,
            employee.Name,
            employee.PrimaryRole,
            employee.IsActive,
            skills,
            preferredDaysOff,
            unavailableDays);
    }

    internal static Result<List<SecondarySkill>> ToDomainSecondarySkills(
        IReadOnlyList<SecondarySkillDto>? skillDtos)
    {
        if (skillDtos is null || skillDtos.Count == 0)
        {
            return Result.Success(new List<SecondarySkill>());
        }

        var skills = new List<SecondarySkill>(skillDtos.Count);
        foreach (var dto in skillDtos)
        {
            var skillResult = SecondarySkill.Create(dto.Role, dto.Proficiency);
            if (skillResult.IsFailure)
            {
                return Result.Failure<List<SecondarySkill>>(skillResult.Error);
            }

            skills.Add(skillResult.Value);
        }

        return Result.Success(skills);
    }

    private static int ToOperationalOrder(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
}

