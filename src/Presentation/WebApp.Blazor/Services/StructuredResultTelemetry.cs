using Application.Common.Interfaces;
using Domain.Common;

namespace WebApp.Blazor.Services;

/// <summary>
/// Registra em logs estruturados (Serilog) os resultados de casos de uso (<see cref="Result"/> / <see cref="Result{TValue}"/>)
/// e eventos de conversão do funil GTM.
/// </summary>
public sealed class StructuredResultTelemetry : IResultTelemetry
{
    private readonly ILogger<StructuredResultTelemetry> _logger;

    public StructuredResultTelemetry(ILogger<StructuredResultTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void RecordUseCaseOutcome(
        string useCaseName,
        Result result,
        long elapsedMilliseconds,
        Guid? tenantId = null,
        string? errorCategory = null)
    {
        var effectiveTenant = tenantId is { } id && id != Guid.Empty ? id.ToString() : "anonymous";

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "UseCase {UseCase} succeeded in {ElapsedMs}ms for Tenant {TenantId}",
                useCaseName,
                elapsedMilliseconds,
                effectiveTenant);
            return;
        }

        if (string.Equals(errorCategory, "Failure", StringComparison.Ordinal))
        {
            _logger.LogError(
                "UseCase {UseCase} failed with {ErrorCode} ({ErrorCategory}) in {ElapsedMs}ms for Tenant {TenantId}: {ErrorMessage}",
                useCaseName,
                result.Error.Code,
                errorCategory,
                elapsedMilliseconds,
                effectiveTenant,
                result.Error.Message);
            return;
        }

        _logger.LogWarning(
            "UseCase {UseCase} returned {ErrorCode} ({ErrorCategory}) in {ElapsedMs}ms for Tenant {TenantId}: {ErrorMessage}",
            useCaseName,
            result.Error.Code,
            errorCategory ?? "Validation",
            elapsedMilliseconds,
            effectiveTenant,
            result.Error.Message);
    }

    public void RecordConversionEvent(
        string eventName,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null)
    {
        _logger.LogInformation(
            "GtmConversion {EventName} utm_source={UtmSource} utm_medium={UtmMedium} utm_campaign={UtmCampaign}",
            eventName,
            string.IsNullOrWhiteSpace(utmSource) ? "direct" : utmSource,
            string.IsNullOrWhiteSpace(utmMedium) ? "none" : utmMedium,
            string.IsNullOrWhiteSpace(utmCampaign) ? "none" : utmCampaign);
    }
}

