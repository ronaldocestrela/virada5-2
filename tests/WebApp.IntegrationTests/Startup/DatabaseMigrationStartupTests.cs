using Application.Common.Interfaces;
using Infrastructure.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebApp.Blazor.Extensions;
using WebApp.IntegrationTests.Fixtures;

namespace WebApp.IntegrationTests.Startup;

public sealed class DatabaseMigrationStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void ShouldRunMigrationsOnStartup_WhenFlagIsNotFalse_ShouldReturnTrue(string? flagValue)
    {
        // Arrange
        var tempDir = CreateTempDirectoryWithoutEnv();
        try
        {
            var configuration = BuildConfiguration(flagValue is null
                ? []
                : new Dictionary<string, string?> { ["RUN_MIGRATIONS_ON_STARTUP"] = flagValue });

            var previousValue = Environment.GetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP");
            Environment.SetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP", null);
            try
            {
                // Act
                var result = DatabaseMigrationExtensions.ShouldRunMigrationsOnStartup(configuration, tempDir);

                // Assert
                result.Should().BeTrue();
            }
            finally
            {
                Environment.SetEnvironmentVariable("RUN_MIGRATIONS_ON_STARTUP", previousValue);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData(" False ")]
    public void ShouldRunMigrationsOnStartup_WhenFlagIsFalse_ShouldReturnFalse(string flagValue)
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RUN_MIGRATIONS_ON_STARTUP"] = flagValue
        });

        // Act
        var result = DatabaseMigrationExtensions.ShouldRunMigrationsOnStartup(configuration);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyDatabaseMigrationsOnStartupAsync_WhenDisabledViaConfiguration_ShouldNotExecuteMigrations()
    {
        // Arrange
        using var provider = BuildSqlServerServiceProvider();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RUN_MIGRATIONS_ON_STARTUP"] = "false"
        });
        var logger = new TestLogger();
        var executorInvoked = false;

        // Act
        await DatabaseMigrationExtensions.ApplyDatabaseMigrationsOnStartupAsync(
            provider,
            configuration,
            logger,
            retryDelays: [TimeSpan.Zero],
            migrateExecutor: (_, _) =>
            {
                executorInvoked = true;
                return Task.CompletedTask;
            });

        // Assert
        executorInvoked.Should().BeFalse();
        logger.Messages.Should().Contain(m => m.Contains("RUN_MIGRATIONS_ON_STARTUP=false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyDatabaseMigrationsOnStartupAsync_WhenProviderIsSqlite_ShouldSkipMigrationsSafely()
    {
        // Arrange
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var provider = BuildSqliteServiceProvider(connection);

        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RUN_MIGRATIONS_ON_STARTUP"] = "true"
        });
        var logger = new TestLogger();
        var executorInvoked = false;

        // Act
        await DatabaseMigrationExtensions.ApplyDatabaseMigrationsOnStartupAsync(
            provider,
            configuration,
            logger,
            retryDelays: [TimeSpan.Zero],
            migrateExecutor: (_, _) =>
            {
                executorInvoked = true;
                return Task.CompletedTask;
            });

        // Assert
        executorInvoked.Should().BeFalse();
        logger.Messages.Should().Contain(m => m.Contains("SQL Server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyDatabaseMigrationsOnStartupAsync_WhenTransientFailureOccurs_ShouldRetryAndSucceed()
    {
        // Arrange
        using var provider = BuildSqlServerServiceProvider();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RUN_MIGRATIONS_ON_STARTUP"] = "true"
        });
        var logger = new TestLogger();
        var attempts = 0;

        // Act
        await DatabaseMigrationExtensions.ApplyDatabaseMigrationsOnStartupAsync(
            provider,
            configuration,
            logger,
            retryDelays: [TimeSpan.Zero, TimeSpan.Zero],
            migrateExecutor: (_, _) =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new InvalidOperationException("SQL Server ainda iniciando.");
                }

                return Task.CompletedTask;
            });

        // Assert
        attempts.Should().Be(2);
        logger.Messages.Should().Contain(m => m.Contains("tentativa 1/3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyDatabaseMigrationsOnStartupAsync_WhenAllRetriesFail_ShouldRethrowException()
    {
        // Arrange
        using var provider = BuildSqlServerServiceProvider();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RUN_MIGRATIONS_ON_STARTUP"] = "true"
        });
        var logger = new TestLogger();
        var attempts = 0;

        // Act
        var act = async () => await DatabaseMigrationExtensions.ApplyDatabaseMigrationsOnStartupAsync(
            provider,
            configuration,
            logger,
            retryDelays: [TimeSpan.Zero, TimeSpan.Zero],
            migrateExecutor: (_, _) =>
            {
                attempts++;
                throw new InvalidOperationException("Banco indisponível.");
            });

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Banco indisponível.");
        attempts.Should().Be(3);
        logger.Messages.Should().Contain(m => m.Contains("Falha definitiva", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebApplicationFactory_WhenStartupMigrationsEnabledByDefaultWithSqlite_ShouldBootHealthy()
    {
        // Arrange
        using var factory = new TenantIsolationWebApplicationFactory();
        var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    private static ServiceProvider BuildSqlServerServiceProvider() =>
        BuildServiceProvider(options =>
            options.UseSqlServer("Server=localhost,1433;Database=Scale5x2Test;User Id=sa;Password=FakePassword123!;TrustServerCertificate=True;"));

    private static ServiceProvider BuildSqliteServiceProvider(SqliteConnection connection) =>
        BuildServiceProvider(options => options.UseSqlite(connection));

    private static ServiceProvider BuildServiceProvider(Action<DbContextOptionsBuilder> configureDb)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, StubCurrentUserService>();
        services.AddDbContext<AppDbContext>(configureDb);
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static string CreateTempDirectoryWithoutEnv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"virada-mig-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}

