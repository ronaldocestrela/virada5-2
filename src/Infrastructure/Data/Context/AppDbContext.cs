using System.Reflection;
using Application.Common.Interfaces;
using Domain.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Context;

/// <summary>
/// Contexto principal do Entity Framework Core com isolamento lógico multi-tenant
/// via Global Query Filters e interceptação fail-closed de TenantId.
/// </summary>
public class AppDbContext : DbContext, IUnitOfWork
{
    private static readonly MethodInfo ApplyTenantQueryFilterMethod = typeof(AppDbContext)
        .GetMethod(nameof(ApplyTenantQueryFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ICurrentUserService _currentUserService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUserService currentUserService)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(currentUserService);
        _currentUserService = currentUserService;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<DemandSlot> DemandSlots => Set<DemandSlot>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Roster> Rosters => Set<Roster>();
    public DbSet<Shift> Shifts => Set<Shift>();

    private Guid CurrentTenantId => _currentUserService.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is null &&
                !entityType.IsOwned() &&
                typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantQueryFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantEnforcement();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantEnforcement();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(entity => CurrentTenantId != Guid.Empty && entity.TenantId == CurrentTenantId);
    }

    private void ApplyTenantEnforcement()
    {
        var tenantEntries = ChangeTracker
            .Entries<ITenantEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (tenantEntries.Count == 0)
        {
            return;
        }

        var currentTenantId = _currentUserService.TenantId;
        if (currentTenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Não é possível persistir entidades vinculadas a um tenant sem um TenantId válido no contexto atual.");
        }

        foreach (var entry in tenantEntries)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = currentTenantId;
                }
                else if (entry.Entity.TenantId != currentTenantId)
                {
                    throw new InvalidOperationException(
                        $"Tentativa de persistir entidade {entry.Entity.GetType().Name} com TenantId divergente do contexto atual.");
                }

                continue;
            }

            if (entry.Entity.TenantId != currentTenantId)
            {
                throw new InvalidOperationException(
                    $"Tentativa de alterar ou remover entidade {entry.Entity.GetType().Name} pertencente a outro tenant.");
            }

            entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
        }
    }
}

