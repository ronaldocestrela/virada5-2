using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Context.Configurations;

internal sealed class ShiftSwapRequestConfiguration : IEntityTypeConfiguration<ShiftSwapRequest>
{
    public void Configure(EntityTypeBuilder<ShiftSwapRequest> builder)
    {
        builder.ToTable("ShiftSwapRequests");

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id)
            .ValueGeneratedNever();

        builder.Property(request => request.TenantId)
            .IsRequired();

        builder.Property(request => request.CompanyId)
            .IsRequired();

        builder.Property(request => request.RosterId)
            .IsRequired();

        builder.Property(request => request.RequesterEmployeeId)
            .IsRequired();

        builder.Property(request => request.RequesterShiftId)
            .IsRequired();

        builder.Property(request => request.TargetEmployeeId)
            .IsRequired();

        builder.Property(request => request.TargetShiftId)
            .IsRequired(false);

        builder.Property(request => request.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(35);

        builder.Property(request => request.Reason)
            .HasMaxLength(ShiftSwapRequest.MaxNotesLength);

        builder.Property(request => request.DecisionNotes)
            .HasMaxLength(ShiftSwapRequest.MaxNotesLength);

        builder.Property(request => request.RequestedAtUtc)
            .IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(request => request.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Roster>()
            .WithMany()
            .HasForeignKey(request => request.RosterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(request => request.RequesterEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(request => request.TargetEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(request => request.RequesterShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(request => request.TargetShiftId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(request => new { request.TenantId, request.CompanyId, request.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_TenantId_CompanyId_Status");

        builder.HasIndex(request => new { request.TenantId, request.RosterId, request.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_TenantId_RosterId_Status");

        builder.HasIndex(request => new { request.TenantId, request.RequesterShiftId, request.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_TenantId_RequesterShiftId_Status");

        builder.HasIndex(request => new { request.TenantId, request.TargetShiftId, request.Status })
            .HasDatabaseName("IX_ShiftSwapRequests_TenantId_TargetShiftId_Status");
    }
}

