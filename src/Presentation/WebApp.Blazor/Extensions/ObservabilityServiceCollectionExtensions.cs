using System.Diagnostics;
using System.Security.Claims;
using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Infrastructure.Data;
using Infrastructure.Data.Identity;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Configura logging estruturado com Serilog, instrumentação OpenTelemetry e telemetria de <c>Result&lt;T&gt;</c>.
/// Todas as configurações sensíveis ou de endpoint são lidas exclusivamente via <c>.env</c>/variáveis de ambiente.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    public static WebApplicationBuilder AddViradaObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var serviceName = DotEnvLoader.ResolveValue("OTEL_SERVICE_NAME") ?? "Virada5x2.WebApp";
        var otlpEndpoint = DotEnvLoader.ResolveValue("OTEL_EXPORTER_OTLP_ENDPOINT");
        var otlpHeadersRaw = DotEnvLoader.ResolveValue("OTEL_EXPORTER_OTLP_HEADERS");
        var minLevelText = DotEnvLoader.ResolveValue("SERILOG_MINIMUM_LEVEL");
        var logFilePath = DotEnvLoader.ResolveValue("SERILOG_LOG_FILE_PATH");

        var parsedHeaders = ParseOtlpHeaders(otlpHeadersRaw);
        var formattedHeaders = FormatOtlpHeaders(parsedHeaders);
        var hasOtlpUri = Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri);

        var minLevel = Enum.TryParse<LogEventLevel>(minLevelText, ignoreCase: true, out var parsedLevel)
            ? parsedLevel
            : LogEventLevel.Information;

        builder.Services.AddSerilog((_, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Is(minLevel)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.With<ActivityTraceEnricher>()
                .Enrich.WithProperty("ServiceName", serviceName)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

            if (builder.Environment.IsDevelopment())
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
            }
            else
            {
                loggerConfig.WriteTo.Console(new CompactJsonFormatter());
            }

            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                loggerConfig.WriteTo.File(
                    new CompactJsonFormatter(),
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14);
            }

            if (hasOtlpUri && otlpUri is not null)
            {
                loggerConfig.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpUri.ToString();
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["deployment.environment"] = builder.Environment.EnvironmentName
                    };

                    if (parsedHeaders.Count > 0)
                    {
                        options.Headers = new Dictionary<string, string>(parsedHeaders, StringComparer.OrdinalIgnoreCase);
                    }
                });
            }
        });

        var otelBuilder = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ApplicationTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                if (hasOtlpUri && otlpUri is not null)
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = otlpUri;
                        if (!string.IsNullOrWhiteSpace(formattedHeaders))
                        {
                            exporter.Headers = formattedHeaders;
                        }
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ApplicationTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasOtlpUri && otlpUri is not null)
                {
                    metrics.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = otlpUri;
                        if (!string.IsNullOrWhiteSpace(formattedHeaders))
                        {
                            exporter.Headers = formattedHeaders;
                        }
                    });
                }
            });

        _ = otelBuilder;

        builder.Services.AddScoped<IResultTelemetry, StructuredResultTelemetry>();

        return builder;
    }

    public static WebApplication UseViradaRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                var user = httpContext.User;
                var isAuthenticated = user.Identity?.IsAuthenticated == true;
                var tenantClaim = isAuthenticated
                    ? user.FindFirst(TenantClaimTypes.TenantId)?.Value
                    : null;
                var userClaim = isAuthenticated
                    ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    : null;
                var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

                diagnosticContext.Set("TenantId", string.IsNullOrWhiteSpace(tenantClaim) ? "anonymous" : tenantClaim);
                diagnosticContext.Set("UserId", string.IsNullOrWhiteSpace(userClaim) ? "anonymous" : userClaim);
                diagnosticContext.Set("TraceId", traceId);
                diagnosticContext.Set("IsAuthenticated", isAuthenticated);
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            };
        });

        return app;
    }

    public static IReadOnlyDictionary<string, string> ParseOtlpHeaders(string? rawHeaders)
    {
        if (string.IsNullOrWhiteSpace(rawHeaders))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = rawHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            var separatorIndex = entry.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == entry.Length - 1)
            {
                continue;
            }

            var key = entry[..separatorIndex].Trim();
            var value = Uri.UnescapeDataString(entry[(separatorIndex + 1)..].Trim());
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                headers[key] = value;
            }
        }

        return headers;
    }

    public static string? FormatOtlpHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (headers.Count == 0)
        {
            return null;
        }

        return string.Join(',', headers.Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }
}
