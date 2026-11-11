using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class DemandSlotConfiguration : IEntityTypeConfiguration<DemandSlot>
{
    public void Configure(EntityTypeBuilder<DemandSlot> builder)
    {
        builder.HasKey(slot => slot.Id);
    }
}

