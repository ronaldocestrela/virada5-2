using Application.Common.Interfaces;
using Domain.Common;

namespace Application.UnitTests.Fakes;

internal sealed class FakeResultTelemetry : IResultTelemetry
{
    public List<(string UseCaseName, bool IsSuccess, string ErrorCode, string? ErrorCategory, Guid? TenantId)> RecordedOutcomes { get; } = [];
    public List<(string EventName, string? UtmSource, string? UtmMedium, string? UtmCampaign)> RecordedConversions { get; } = [];

    public void RecordUseCaseOutcome(
        string useCaseName,
        Result result,
        long elapsedMilliseconds,
        Guid? tenantId = null,
        string? errorCategory = null)
    {
        RecordedOutcomes.Add((useCaseName, result.IsSuccess, result.Error.Code, errorCategory, tenantId));
    }

    public void RecordConversionEvent(
        string eventName,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null)
    {
        RecordedConversions.Add((eventName, utmSource, utmMedium, utmCampaign));
    }
}

