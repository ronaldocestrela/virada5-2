using Domain.Common;
using Microsoft.Extensions.Logging;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Services;

public class StructuredResultTelemetryTests
{
    [Fact]
    public void RecordUseCaseOutcome_WhenSuccess_ShouldLogInformationWithStructuredProperties()
    {
        // Arrange
        var logger = new CapturingLogger<StructuredResultTelemetry>();
        var telemetry = new StructuredResultTelemetry(logger);
        var tenantId = Guid.NewGuid();

        // Act
        telemetry.RecordUseCaseOutcome("GenerateRoster5x2", Result.Success(), 42, tenantId);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["UseCase"].Should().Be("GenerateRoster5x2");
        entry.Properties["ElapsedMs"].Should().Be(42L);
        entry.Properties["TenantId"].Should().Be(tenantId.ToString());
    }

    [Theory]
    [InlineData("Validation")]
    [InlineData("Conflict")]
    [InlineData("NotFound")]
    public void RecordUseCaseOutcome_WhenBusinessFailure_ShouldLogWarningWithErrorCodeAndCategory(string category)
    {
        // Arrange
        var logger = new CapturingLogger<StructuredResultTelemetry>();
        var telemetry = new StructuredResultTelemetry(logger);
        var error = Error.Validation("Employee.InvalidName", "Nome obrigatório.");

        // Act
        telemetry.RecordUseCaseOutcome("CreateEmployee", Result.Failure(error), 7, null, category);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Properties["UseCase"].Should().Be("CreateEmployee");
        entry.Properties["ErrorCode"].Should().Be("Employee.InvalidName");
        entry.Properties["ErrorCategory"].Should().Be(category);
        entry.Properties["TenantId"].Should().Be("anonymous");
        entry.Properties["ErrorMessage"].Should().Be("Nome obrigatório.");
    }

    [Fact]
    public void RecordUseCaseOutcome_WhenCategoryIsFailure_ShouldLogError()
    {
        // Arrange
        var logger = new CapturingLogger<StructuredResultTelemetry>();
        var telemetry = new StructuredResultTelemetry(logger);
        var tenantId = Guid.NewGuid();
        var error = Error.Failure("Unexpected.Exception", "Erro inesperado no banco.");

        // Act
        telemetry.RecordUseCaseOutcome("PublishRoster", Result.Failure(error), 15, tenantId, "Failure");

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Properties["UseCase"].Should().Be("PublishRoster");
        entry.Properties["ErrorCode"].Should().Be("Unexpected.Exception");
        entry.Properties["ErrorCategory"].Should().Be("Failure");
        entry.Properties["TenantId"].Should().Be(tenantId.ToString());
    }

    [Fact]
    public void RecordConversionEvent_WhenUtmMissing_ShouldDefaultToDirectAndNone()
    {
        // Arrange
        var logger = new CapturingLogger<StructuredResultTelemetry>();
        var telemetry = new StructuredResultTelemetry(logger);

        // Act
        telemetry.RecordConversionEvent("ebook_landing_view");

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["EventName"].Should().Be("ebook_landing_view");
        entry.Properties["UtmSource"].Should().Be("direct");
        entry.Properties["UtmMedium"].Should().Be("none");
        entry.Properties["UtmCampaign"].Should().Be("none");
        entry.Properties["UtmContent"].Should().Be("none");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    props[kvp.Key] = kvp.Value;
                }
            }

            Entries.Add((logLevel, formatter(state, exception), props));
        }
    }
}

