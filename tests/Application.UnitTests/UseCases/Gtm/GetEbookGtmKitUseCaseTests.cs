using Application.DTOs.Gtm;
using Application.UseCases.Gtm;
using Application.UnitTests.Fakes;

namespace Application.UnitTests.UseCases.Gtm;

public class GetEbookGtmKitUseCaseTests
{
    [Fact]
    public void Execute_WhenBaseUrlValid_ShouldGenerateAllFiveCanonicalEbookTouchpoints()
    {
        // Arrange
        var telemetry = new FakeResultTelemetry();
        var useCase = new GetEbookGtmKitUseCase(telemetry);

        // Act
        var result = useCase.Execute(new GetEbookGtmKitQuery("https://app.virada5x2.com.br/"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BaseUrl.Should().Be("https://app.virada5x2.com.br");
        result.Value.Touchpoints.Should().HaveCount(5);
        result.Value.Touchpoints.Select(t => t.UtmContent).Should().ContainInOrder(
            "prefacio_qr",
            "cap2_demanda",
            "cap4_simulador",
            "cap6_cta_pdf",
            "contracapa_qr");

        foreach (var item in result.Value.Touchpoints)
        {
            item.LandingUrl.Should().StartWith("https://app.virada5x2.com.br/ebook?");
            item.LandingUrl.Should().Contain($"utm_content={item.UtmContent}");
            item.QrCodeSvg.Should().StartWith("<svg");
            item.QrCodeSvg.Should().Contain("currentColor");
            item.SuggestedFileName.Should().EndWith(".svg");
        }

        telemetry.RecordedOutcomes.Should().ContainSingle(o =>
            o.UseCaseName == "GetEbookGtmKit" && o.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://app.virada5x2.com.br")]
    [InlineData("not-a-valid-url")]
    public void Execute_WhenBaseUrlInvalid_ShouldReturnValidationFailure(string invalidBaseUrl)
    {
        // Arrange
        var telemetry = new FakeResultTelemetry();
        var useCase = new GetEbookGtmKitUseCase(telemetry);

        // Act
        var result = useCase.Execute(new GetEbookGtmKitQuery(invalidBaseUrl));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Gtm.InvalidBaseUrl");
        telemetry.RecordedOutcomes.Should().ContainSingle(o =>
            o.UseCaseName == "GetEbookGtmKit" &&
            !o.IsSuccess &&
            o.ErrorCode == "Gtm.InvalidBaseUrl");
    }
}

