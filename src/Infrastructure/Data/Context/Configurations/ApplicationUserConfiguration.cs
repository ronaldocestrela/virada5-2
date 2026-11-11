using Infrastructure.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

/// <summary>
/// Configuração Fluent API da entidade <see cref="ApplicationUser"/> no SQL Server.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.TenantId)
            .IsRequired();

        builder.Property(user => user.FullName)
            .IsRequired()
            .HasMaxLength(ApplicationUser.MaxFullNameLength);

        builder.Property(user => user.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(user => user.TenantId)
            .HasDatabaseName("IX_AspNetUsers_TenantId");
    }
}

