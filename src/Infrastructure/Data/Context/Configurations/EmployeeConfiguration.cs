using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(employee => employee.Id);

        builder.ConfigureJsonReadOnlyCollection(
            employee => employee.SecondarySkills,
            "_secondarySkills",
            skill => new SecondarySkillRecord(skill.Role, skill.Proficiency),
            record => SecondarySkill.Create(record.Role, record.Proficiency).Value);

        builder.ConfigureJsonReadOnlyCollection(
            employee => employee.PreferredDaysOff,
            "_preferredDaysOff");

        builder.ConfigureJsonReadOnlyCollection(
            employee => employee.UnavailableDays,
            "_unavailableDays");
    }

    private sealed record SecondarySkillRecord(StationRole Role, SkillProficiency Proficiency);
}

