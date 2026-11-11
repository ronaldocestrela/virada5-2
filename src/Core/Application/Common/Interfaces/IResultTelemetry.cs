using Domain.Common;

namespace Application.Common.Interfaces;

/// <summary>
/// Canal de observabilidade para registrar resultados (<see cref="Result"/> / <see cref="Result{TValue}"/>)
/// e eventos de conversão GTM sem acoplar a camada Application a bibliotecas externas de logging.
/// </summary>
public interface IResultTelemetry
{
    void RecordUseCaseOutcome(
        string useCaseName,
        Result result,
        long elapsedMilliseconds,
        Guid? tenantId = null,
        string? errorCategory = null);

    void RecordConversionEvent(
        string eventName,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null);
}

