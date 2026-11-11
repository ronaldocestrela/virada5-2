using Infrastructure.Data.Context;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Configura endpoints de liveness e readiness para Azure App Service, containers Docker e orquestradores.
/// </summary>
public static class HealthCheckExtensions
{
    public static IServiceCollection AddViradaHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Aplicação operacional."), tags: ["live"])
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

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

