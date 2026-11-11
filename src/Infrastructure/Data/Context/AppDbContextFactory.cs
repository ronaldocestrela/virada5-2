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

    private static string? ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return TryReadFromDotEnv(Directory.GetCurrentDirectory());
    }

    private static string? TryReadFromDotEnv(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var envFilePath = Path.Combine(directory.FullName, ".env");
            if (File.Exists(envFilePath))
            {
                foreach (var rawLine in File.ReadLines(envFilePath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    if (!string.Equals(key, ConnectionStringEnvVar, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var value = line[(separatorIndex + 1)..].Trim();
                    if (value.Length >= 2 &&
                        ((value.StartsWith('"') && value.EndsWith('"')) ||
                         (value.StartsWith('\'') && value.EndsWith('\''))))
                    {
                        value = value[1..^1];
                    }

                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }
}
