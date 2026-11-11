using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace WebApp.Blazor.Services;

/// <summary>
/// Enriquece eventos de log Serilog com <c>TraceId</c> e <c>SpanId</c> da <see cref="Activity.Current"/>
/// para correlação distribuída entre logs, traces e métricas OpenTelemetry.
/// </summary>
public sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        if (activity.TraceId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        }

        if (activity.SpanId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        }
    }
}

