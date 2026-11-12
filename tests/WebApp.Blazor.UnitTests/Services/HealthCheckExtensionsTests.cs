using Application.Common.Interfaces;
using Infrastructure.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebApp.Blazor.Extensions;

namespace WebApp.Blazor.UnitTests.Services;

public sealed class HealthCheckExtensionsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"virada-dp-{Guid.NewGuid():N}");

    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseAndDataProtectionWritable_ShouldReportHealthy()
    {
        // Arrange
        await using var sqlite = new SqliteConnection("DataSource=:memory:");
        await sqlite.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUserService>(new StubCurrentUserService());
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqlite));
        services.AddViradaHealthChecks(dataProtectionKeysPath: _tempDir);

        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync(reg => reg.Tags.Contains("ready"));

        // Assert
        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey("database");
        report.Entries["database"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey("dataprotection_keys");
        report.Entries["dataprotection_keys"].Status.Should().Be(HealthStatus.Healthy);
        Directory.Exists(_tempDir).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }
}
