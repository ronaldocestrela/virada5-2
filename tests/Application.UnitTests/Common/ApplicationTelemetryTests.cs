using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Domain.Common;

namespace Application.UnitTests.Common;

public class ApplicationTelemetryTests
{
    [Fact]
    public void ObserveSuccess_WhenResultSucceeds_ShouldRecordExecutionMetricAndActivityTag()
    {
        // Arrange
        var uniqueUseCase = $"GenerateRoster5x2_{Guid.NewGuid():N}";
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);
        var recordedActivities = new ConcurrentQueue<Activity>();
        using var activityListener = CreateActivityListener(recordedActivities);
        var spyTelemetry = new SpyResultTelemetry();
        var tenantId = Guid.NewGuid();

        // Act
        var result = ApplicationTelemetry.Observe(
            uniqueUseCase,
            tenantId,
            spyTelemetry,
            () => Result.Success("ok"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        spyTelemetry.RecordedOutcomes.Should().ContainSingle(x =>
            x.UseCaseName == uniqueUseCase &&
            x.IsSuccess &&
            x.TenantId == tenantId &&
            x.ErrorCode == string.Empty);

        var scopedMeasurements = recordedMeasurements
            .Where(m => m.Tags.TryGetValue("usecase", out var uc) && Equals(uc, uniqueUseCase))
            .ToList();

        scopedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.UseCaseExecutionsCounterName &&
            m.LongValue == 1 &&
            Equals(m.Tags["outcome"], "success"));

        scopedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.UseCaseDurationHistogramName &&
            m.DoubleValue >= 0);

        scopedMeasurements.Should().NotContain(m =>
            m.Instrument == ApplicationTelemetry.ResultFailuresCounterName);

        recordedActivities.Should().ContainSingle(a =>
            a.OperationName == $"UseCase.{uniqueUseCase}" &&
            Equals(a.GetTagItem("virada5x2.result.is_success"), true) &&
            Equals(a.GetTagItem("virada5x2.tenant_id"), tenantId.ToString()));
    }

    [Fact]
    public async Task ObserveAsync_WhenResultFailsWithValidation_ShouldRecordFailureMetricAndErrorCode()
    {
        // Arrange
        var uniqueUseCase = $"GenerateRoster5x2Fail_{Guid.NewGuid():N}";
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);
        var recordedActivities = new ConcurrentQueue<Activity>();
        using var activityListener = CreateActivityListener(recordedActivities);
        var spyTelemetry = new SpyResultTelemetry();
        var tenantId = Guid.NewGuid();
        var error = Error.Validation("RosterGeneration.NoActiveEmployees", "Sem colaboradores ativos.");

        // Act
        var result = await ApplicationTelemetry.ObserveAsync(
            uniqueUseCase,
            tenantId,
            spyTelemetry,
            () => Task.FromResult(Result.Failure<string>(error)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);

        spyTelemetry.RecordedOutcomes.Should().ContainSingle(x =>
            x.UseCaseName == uniqueUseCase &&
            !x.IsSuccess &&
            x.ErrorCode == "RosterGeneration.NoActiveEmployees" &&
            x.ErrorCategory == "Validation");

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.ResultFailuresCounterName &&
            m.LongValue == 1 &&
            Equals(m.Tags["usecase"], uniqueUseCase) &&
            Equals(m.Tags["error.code"], "RosterGeneration.NoActiveEmployees") &&
            Equals(m.Tags["error.category"], "Validation"));

        recordedActivities.Should().ContainSingle(a =>
            a.OperationName == $"UseCase.{uniqueUseCase}" &&
            Equals(a.GetTagItem("virada5x2.result.is_success"), false) &&
            Equals(a.GetTagItem("virada5x2.error.code"), "RosterGeneration.NoActiveEmployees"));
    }

    [Theory]
    [InlineData("RosterGeneration.PublishedRosterExists", "Conflict")]
    [InlineData("Employee.NotFound", "NotFound")]
    [InlineData("RosterGeneration.EmptyTenant", "Validation")]
    [InlineData("Unexpected.Failure", "Failure")]
    public void ClassifyErrorCategory_WhenGivenErrorCode_ShouldClassifyDeterministically(
        string code,
        string expectedCategory)
    {
        // Act
        var category = ApplicationTelemetry.ClassifyErrorCategory(new Error(code, "mensagem"));

        // Assert
        category.Should().Be(expectedCategory);
    }

    [Fact]
    public void Observe_WhenTelemetrySinkIsNull_ShouldStillExecuteAndEmitBclMetrics()
    {
        // Arrange
        var uniqueUseCase = $"SaveCompanySettings_{Guid.NewGuid():N}";
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);

        // Act
        var result = ApplicationTelemetry.Observe(
            uniqueUseCase,
            null,
            null,
            () => Result.Failure(Error.Conflict("Company.AlreadyExists", "Conflito")));

        // Assert
        result.IsFailure.Should().BeTrue();
        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.ResultFailuresCounterName &&
            Equals(m.Tags["usecase"], uniqueUseCase) &&
            Equals(m.Tags["error.code"], "Company.AlreadyExists"));
    }

    [Fact]
    public void RecordConversion_WhenCalledWithUtmParameters_ShouldEmitMetricAndNotifySink()
    {
        // Arrange
        var uniqueEvent = $"ebook_landing_view_{Guid.NewGuid():N}";
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);
        var spyTelemetry = new SpyResultTelemetry();

        // Act
        ApplicationTelemetry.RecordConversion(
            uniqueEvent,
            "ebook",
            "qrcode",
            "lancamento_5x2",
            spyTelemetry);

        // Assert
        spyTelemetry.RecordedConversions.Should().ContainSingle(c =>
            c.EventName == uniqueEvent &&
            c.UtmSource == "ebook" &&
            c.UtmMedium == "qrcode" &&
            c.UtmCampaign == "lancamento_5x2");

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.GtmConversionsCounterName &&
            Equals(m.Tags["event"], uniqueEvent) &&
            Equals(m.Tags["utm_source"], "ebook"));
    }

    [Fact]
    public void RecordConversion_WhenCalledWithUtmContent_ShouldEmitUtmContentTagAndNotifySink()
    {
        // Arrange
        var uniqueEvent = $"ebook_landing_view_{Guid.NewGuid():N}";
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);
        var spyTelemetry = new SpyResultTelemetry();

        // Act
        ApplicationTelemetry.RecordConversion(
            uniqueEvent,
            "ebook",
            "qrcode",
            "transicao_5x2",
            spyTelemetry,
            "cap4_simulador");

        // Assert
        spyTelemetry.RecordedConversions.Should().ContainSingle(c =>
            c.EventName == uniqueEvent &&
            c.UtmContent == "cap4_simulador");

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.GtmConversionsCounterName &&
            Equals(m.Tags["event"], uniqueEvent) &&
            Equals(m.Tags["utm_content"], "cap4_simulador"));
    }

    [Fact]
    public async Task ObserveAsync_WhenTenantResolvedDynamically_ShouldTagActivityAndNotifySinkWithResolvedTenantId()
    {
        // Arrange
        var uniqueUseCase = $"DynamicTenant_{Guid.NewGuid():N}";
        var resolvedTenantId = Guid.NewGuid();
        var recordedActivities = new ConcurrentQueue<Activity>();
        using var activityListener = CreateActivityListener(recordedActivities);
        var spyTelemetry = new SpyResultTelemetry();

        // Act
        var result = await ApplicationTelemetry.ObserveAsync(
            uniqueUseCase,
            null,
            spyTelemetry,
            ctx =>
            {
                ctx.SetTenantId(resolvedTenantId);
                return Task.FromResult(Result.Success("ok"));
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        spyTelemetry.RecordedOutcomes.Should().ContainSingle(x =>
            x.UseCaseName == uniqueUseCase &&
            x.IsSuccess &&
            x.TenantId == resolvedTenantId);

        recordedActivities.Should().ContainSingle(a =>
            a.OperationName == $"UseCase.{uniqueUseCase}" &&
            Equals(a.GetTagItem("virada5x2.tenant_id"), resolvedTenantId.ToString()));
    }

    [Fact]
    public async Task ObserveAsync_WhenErrorCategoryIsFailure_ShouldSetActivityStatusError()
    {
        // Arrange
        var uniqueUseCase = $"FailureStatus_{Guid.NewGuid():N}";
        var recordedActivities = new ConcurrentQueue<Activity>();
        using var activityListener = CreateActivityListener(recordedActivities);
        var error = Error.Failure("Unexpected.Failure", "Falha infraestrutural.");

        // Act
        var result = await ApplicationTelemetry.ObserveAsync(
            uniqueUseCase,
            Guid.NewGuid(),
            null,
            () => Task.FromResult(Result.Failure(error)));

        // Assert
        result.IsFailure.Should().BeTrue();
        recordedActivities.Should().ContainSingle(a =>
            a.OperationName == $"UseCase.{uniqueUseCase}" &&
            a.Status == ActivityStatusCode.Error &&
            a.StatusDescription == "Falha infraestrutural.");
    }

    [Fact]
    public async Task ObserveAsync_WhenActionThrowsUnexpectedException_ShouldRecordFailureTelemetryAndRethrow()
    {
        // Arrange
        var uniqueUseCase = $"UnhandledException_{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();
        var recordedMeasurements = new ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)>();
        using var meterListener = CreateMeterListener(recordedMeasurements);
        var recordedActivities = new ConcurrentQueue<Activity>();
        using var activityListener = CreateActivityListener(recordedActivities);
        var spyTelemetry = new SpyResultTelemetry();

        // Act
        Func<Task> act = () => ApplicationTelemetry.ObserveAsync<Result>(
            uniqueUseCase,
            tenantId,
            spyTelemetry,
            () => throw new InvalidOperationException("Falha inesperada de banco."));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Falha inesperada de banco.");

        spyTelemetry.RecordedOutcomes.Should().ContainSingle(x =>
            x.UseCaseName == uniqueUseCase &&
            !x.IsSuccess &&
            x.ErrorCode == "Unexpected.Exception" &&
            x.ErrorCategory == "Failure" &&
            x.TenantId == tenantId);

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.UseCaseExecutionsCounterName &&
            Equals(m.Tags["usecase"], uniqueUseCase) &&
            Equals(m.Tags["outcome"], "exception"));

        recordedMeasurements.Should().Contain(m =>
            m.Instrument == ApplicationTelemetry.ResultFailuresCounterName &&
            Equals(m.Tags["usecase"], uniqueUseCase) &&
            Equals(m.Tags["error.code"], "Unexpected.Exception") &&
            Equals(m.Tags["error.category"], "Failure"));

        recordedActivities.Should().ContainSingle(a =>
            a.OperationName == $"UseCase.{uniqueUseCase}" &&
            a.Status == ActivityStatusCode.Error &&
            Equals(a.GetTagItem("exception.type"), typeof(InvalidOperationException).FullName));
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<(string Instrument, long LongValue, double DoubleValue, Dictionary<string, object?> Tags)> sink)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ApplicationTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            sink.Enqueue((instrument.Name, measurement, measurement, ToDict(tags)));
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            sink.Enqueue((instrument.Name, 0, measurement, ToDict(tags)));
        });

        listener.Start();
        return listener;
    }

    private static ActivityListener CreateActivityListener(ConcurrentQueue<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => sink.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Dictionary<string, object?> ToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            dict[tag.Key] = tag.Value;
        }

        return dict;
    }

    private sealed class SpyResultTelemetry : IResultTelemetry
    {
        public List<(string UseCaseName, bool IsSuccess, string ErrorCode, string? ErrorCategory, Guid? TenantId)> RecordedOutcomes { get; } = [];
        public List<(string EventName, string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmContent)> RecordedConversions { get; } = [];

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
            string? utmCampaign = null,
            string? utmContent = null)
        {
            RecordedConversions.Add((eventName, utmSource, utmMedium, utmCampaign, utmContent));
        }
    }
}
