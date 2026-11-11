using System.Text;
using Application.DTOs.Gtm;
using Domain.Common;

namespace Application.UseCases.Gtm;

/// <summary>
/// Serviço puro de geração e sanitização de links de conversão UTM e QR Codes para o Ebook GTM.
/// </summary>
public static class GtmConversionLinkService
{
    public static Result<GtmConversionLinkDto> BuildEbookConversionLink(BuildEbookConversionLinkCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Uri.TryCreate(command.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure<GtmConversionLinkDto>(
                Error.Validation("Gtm.InvalidBaseUrl", "A URL base pública deve ser um endereço HTTP/HTTPS absoluto válido."));
        }

        if (string.IsNullOrWhiteSpace(command.UtmSource))
        {
            return Result.Failure<GtmConversionLinkDto>(
                Error.Validation("Gtm.EmptySource", "O parâmetro utm_source é obrigatório."));
        }

        if (string.IsNullOrWhiteSpace(command.UtmMedium))
        {
            return Result.Failure<GtmConversionLinkDto>(
                Error.Validation("Gtm.EmptyMedium", "O parâmetro utm_medium é obrigatório."));
        }

        if (string.IsNullOrWhiteSpace(command.UtmCampaign))
        {
            return Result.Failure<GtmConversionLinkDto>(
                Error.Validation("Gtm.EmptyCampaign", "O parâmetro utm_campaign é obrigatório."));
        }

        var source = SanitizeUtmValue(command.UtmSource)!;
        var medium = SanitizeUtmValue(command.UtmMedium)!;
        var campaign = SanitizeUtmValue(command.UtmCampaign)!;
        var content = SanitizeUtmValue(command.UtmContent);

        var query = BuildRegisterQueryString(source, medium, campaign, content);
        var normalizedBase = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var landingUrl = $"{normalizedBase}/ebook{query}";

        var qrResult = QrCodeSvgGenerator.GenerateSvg(
            landingUrl,
            $"QR Code de conversão Virada 5x2 ({campaign})");

        if (qrResult.IsFailure)
        {
            return Result.Failure<GtmConversionLinkDto>(qrResult.Error);
        }

        return Result.Success(new GtmConversionLinkDto(
            landingUrl,
            qrResult.Value,
            source,
            medium,
            campaign,
            content));
    }

    public static string BuildRegisterQueryString(
        string? utmSource,
        string? utmMedium,
        string? utmCampaign,
        string? utmContent = null)
    {
        var pairs = new List<(string Key, string Value)>(4);

        if (SanitizeUtmValue(utmSource) is { } s)
        {
            pairs.Add(("utm_source", s));
        }

        if (SanitizeUtmValue(utmMedium) is { } m)
        {
            pairs.Add(("utm_medium", m));
        }

        if (SanitizeUtmValue(utmCampaign) is { } c)
        {
            pairs.Add(("utm_campaign", c));
        }

        if (SanitizeUtmValue(utmContent) is { } ct)
        {
            pairs.Add(("utm_content", ct));
        }

        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder("?");
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('&');
            }

            sb.Append(pairs[i].Key)
              .Append('=')
              .Append(Uri.EscapeDataString(pairs[i].Value));
        }

        return sb.ToString();
    }

    public static string? SanitizeUtmValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            trimmed = trimmed[..64];
        }

        return trimmed;
    }
}

