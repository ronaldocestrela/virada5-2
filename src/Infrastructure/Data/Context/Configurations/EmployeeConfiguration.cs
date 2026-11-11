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
        builder.ToTable("Employees");

        builder.HasKey(employee => employee.Id);
        builder.Property(employee => employee.Id)
            .ValueGeneratedNever();

        builder.Property(employee => employee.TenantId)
            .IsRequired();

        builder.Property(employee => employee.CompanyId)
            .IsRequired();

        builder.Property(employee => employee.Name)
            .IsRequired()
            .HasMaxLength(Employee.MaxNameLength);

        builder.Property(employee => employee.PrimaryRole)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(employee => employee.IsActive)
            .IsRequired();

        builder.ConfigureJsonReadOnlyCollection(
                employee => employee.SecondarySkills,
                "_secondarySkills",
                skill => new SecondarySkillRecord(skill.Role, skill.Proficiency),
                record => SecondarySkill.Create(record.Role, record.Proficiency).Value)
            .IsRequired()
            .HasMaxLength(2000);

        builder.ConfigureJsonReadOnlyCollection(
                employee => employee.PreferredDaysOff,
                "_preferredDaysOff")
            .IsRequired()
            .HasMaxLength(200);

        builder.ConfigureJsonReadOnlyCollection(
                employee => employee.UnavailableDays,
                "_unavailableDays")
            .IsRequired()
            .HasMaxLength(200);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(employee => employee.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(employee => new { employee.TenantId, employee.CompanyId, employee.IsActive })
            .HasDatabaseName("IX_Employees_TenantId_CompanyId_IsActive");
    }

    private sealed record SecondarySkillRecord(StationRole Role, SkillProficiency Proficiency);
}
