using Infrastructure.Data;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Aplica automaticamente as migrations pendentes do <see cref="AppDbContext"/> no arranque da aplicação.
/// </summary>
public static partial class DatabaseMigrationExtensions
{
    public const string RunMigrationsOnStartupSetting = "RUN_MIGRATIONS_ON_STARTUP";

    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    public static bool ShouldRunMigrationsOnStartup(
        IConfiguration configuration,
        string? dotEnvSearchDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var rawValue = configuration[RunMigrationsOnStartupSetting];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            rawValue = DotEnvLoader.ResolveValue(RunMigrationsOnStartupSetting, dotEnvSearchDirectory);
        }

        return !string.Equals(rawValue?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    public static Task ApplyDatabaseMigrationsOnStartupAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("WebApp.Blazor.StartupMigrations");

        return ApplyDatabaseMigrationsOnStartupAsync(
            app.Services,
            app.Configuration,
            logger,
            retryDelays: null,
            migrateExecutor: null,
            cancellationToken);
    }

    public static async Task ApplyDatabaseMigrationsOnStartupAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        TimeSpan[]? retryDelays = null,
        Func<AppDbContext, CancellationToken, Task>? migrateExecutor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        if (!ShouldRunMigrationsOnStartup(configuration))
        {
            LogMigrationsDisabled(logger);
            return;
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!dbContext.Database.IsSqlServer())
        {
            LogNonSqlServerProviderSkipped(logger);
            return;
        }

        var delays = retryDelays ?? DefaultRetryDelays;
        var maxAttempts = 1 + delays.Length;
        var executor = migrateExecutor ?? ApplySqlServerMigrationsAsync;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await executor(dbContext, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxAttempts)
            {
                var delay = delays[attempt - 1];
                LogTransientMigrationFailure(logger, ex, attempt, maxAttempts, delay.TotalSeconds);

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPermanentMigrationFailure(logger, ex, maxAttempts);
                throw;
            }
        }

        async Task ApplySqlServerMigrationsAsync(AppDbContext context, CancellationToken ct)
        {
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pendingMigrations.Count == 0)
            {
                LogNoPendingMigrations(logger);
                return;
            }

            LogApplyingPendingMigrations(logger, pendingMigrations.Count, string.Join(", ", pendingMigrations));
            await context.Database.MigrateAsync(ct);
            LogMigrationsApplied(logger);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Migrations automáticas no arranque desativadas via RUN_MIGRATIONS_ON_STARTUP=false.")]
    private static partial void LogMigrationsDisabled(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Migrations automáticas no arranque ignoradas: o provider configurado não é SQL Server.")]
    private static partial void LogNonSqlServerProviderSkipped(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Nenhuma migration pendente para o AppDbContext.")]
    private static partial void LogNoPendingMigrations(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Aplicando {PendingCount} migration(s) pendente(s) no arranque: {Migrations}.")]
    private static partial void LogApplyingPendingMigrations(ILogger logger, int pendingCount, string migrations);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Migrations do AppDbContext aplicadas com sucesso no arranque.")]
    private static partial void LogMigrationsApplied(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Falha transitória ao aplicar migrations no arranque (tentativa {Attempt}/{MaxAttempts}). Nova tentativa em {DelaySeconds:0.##}s.")]
    private static partial void LogTransientMigrationFailure(
        ILogger logger,
        Exception exception,
        int attempt,
        int maxAttempts,
        double delaySeconds);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Falha definitiva ao aplicar migrations do AppDbContext no arranque após {MaxAttempts} tentativa(s).")]
    private static partial void LogPermanentMigrationFailure(
        ILogger logger,
        Exception exception,
        int maxAttempts);
}

