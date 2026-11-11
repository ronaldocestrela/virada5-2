using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");

        builder.HasKey(company => company.Id);
        builder.Property(company => company.Id)
            .ValueGeneratedNever();

        builder.Property(company => company.TenantId)
            .IsRequired();

        builder.Property(company => company.Name)
            .IsRequired()
            .HasMaxLength(Company.MaxNameLength);

        builder.Property(company => company.BusinessType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(company => company.OpeningTime)
            .IsRequired();

        builder.Property(company => company.ClosingTime)
            .IsRequired();

        builder.Property(company => company.ScheduleModel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.ConfigureJsonReadOnlyCollection(
                company => company.ClosedDays,
                "_closedDays")
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(company => company.TenantId)
            .IsUnique()
            .HasDatabaseName("IX_Companies_TenantId");
    }
}
