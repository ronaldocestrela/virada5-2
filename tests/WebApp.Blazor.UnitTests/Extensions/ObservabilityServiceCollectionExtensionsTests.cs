using System.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;
using WebApp.Blazor.Extensions;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Extensions;

public class ObservabilityServiceCollectionExtensionsTests
{
    [Fact]
    public void ParseOtlpHeaders_WhenValidCommaSeparatedPairs_ShouldParseTrimmedHeadersAndPreserveEqualsInValues()
    {
        // Arrange
        const string raw = " Authorization = Bearer token== , x-api-key=abc123 , invalid-entry ";

        // Act
        var parsed = ObservabilityServiceCollectionExtensions.ParseOtlpHeaders(raw);
        var formatted = ObservabilityServiceCollectionExtensions.FormatOtlpHeaders(parsed);

        // Assert
        parsed.Should().HaveCount(2);
        parsed["Authorization"].Should().Be("Bearer token==");
        parsed["x-api-key"].Should().Be("abc123");
        formatted.Should().Contain("Authorization=Bearer token==");
        formatted.Should().Contain("x-api-key=abc123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOtlpHeaders_WhenNullOrWhitespace_ShouldReturnEmptyDictionary(string? raw)
    {
        // Act
        var parsed = ObservabilityServiceCollectionExtensions.ParseOtlpHeaders(raw);

        // Assert
        parsed.Should().BeEmpty();
        ObservabilityServiceCollectionExtensions.FormatOtlpHeaders(parsed).Should().BeNull();
    }

    [Fact]
    public void ActivityTraceEnricher_WhenActivityIsActive_ShouldAttachTraceIdAndSpanId()
    {
        // Arrange
        using var activity = new Activity("TestOperation");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var enricher = new ActivityTraceEnricher();
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplate("msg", Array.Empty<MessageTemplateToken>()),
            Array.Empty<LogEventProperty>());

        // Act
        enricher.Enrich(logEvent, new SimplePropertyFactory());

        // Assert
        logEvent.Properties.Should().ContainKey("TraceId");
        logEvent.Properties.Should().ContainKey("SpanId");
        logEvent.Properties["TraceId"].ToString().Should().Contain(activity.TraceId.ToString());
        logEvent.Properties["SpanId"].ToString().Should().Contain(activity.SpanId.ToString());
    }

    private sealed class SimplePropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}

