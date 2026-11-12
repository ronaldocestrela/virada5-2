using Bunit;
using FluentAssertions;
using Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages;

namespace WebApp.Blazor.UnitTests.Pages;

public class HomeTests : BunitContext
{
    [Fact]
    public void Render_WhenNotAuthenticated_ShouldDisplayLandingPageHeroAndActions()
    {
        // Arrange
        var authContext = this.AddAuthorization();
        authContext.SetNotAuthorized();

        // Act
        var cut = Render<Home>();

        // Assert
        cut.Find("[data-testid=\"landing-page\"]").Should().NotBeNull();
        cut.Find(".landing-hero").Should().NotBeNull();
        cut.Find(".landing-hero__title").TextContent.Should().Contain("Passe da escala 6x1 para a 5x2");
        cut.Find(".landing-paths-grid").Should().NotBeNull();
        cut.Find(".landing-benchmarks-grid").Should().NotBeNull();
        cut.Find(".landing-tiers-grid").Should().NotBeNull();
    }

    [Fact]
    public void Render_WhenAuthenticated_ShouldRedirectToDashboard()
    {
        // Arrange
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("gestor@casa.com.br");
        authContext.SetRoles(ApplicationRoles.Manager);

        var navMan = Services.GetRequiredService<NavigationManager>();

        // Act
        var cut = Render<Home>();

        // Assert
        navMan.Uri.Should().EndWith("/dashboard");
    }
}
