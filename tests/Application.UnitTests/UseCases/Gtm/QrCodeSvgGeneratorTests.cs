using System.Xml.Linq;
using Application.UseCases.Gtm;

namespace Application.UnitTests.UseCases.Gtm;

public class QrCodeSvgGeneratorTests
{
    [Fact]
    public void GenerateSvg_WhenValidShortUrl_ShouldReturnValidTokenFriendlySvgWithQuietZone()
    {
        // Act
        var result = QrCodeSvgGenerator.GenerateSvg("https://virada5x2.com/ebook", "QR Code Ebook");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var doc = XDocument.Parse(result.Value);
        doc.Root!.Name.LocalName.Should().Be("svg");
        doc.Root.Attribute("role")?.Value.Should().Be("img");
        doc.Root.Attribute("aria-label")?.Value.Should().Be("QR Code Ebook");
        doc.Root.Attribute("viewBox")?.Value.Should().NotBeNullOrWhiteSpace();
        result.Value.Should().Contain("fill=\"currentColor\"");
        result.Value.Should().NotContain("#");
    }

    [Fact]
    public void GenerateSvg_WhenLongEbookCampaignUrl_ShouldScaleToHigherQrVersionAndRemainValidXml()
    {
        // Arrange
        var url = "https://app.virada5x2.com.br/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=guia_completo_transicao_5x2&utm_content=capitulo_7";

        // Act
        var result = QrCodeSvgGenerator.GenerateSvg(url);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var doc = XDocument.Parse(result.Value);
        var viewBoxParts = doc.Root!.Attribute("viewBox")!.Value.Split(' ');
        int.Parse(viewBoxParts[2]).Should().BeGreaterThan(29); // Version >= 3 + quiet zone
    }

    [Fact]
    public void GenerateSvg_WhenPayloadEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = QrCodeSvgGenerator.GenerateSvg("  ");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("QrCode.EmptyPayload");
    }

    [Fact]
    public void GenerateSvg_WhenPayloadExceedsMaxCapacity_ShouldReturnValidationFailure()
    {
        // Arrange
        var oversizedPayload = new string('A', 250);

        // Act
        var result = QrCodeSvgGenerator.GenerateSvg(oversizedPayload);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("QrCode.PayloadTooLarge");
    }

    [Fact]
    public void GenerateMatrix_WhenGenerated_ShouldContainStandardThreeFinderPatternsAndBeDeterministic()
    {
        // Act
        var first = QrCodeSvgGenerator.GenerateMatrix("https://app.virada5x2.com.br/ebook");
        var second = QrCodeSvgGenerator.GenerateMatrix("https://app.virada5x2.com.br/ebook");

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        var matrix = first.Value;
        var size = matrix.GetLength(0);
        size.Should().Be(matrix.GetLength(1));

        // Top-left 7x7 finder outer ring corner checks
        matrix[0, 0].Should().BeTrue();
        matrix[0, 6].Should().BeTrue();
        matrix[6, 0].Should().BeTrue();
        matrix[6, 6].Should().BeTrue();
        matrix[3, 3].Should().BeTrue();

        // Top-right 7x7 finder center
        matrix[3, size - 4].Should().BeTrue();
        // Bottom-left 7x7 finder center
        matrix[size - 4, 3].Should().BeTrue();

        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                matrix[r, c].Should().Be(second.Value[r, c]);
            }
        }
    }
}

