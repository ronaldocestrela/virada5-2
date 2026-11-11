using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class RosterConfiguration : IEntityTypeConfiguration<Roster>
{
    public void Configure(EntityTypeBuilder<Roster> builder)
    {
        builder.HasKey(roster => roster.Id);

        builder.Property(roster => roster.SundayPolicy)
            .HasConversion(
                policy => policy.MaxConsecutiveSundaysWorked,
                value => SundayRotationPolicy.Create(value).Value);

        builder.HasMany(roster => roster.Shifts)
            .WithOne()
            .HasForeignKey(shift => shift.RosterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(roster => roster.Shifts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

