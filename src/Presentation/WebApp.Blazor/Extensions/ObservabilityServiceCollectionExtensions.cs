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
        var minLevelText = DotEnvLoader.ResolveValue("SERILOG_MINIMUM_LEVEL");
        var logFilePath = DotEnvLoader.ResolveValue("SERILOG_LOG_FILE_PATH");

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

                if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(ApplicationTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpUri);
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

                diagnosticContext.Set("TenantId", string.IsNullOrWhiteSpace(tenantClaim) ? "anonymous" : tenantClaim);
                diagnosticContext.Set("IsAuthenticated", isAuthenticated);
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            };
        });

        return app;
    }
}
