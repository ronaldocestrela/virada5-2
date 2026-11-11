using System.Diagnostics;
using System.Diagnostics.Metrics;
using Application.Common.Interfaces;
using Domain.Common;

namespace Application.Common.Telemetry;

/// <summary>
/// Contexto mutável de execução para captura tardia de <c>TenantId</c> em fluxos que o resolvem durante o caso de uso.
/// </summary>
public sealed class TelemetryExecutionContext
{
    internal TelemetryExecutionContext(Guid? initialTenantId)
    {
        TenantId = initialTenantId is { } id && id != Guid.Empty ? id : null;
    }

    public Guid? TenantId { get; private set; }

    public void SetTenantId(Guid tenantId)
    {
        if (tenantId != Guid.Empty)
        {
            TenantId = tenantId;
        }
    }
}

/// <summary>
/// Instrumentação nativa via BCL (<see cref="ActivitySource"/> e <see cref="Meter"/>) para casos de uso
/// baseados em <see cref="Result"/> e funil GTM, compatível com OpenTelemetry e Serilog.
/// </summary>
public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "Virada5x2.Application";
    public const string MeterName = "Virada5x2.Application";
    public const string UseCaseExecutionsCounterName = "virada5x2.usecase.executions";
    public const string ResultFailuresCounterName = "virada5x2.result.failures";
    public const string UseCaseDurationHistogramName = "virada5x2.usecase.duration_ms";
    public const string GtmConversionsCounterName = "virada5x2.gtm.conversions";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> UseCaseExecutions =
        Meter.CreateCounter<long>(UseCaseExecutionsCounterName, unit: "{execution}");

    private static readonly Counter<long> ResultFailures =
        Meter.CreateCounter<long>(ResultFailuresCounterName, unit: "{failure}");

    private static readonly Histogram<double> UseCaseDurationMs =
        Meter.CreateHistogram<double>(UseCaseDurationHistogramName, unit: "ms");

    private static readonly Counter<long> GtmConversions =
        Meter.CreateCounter<long>(GtmConversionsCounterName, unit: "{conversion}");

    public static TResult Observe<TResult>(
        string useCaseName,
        Guid? tenantId,
        IResultTelemetry? sink,
        Func<TResult> action)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(action);
        return Observe(useCaseName, tenantId, sink, _ => action());
    }

    public static TResult Observe<TResult>(
        string useCaseName,
        Guid? initialTenantId,
        IResultTelemetry? sink,
        Func<TelemetryExecutionContext, TResult> action)
        where TResult : Result
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(useCaseName);
        ArgumentNullException.ThrowIfNull(action);

        var context = new TelemetryExecutionContext(initialTenantId);
        using var activity = StartUseCaseActivity(useCaseName, context.TenantId);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = action(context);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            RecordOutcome(useCaseName, result, elapsedMs, context.TenantId, sink, activity);
            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            RecordException(useCaseName, ex, elapsedMs, context.TenantId, sink, activity);
            throw;
        }
    }

    public static Task<TResult> ObserveAsync<TResult>(
        string useCaseName,
        Guid? tenantId,
        IResultTelemetry? sink,
        Func<Task<TResult>> action)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(action);
        return ObserveAsync(useCaseName, tenantId, sink, _ => action());
    }

    public static async Task<TResult> ObserveAsync<TResult>(
        string useCaseName,
        Guid? initialTenantId,
        IResultTelemetry? sink,
        Func<TelemetryExecutionContext, Task<TResult>> action)
        where TResult : Result
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(useCaseName);
        ArgumentNullException.ThrowIfNull(action);

        var context = new TelemetryExecutionContext(initialTenantId);
        using var activity = StartUseCaseActivity(useCaseName, context.TenantId);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = await action(context);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            RecordOutcome(useCaseName, result, elapsedMs, context.TenantId, sink, activity);
            return result;
        }
        catch (Exception ex)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            RecordException(useCaseName, ex, elapsedMs, context.TenantId, sink, activity);
            throw;
        }
    }

    public static void RecordConversion(
        string eventName,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign,
        IResultTelemetry? sink = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        var tags = new TagList
        {
            { "event", eventName },
            { "utm_source", string.IsNullOrWhiteSpace(utmSource) ? "direct" : utmSource.Trim() },
            { "utm_medium", string.IsNullOrWhiteSpace(utmMedium) ? "none" : utmMedium.Trim() },
            { "utm_campaign", string.IsNullOrWhiteSpace(utmCampaign) ? "none" : utmCampaign.Trim() }
        };

        GtmConversions.Add(1, tags);
        sink?.RecordConversionEvent(eventName, utmSource, utmMedium, utmCampaign);
    }

    public static string ClassifyErrorCategory(Error error)
    {
        if (error == Error.None || string.IsNullOrWhiteSpace(error.Code))
        {
            return "None";
        }

        var code = error.Code;
        if (code.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
        {
            return "NotFound";
        }

        if (code.Contains("Conflict", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Exists", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Published", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Expired", StringComparison.OrdinalIgnoreCase))
        {
            return "Conflict";
        }

        if (code.Contains("Unexpected", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) ||
            code.EndsWith(".Failure", StringComparison.OrdinalIgnoreCase))
        {
            return "Failure";
        }

        return "Validation";
    }

    private static Activity? StartUseCaseActivity(string useCaseName, Guid? tenantId)
    {
        var activity = ActivitySource.StartActivity($"UseCase.{useCaseName}", ActivityKind.Internal);
        SetTenantTag(activity, tenantId);
        return activity;
    }

    private static void SetTenantTag(Activity? activity, Guid? tenantId)
    {
        if (activity is not null && tenantId is { } id && id != Guid.Empty)
        {
            activity.SetTag("virada5x2.tenant_id", id.ToString());
        }
    }

    private static void RecordOutcome(
        string useCaseName,
        Result result,
        long elapsedMs,
        Guid? tenantId,
        IResultTelemetry? sink,
        Activity? activity)
    {
        var outcome = result.IsSuccess ? "success" : "failure";
        var errorCategory = result.IsFailure ? ClassifyErrorCategory(result.Error) : null;

        SetTenantTag(activity, tenantId);
        RecordExecutionAndDuration(useCaseName, outcome, elapsedMs);

        if (result.IsFailure)
        {
            RecordFailureMetric(useCaseName, result.Error.Code, errorCategory);
        }

        if (activity is not null)
        {
            activity.SetTag("virada5x2.result.is_success", result.IsSuccess);
            if (result.IsFailure)
            {
                activity.SetTag("virada5x2.error.code", result.Error.Code);
                activity.SetTag("virada5x2.error.category", errorCategory);
                if (string.Equals(errorCategory, "Failure", StringComparison.Ordinal))
                {
                    activity.SetStatus(ActivityStatusCode.Error, result.Error.Message);
                }
            }
        }

        sink?.RecordUseCaseOutcome(useCaseName, result, elapsedMs, tenantId, errorCategory);
    }

    private static void RecordException(
        string useCaseName,
        Exception exception,
        long elapsedMs,
        Guid? tenantId,
        IResultTelemetry? sink,
        Activity? activity)
    {
        const string errorCode = "Unexpected.Exception";
        const string errorCategory = "Failure";

        SetTenantTag(activity, tenantId);
        RecordExecutionAndDuration(useCaseName, "exception", elapsedMs);
        RecordFailureMetric(useCaseName, errorCode, errorCategory);

        if (activity is not null)
        {
            activity.SetTag("virada5x2.result.is_success", false);
            activity.SetTag("virada5x2.error.code", errorCode);
            activity.SetTag("virada5x2.error.category", errorCategory);
            activity.SetTag("exception.type", exception.GetType().FullName);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        sink?.RecordUseCaseOutcome(
            useCaseName,
            Result.Failure(Error.Failure(errorCode, exception.Message)),
            elapsedMs,
            tenantId,
            errorCategory);
    }

    private static void RecordExecutionAndDuration(string useCaseName, string outcome, long elapsedMs)
    {
        UseCaseExecutions.Add(1, new TagList
        {
            { "usecase", useCaseName },
            { "outcome", outcome }
        });

        UseCaseDurationMs.Record(elapsedMs, new TagList
        {
            { "usecase", useCaseName },
            { "outcome", outcome }
        });
    }

    private static void RecordFailureMetric(string useCaseName, string errorCode, string? errorCategory)
    {
        ResultFailures.Add(1, new TagList
        {
            { "usecase", useCaseName },
            { "error.code", errorCode },
            { "error.category", errorCategory }
        });
    }
}
