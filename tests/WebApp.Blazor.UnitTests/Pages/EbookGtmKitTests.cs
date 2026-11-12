using Application.Common.Interfaces;
using Application.UseCases.Gtm;
using Bunit;
using Infrastructure.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Gtm;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class EbookGtmKitTests : BunitContext
{
    public EbookGtmKitTests()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("dono@casa.com.br");
        authContext.SetRoles(ApplicationRoles.Owner);

        Services.AddScoped<GetEbookGtmKitUseCase>();
        Services.AddScoped<IToastService, ToastService>();
    }

    [Fact]
    public void Render_WhenAuthorized_ShouldDisplayAllFiveCanonicalEbookQrCards()
    {
        // Act
        var cut = Render<EbookGtmKit>();

        // Assert
        cut.Find("[data-testid='ebook-kit-card-prefacio_qr'] svg").Should().NotBeNull();
        cut.Find("[data-testid='ebook-kit-card-cap2_demanda'] svg").Should().NotBeNull();
        cut.Find("[data-testid='ebook-kit-card-cap4_simulador'] svg").Should().NotBeNull();
        cut.Find("[data-testid='ebook-kit-card-cap6_cta_pdf'] svg").Should().NotBeNull();
        cut.Find("[data-testid='ebook-kit-card-contracapa_qr'] svg").Should().NotBeNull();
    }

    [Fact]
    public void RegenerateKit_WhenBaseUrlInvalid_ShouldDisplayValidationAlert()
    {
        // Arrange
        var cut = Render<EbookGtmKit>();
        var input = cut.Find("#gtm-base-url");

        // Act
        input.Change("invalid-url");
        cut.Find("button").Click();

        // Assert
        cut.Find("[role='alert']").TextContent.Should().Contain("HTTP/HTTPS");
    }
}

