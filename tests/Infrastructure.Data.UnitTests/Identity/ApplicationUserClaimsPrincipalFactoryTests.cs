using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Infrastructure.Data.UnitTests.Identity;

public class ApplicationUserClaimsPrincipalFactoryTests
{
    [Fact]
    public async Task CreateAsync_WhenUserHasValidTenantIdAndFullName_ShouldIncludeTenantAndFullNameClaims()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var user = ApplicationUser.Create(tenantId, "dono@casa.com.br", "Carlos Souza").Value;

        var userManager = CreateMockUserManager(user, ["Owner"]);
        var roleManager = CreateMockRoleManager();
        var optionsAccessor = Options.Create(new IdentityOptions());

        var factory = new ApplicationUserClaimsPrincipalFactory(userManager, roleManager, optionsAccessor);

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert
        principal.FindFirst(TenantClaimTypes.TenantId)?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst(TenantClaimTypes.FullName)?.Value.Should().Be("Carlos Souza");
        principal.IsInRole("Owner").Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WhenUserHasEmptyTenantId_ShouldNotEmitTenantIdClaimFailClosed()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            UserName = "semtenant@casa.com.br",
            Email = "semtenant@casa.com.br",
            FullName = "Sem Tenant"
        };

        var userManager = CreateMockUserManager(user, []);
        var roleManager = CreateMockRoleManager();
        var optionsAccessor = Options.Create(new IdentityOptions());

        var factory = new ApplicationUserClaimsPrincipalFactory(userManager, roleManager, optionsAccessor);

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert
        principal.FindFirst(TenantClaimTypes.TenantId).Should().BeNull();
    }

    private static UserManager<ApplicationUser> CreateMockUserManager(ApplicationUser user, IList<string> roles)
    {
        var store = Substitute.For<IUserRoleStore<ApplicationUser>, IUserEmailStore<ApplicationUser>>();
        var manager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>>>());

        manager.SupportsUserRole.Returns(true);
        manager.GetUserIdAsync(user).Returns(user.Id.ToString("D"));
        manager.GetUserNameAsync(user).Returns(user.UserName);
        manager.GetRolesAsync(user).Returns(roles);

        return manager;
    }

    private static RoleManager<ApplicationRole> CreateMockRoleManager()
    {
        var store = Substitute.For<IRoleStore<ApplicationRole>>();
        return Substitute.For<RoleManager<ApplicationRole>>(
            store,
            Array.Empty<IRoleValidator<ApplicationRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<RoleManager<ApplicationRole>>>());
    }
}

