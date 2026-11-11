using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Data.Context;

/// <summary>
/// Fábrica de design-time para criação do <see cref="AppDbContext"/> em comandos do EF Core CLI,
/// carregando opcionalmente <c>DB_CONNECTION_STRING</c> do ambiente ou de <c>.env</c>.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionStringEnvVar = "DB_CONNECTION_STRING";

    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = ResolveConnectionString();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseSqlServer(connectionString);
        }
        else
        {
            optionsBuilder.UseSqlServer();
        }

        return new AppDbContext(optionsBuilder.Options, new DesignTimeCurrentUserService());
    }

    private static string? ResolveConnectionString() =>
        DotEnvLoader.ResolveValue(ConnectionStringEnvVar);

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }
}
