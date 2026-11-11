using Bunit;
using WebApp.Blazor.Components.DesignSystem.AppQrCode;

namespace WebApp.Blazor.UnitTests.DesignSystem;

public class AppQrCodeTests : BunitContext
{
    [Fact]
    public void Render_WhenPayloadValid_ShouldRenderInlineSvgAndCaption()
    {
        // Act
        var cut = Render<AppQrCode>(parameters => parameters
            .Add(p => p.Value, "https://app.virada5x2.com.br/ebook?utm_source=ebook")
            .Add(p => p.AriaLabel, "QR Code Ebook")
            .Add(p => p.Caption, "Escaneie para acessar"));

        // Assert
        var svg = cut.Find("svg");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().Be("QR Code Ebook");
        cut.Find(".app-qr__caption").TextContent.Should().Be("Escaneie para acessar");
        cut.FindAll("[role='alert']").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenPayloadEmpty_ShouldDisplayValidationAlert()
    {
        // Act
        var cut = Render<AppQrCode>(parameters => parameters
            .Add(p => p.Value, " "));

        // Assert
        cut.Find("[role='alert']").TextContent.Should().Contain("obrigatório");
        cut.FindAll("svg").Should().BeEmpty();
    }
}

