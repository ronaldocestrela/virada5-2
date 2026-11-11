using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Context;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Infrastructure.Data.UnitTests.Identity;

public class AppDbContextIdentityTests
{
    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldConfigureIdentityTablesAndApplicationUserTenantIndex()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer()
            .Options;
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.TenantId.Returns(Guid.Empty);

        using var context = new AppDbContext(options, currentUserService);

        // Act
        var userEntity = context.Model.FindEntityType(typeof(ApplicationUser));
        var roleEntity = context.Model.FindEntityType(typeof(ApplicationRole));

        // Assert
        typeof(AppDbContext).Should().BeDerivedFrom<IdentityDbContext<ApplicationUser, ApplicationRole, Guid>>();

        userEntity.Should().NotBeNull();
        userEntity!.GetTableName().Should().Be("AspNetUsers");
        userEntity!.GetDeclaredQueryFilters().Should().BeEmpty(
            "AspNetUsers precisa ser consultável antes da autenticação para efetuar o login");

        var tenantIdProp = userEntity!.FindProperty(nameof(ApplicationUser.TenantId))!;
        tenantIdProp.IsNullable.Should().BeFalse();

        var fullNameProp = userEntity.FindProperty(nameof(ApplicationUser.FullName))!;
        fullNameProp.IsNullable.Should().BeFalse();
        fullNameProp.GetMaxLength().Should().Be(ApplicationUser.MaxFullNameLength);

        var tenantIndex = userEntity.FindIndex(tenantIdProp);
        tenantIndex.Should().NotBeNull();
        tenantIndex!.GetDatabaseName().Should().Be("IX_AspNetUsers_TenantId");

        roleEntity.Should().NotBeNull();
        roleEntity!.GetTableName().Should().Be("AspNetRoles");
        var descriptionProp = roleEntity!.FindProperty(nameof(ApplicationRole.Description))!;
        descriptionProp.GetMaxLength().Should().Be(ApplicationRole.MaxDescriptionLength);
    }

    [Fact]
    public async Task IdentityManagers_WhenUnauthenticatedContextActive_ShouldFindUserForLoginWhileDomainEntitiesRemainFailClosed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var activeTenantId = tenantId;
        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.TenantId.Returns(_ => activeTenantId);
        currentUserService.IsAuthenticated.Returns(_ => activeTenantId != Guid.Empty);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(currentUserService);
        services.AddInfrastructureData(connectionString: null);
        // Substitui provider para SQLite em memória mantendo todas as configurações do Identity
        services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(connection));

        using var provider = services.BuildServiceProvider();

        using (var setupScope = provider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            var company = Company.Create(
                tenantId,
                "Casa Identity",
                BusinessType.Restaurant,
                new TimeOnly(11, 0),
                new TimeOnly(23, 0),
                ScheduleModel.FixedClosure,
                [DayOfWeek.Monday]).Value;
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            var roleManager = setupScope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await roleManager.CreateAsync(new ApplicationRole(ApplicationRoles.Owner, "Proprietário"));

            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = ApplicationUser.Create(tenantId, "owner@casaidentity.com.br", "Dono da Casa").Value;
            var createResult = await userManager.CreateAsync(user, "SenhaForte123");
            createResult.Succeeded.Should().BeTrue();
            (await userManager.AddToRoleAsync(user, ApplicationRoles.Owner)).Succeeded.Should().BeTrue();
        }

        // Act: simula contexto não autenticado durante o login (TenantId == Guid.Empty)
        activeTenantId = Guid.Empty;

        using var loginScope = provider.CreateScope();
        var loginDb = loginScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginUserManager = loginScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var foundUser = await loginUserManager.FindByEmailAsync("owner@casaidentity.com.br");
        var passwordValid = foundUser is not null && await loginUserManager.CheckPasswordAsync(foundUser, "SenhaForte123");
        var roles = foundUser is not null ? await loginUserManager.GetRolesAsync(foundUser) : [];
        var leakedCompanies = await loginDb.Companies.ToListAsync();

        // Assert
        foundUser.Should().NotBeNull();
        foundUser!.TenantId.Should().Be(tenantId);
        passwordValid.Should().BeTrue();
        roles.Should().ContainSingle().Which.Should().Be(ApplicationRoles.Owner);
        leakedCompanies.Should().BeEmpty("entidades de domínio continuam isoladas em modo fail-closed");
    }
}

