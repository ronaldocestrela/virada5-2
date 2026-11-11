using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class RosterConfiguration : IEntityTypeConfiguration<Roster>
{
    public void Configure(EntityTypeBuilder<Roster> builder)
    {
        builder.ToTable("Rosters");

        builder.HasKey(roster => roster.Id);
        builder.Property(roster => roster.Id)
            .ValueGeneratedNever();

        builder.Property(roster => roster.TenantId)
            .IsRequired();

        builder.Property(roster => roster.CompanyId)
            .IsRequired();

        builder.Property(roster => roster.WeekStartDate)
            .IsRequired();

        builder.Property(roster => roster.WeekEndDate)
            .IsRequired();

        builder.Property(roster => roster.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(roster => roster.SundayPolicy)
            .IsRequired()
            .HasConversion(
                policy => policy.MaxConsecutiveSundaysWorked,
                value => SundayRotationPolicy.Create(value).Value);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(roster => roster.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(roster => roster.Shifts)
            .WithOne()
            .HasForeignKey(shift => shift.RosterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(roster => roster.Shifts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(roster => new { roster.TenantId, roster.CompanyId, roster.WeekStartDate })
            .HasDatabaseName("IX_Rosters_TenantId_CompanyId_WeekStartDate");
    }
}
