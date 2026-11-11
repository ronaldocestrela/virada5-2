using Application.DTOs.Gtm;
using Application.UseCases.Gtm;

namespace Application.UnitTests.UseCases.Gtm;

public class GtmConversionLinkServiceTests
{
    [Fact]
    public void BuildEbookConversionLink_WhenValidInput_ShouldGenerateEncodedUrlAndSvgQrCode()
    {
        // Arrange
        var command = new BuildEbookConversionLinkCommand(
            BaseUrl: "https://app.virada5x2.com.br",
            UtmSource: "ebook",
            UtmMedium: "qrcode",
            UtmCampaign: "transicao_5x2_cap3",
            UtmContent: "cta_diagnostico");

        // Act
        var result = GtmConversionLinkService.BuildEbookConversionLink(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.LandingUrl.Should().Be(
            "https://app.virada5x2.com.br/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=transicao_5x2_cap3&utm_content=cta_diagnostico");
        result.Value.QrCodeSvg.Should().StartWith("<svg");
        result.Value.QrCodeSvg.Should().Contain("currentColor");
        result.Value.QrCodeSvg.Should().NotContain("#");
    }

    [Theory]
    [InlineData("http://", "Gtm.InvalidBaseUrl")]
    [InlineData("javascript:alert(1)", "Gtm.InvalidBaseUrl")]
    [InlineData("not-a-url", "Gtm.InvalidBaseUrl")]
    public void BuildEbookConversionLink_WhenBaseUrlInvalid_ShouldReturnValidationFailure(
        string invalidBaseUrl,
        string expectedCode)
    {
        // Arrange
        var command = new BuildEbookConversionLinkCommand(
            BaseUrl: invalidBaseUrl,
            UtmSource: "ebook",
            UtmMedium: "pdf",
            UtmCampaign: "lancamento");

        // Act
        var result = GtmConversionLinkService.BuildEbookConversionLink(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void BuildEbookConversionLink_WhenUtmCampaignMissing_ShouldReturnValidationFailure()
    {
        // Arrange
        var command = new BuildEbookConversionLinkCommand(
            BaseUrl: "https://app.virada5x2.com.br",
            UtmSource: "ebook",
            UtmMedium: "qrcode",
            UtmCampaign: "   ");

        // Act
        var result = GtmConversionLinkService.BuildEbookConversionLink(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Gtm.EmptyCampaign");
    }

    [Fact]
    public void BuildRegisterRedirectQuery_WhenGivenUtmParameters_ShouldSanitizeAndPreserveSafeValues()
    {
        // Act
        var query = GtmConversionLinkService.BuildRegisterQueryString("ebook", "qrcode", "cap_1", "botao_topo");

        // Assert
        query.Should().Be("?utm_source=ebook&utm_medium=qrcode&utm_campaign=cap_1&utm_content=botao_topo");
    }
}

