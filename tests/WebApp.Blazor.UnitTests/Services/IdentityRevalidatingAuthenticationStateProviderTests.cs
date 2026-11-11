using System.Security.Claims;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Services;

public class IdentityRevalidatingAuthenticationStateProviderTests
{
    [Fact]
    public async Task ValidateAuthenticationStateAsync_WhenUserActiveAndSecurityStampMatches_ShouldReturnTrue()
    {
        // Arrange
        var user = ApplicationUser.Create(Guid.NewGuid(), "gestor@casa.com.br", "Gestor").Value;
        user.SecurityStamp = "stamp-valido";

        var identityOptions = Options.Create(new IdentityOptions());
        var userManager = CreateMockUserManager(user);
        var provider = CreateProvider(userManager, identityOptions);

        var principal = CreatePrincipal(user, identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, "stamp-valido");

        // Act
        var isValid = await provider.ValidateAuthenticationStateForTestAsync(
            new AuthenticationState(principal),
            CancellationToken.None);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAuthenticationStateAsync_WhenSecurityStampDiffers_ShouldReturnFalse()
    {
        // Arrange
        var user = ApplicationUser.Create(Guid.NewGuid(), "gestor@casa.com.br", "Gestor").Value;
        user.SecurityStamp = "stamp-novo";

        var identityOptions = Options.Create(new IdentityOptions());
        var userManager = CreateMockUserManager(user);
        var provider = CreateProvider(userManager, identityOptions);

        var principal = CreatePrincipal(user, identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, "stamp-antigo");

        // Act
        var isValid = await provider.ValidateAuthenticationStateForTestAsync(
            new AuthenticationState(principal),
            CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAuthenticationStateAsync_WhenUserIsInactive_ShouldReturnFalse()
    {
        // Arrange
        var user = ApplicationUser.Create(Guid.NewGuid(), "gestor@casa.com.br", "Gestor").Value;
        user.SecurityStamp = "stamp-valido";
        user.IsActive = false;

        var identityOptions = Options.Create(new IdentityOptions());
        var userManager = CreateMockUserManager(user);
        var provider = CreateProvider(userManager, identityOptions);

        var principal = CreatePrincipal(user, identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, "stamp-valido");

        // Act
        var isValid = await provider.ValidateAuthenticationStateForTestAsync(
            new AuthenticationState(principal),
            CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAuthenticationStateAsync_WhenUserDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var user = ApplicationUser.Create(Guid.NewGuid(), "gestor@casa.com.br", "Gestor").Value;
        var identityOptions = Options.Create(new IdentityOptions());
        var userManager = CreateMockUserManager(null);
        var provider = CreateProvider(userManager, identityOptions);

        var principal = CreatePrincipal(user, identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, "stamp-valido");

        // Act
        var isValid = await provider.ValidateAuthenticationStateForTestAsync(
            new AuthenticationState(principal),
            CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
    }

    private static TestableRevalidatingProvider CreateProvider(
        UserManager<ApplicationUser> userManager,
        IOptions<IdentityOptions> identityOptions)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => userManager);
        var serviceProvider = services.BuildServiceProvider();

        return new TestableRevalidatingProvider(
            NullLoggerFactory.Instance,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            identityOptions);
    }

    private static UserManager<ApplicationUser> CreateMockUserManager(ApplicationUser? userToReturn)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var manager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        manager.SupportsUserSecurityStamp.Returns(true);
        manager.GetUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(userToReturn);
        if (userToReturn is not null)
        {
            manager.GetSecurityStampAsync(userToReturn).Returns(userToReturn.SecurityStamp!);
        }

        return manager;
    }

    private static ClaimsPrincipal CreatePrincipal(ApplicationUser user, string stampClaimType, string stampValue) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new Claim(TenantClaimTypes.TenantId, user.TenantId.ToString("D")),
            new Claim(stampClaimType, stampValue)
        ],
        authenticationType: "Identity.Application"));

    private sealed class TestableRevalidatingProvider(
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options)
        : IdentityRevalidatingAuthenticationStateProvider(loggerFactory, scopeFactory, options)
    {
        public Task<bool> ValidateAuthenticationStateForTestAsync(
            AuthenticationState authenticationState,
            CancellationToken cancellationToken) =>
            ValidateAuthenticationStateAsync(authenticationState, cancellationToken);
    }
}

