using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");

        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.Id)
            .ValueGeneratedNever();

        builder.Property(shift => shift.TenantId)
            .IsRequired();

        builder.Property(shift => shift.RosterId)
            .IsRequired();

        builder.Property(shift => shift.EmployeeId)
            .IsRequired();

        builder.Property(shift => shift.Date)
            .IsRequired();

        builder.Property(shift => shift.StartTime)
            .IsRequired();

        builder.Property(shift => shift.EndTime)
            .IsRequired();

        builder.Property(shift => shift.AssignedRole)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(shift => shift.CoverageOrigin)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(shift => shift.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(shift => new { shift.TenantId, shift.RosterId, shift.EmployeeId })
            .HasDatabaseName("IX_Shifts_TenantId_RosterId_EmployeeId");

        builder.HasIndex(shift => new { shift.TenantId, shift.EmployeeId, shift.Date })
            .HasDatabaseName("IX_Shifts_TenantId_EmployeeId_Date");
    }
}
