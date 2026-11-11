using System.Security.Claims;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Services;

public class CurrentUserServiceTests
{
    [Fact]
    public void Properties_WhenHttpContextIsNullAndNoAuthState_ShouldFailClosedWithEmptyTenant()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var service = new CurrentUserService(httpContextAccessor);

        // Act & Assert
        service.IsAuthenticated.Should().BeFalse();
        service.UserId.Should().BeNull();
        service.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Properties_WhenHttpContextUserAuthenticatedWithValidClaims_ShouldExposeUserIdAndTenantId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(TenantClaimTypes.TenantId, tenantId.ToString("D"))
        ],
        authenticationType: "Identity.Application");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var service = new CurrentUserService(httpContextAccessor);

        // Act & Assert
        service.IsAuthenticated.Should().BeTrue();
        service.UserId.Should().Be(userId);
        service.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Properties_WhenAuthenticatedWithoutTenantClaim_ShouldFailClosedWithEmptyTenantId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))
        ],
        authenticationType: "Identity.Application");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var service = new CurrentUserService(httpContextAccessor);

        // Act & Assert
        service.IsAuthenticated.Should().BeFalse();
        service.UserId.Should().Be(userId);
        service.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Properties_WhenHttpContextIsNullAndAuthStateCompleted_ShouldFallbackToAuthenticationStateProvider()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(TenantClaimTypes.TenantId, tenantId.ToString("D"))
        ],
        authenticationType: "Identity.Application"));

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(principal)));

        var service = new CurrentUserService(httpContextAccessor, authStateProvider);

        // Act & Assert
        service.IsAuthenticated.Should().BeTrue();
        service.UserId.Should().Be(userId);
        service.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantId_WhenScopedTenantContextIsActiveAndUnauthenticated_ShouldReturnScopedTenantAndRestoreOnDispose()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var scopedContext = new ScopedTenantContext();
        var service = new CurrentUserService(httpContextAccessor, scopedTenantContext: scopedContext);
        var tokenTenantId = Guid.NewGuid();

        // Act & Assert
        service.TenantId.Should().Be(Guid.Empty);
        service.IsAuthenticated.Should().BeFalse();

        using (scopedContext.BeginScope(tokenTenantId))
        {
            service.TenantId.Should().Be(tokenTenantId);
            service.IsAuthenticated.Should().BeFalse();
        }

        service.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Properties_WhenAuthStateProviderThrowsOutsideRazorScope_ShouldFailClosedWithEmptyTenant()
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns<Task<AuthenticationState>>(_ => throw new InvalidOperationException("Outside Razor scope"));

        var service = new CurrentUserService(httpContextAccessor, authStateProvider);

        // Act & Assert
        service.IsAuthenticated.Should().BeFalse();
        service.UserId.Should().BeNull();
        service.TenantId.Should().Be(Guid.Empty);
    }
}

