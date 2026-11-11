using Application.Common.Interfaces;
using Infrastructure.Data;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Data.UnitTests.Identity;

public sealed class TenantRegistrationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TenantRegistrationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUserService>(new StubCurrentUserService());
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<ITenantRegistrationService, TenantRegistrationService>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task RegisterOwnerAsync_WhenValid_ShouldCreateIsolatedTenantAndAssignOwnerRole()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ITenantRegistrationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Act
        var result = await sut.RegisterOwnerAsync("João Dono", "joao@padaria5x2.com.br", "SenhaForte123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().NotBe(Guid.Empty);
        var user = await userManager.FindByEmailAsync("joao@padaria5x2.com.br");
        user.Should().NotBeNull();
        user!.TenantId.Should().Be(result.Value.TenantId);
        (await userManager.IsInRoleAsync(user, ApplicationRoles.Owner)).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterOwnerAsync_WhenEmailDuplicate_ShouldReturnConflictFailure()
    {
        // Arrange
        using var scope = _provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ITenantRegistrationService>();
        (await sut.RegisterOwnerAsync("João Um", "duplicado@padaria5x2.com.br", "SenhaForte123")).IsSuccess.Should().BeTrue();

        // Act
        var duplicate = await sut.RegisterOwnerAsync("João Dois", "duplicado@padaria5x2.com.br", "SenhaForte123");

        // Assert
        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Code.Should().Be("TenantRegistration.DuplicateEmail");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }
}

