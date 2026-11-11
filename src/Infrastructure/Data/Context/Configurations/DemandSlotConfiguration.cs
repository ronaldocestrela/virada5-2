using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class DemandSlotConfiguration : IEntityTypeConfiguration<DemandSlot>
{
    public void Configure(EntityTypeBuilder<DemandSlot> builder)
    {
        builder.ToTable("DemandSlots");

        builder.HasKey(slot => slot.Id);
        builder.Property(slot => slot.Id)
            .ValueGeneratedNever();

        builder.Property(slot => slot.TenantId)
            .IsRequired();

        builder.Property(slot => slot.CompanyId)
            .IsRequired();

        builder.Property(slot => slot.DayOfWeek)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(slot => slot.Hour)
            .IsRequired();

        builder.Property(slot => slot.ExpectedVolume)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(slot => slot.RelativeVolume)
            .IsRequired()
            .HasPrecision(5, 4);

        builder.Property(slot => slot.Classification)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(slot => slot.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(slot => new { slot.TenantId, slot.CompanyId, slot.DayOfWeek, slot.Hour })
            .IsUnique()
            .HasDatabaseName("IX_DemandSlots_TenantId_CompanyId_DayOfWeek_Hour");
    }
}
