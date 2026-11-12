using Infrastructure.Data.Context;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Configura endpoints de liveness e readiness para Azure App Service, containers Docker e orquestradores.
/// </summary>
public static class HealthCheckExtensions
{
    public static IServiceCollection AddViradaHealthChecks(
        this IServiceCollection services,
        string? dataProtectionKeysPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var healthChecks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Aplicação operacional."), tags: ["live"])
            .AddDbContextCheck<AppDbContext>(
                "database",
                tags: ["ready"],
                customTestQuery: VerifyDatabaseReadinessAsync);

        if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            healthChecks.AddCheck(
                "dataprotection_keys",
                () => VerifyDataProtectionDirectory(dataProtectionKeysPath),
                tags: ["ready"]);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapViradaHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteStructuredHealthResponseAsync
        }).AllowAnonymous();

        return endpoints;
    }

    private static async Task<bool> VerifyDatabaseReadinessAsync(AppDbContext dbContext, CancellationToken ct)
    {
        if (!await dbContext.Database.CanConnectAsync(ct))
        {
            return false;
        }

        if (!dbContext.Database.IsRelational())
        {
            return true;
        }

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(ct);
        if (appliedMigrations.Any() || dbContext.Database.IsSqlServer())
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(ct);
            return !pendingMigrations.Any();
        }

        return true;
    }

    private static HealthCheckResult VerifyDataProtectionDirectory(string keysPath)
    {
        try
        {
            Directory.CreateDirectory(keysPath);
            var probeFile = Path.Combine(keysPath, $".probe-{Guid.NewGuid():N}");
            using (File.Create(probeFile, 1, FileOptions.DeleteOnClose))
            {
            }

            return HealthCheckResult.Healthy("Diretório de chaves DataProtection gravável.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Diretório de chaves DataProtection inacessível para gravação.",
                ex);
        }
    }

    private static Task WriteStructuredHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    durationMs = Math.Round(kvp.Value.Duration.TotalMilliseconds, 2)
                })
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
