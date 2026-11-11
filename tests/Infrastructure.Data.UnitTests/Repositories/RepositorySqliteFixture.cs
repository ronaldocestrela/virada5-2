using Application.Common.Interfaces;
using Infrastructure.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Infrastructure.Data.UnitTests.Repositories;

internal sealed class RepositorySqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private bool _initialized;

    public RepositorySqliteFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public AppDbContext CreateContext(Guid tenantId)
    {
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.TenantId.Returns(tenantId);
        currentUserService.UserId.Returns(tenantId == Guid.Empty ? null : Guid.NewGuid());
        currentUserService.IsAuthenticated.Returns(tenantId != Guid.Empty);

        var context = new AppDbContext(_options, currentUserService);
        if (!_initialized)
        {
            context.Database.EnsureCreated();
            _initialized = true;
        }

        return context;
    }

    public void Dispose() => _connection.Dispose();
}

