using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Gtm;
using Domain.Common;

namespace Application.UseCases.Gtm;

/// <summary>
/// Caso de uso que gera o kit oficial de links UTM e QR Codes SVG do Ebook a partir da URL pública configurada.
/// </summary>
public sealed class GetEbookGtmKitUseCase
{
    private sealed record CanonicalTouchpointSpec(
        string TouchpointName,
        string UtmSource,
        string UtmMedium,
        string UtmCampaign,
        string UtmContent);

    private static readonly CanonicalTouchpointSpec[] CanonicalTouchpoints =
    [
        new("Capa / Prefácio", "ebook", "qrcode", "transicao_5x2", "prefacio_qr"),
        new("Cap. 2: Mapa de Demanda", "ebook", "qrcode", "transicao_5x2", "cap2_demanda"),
        new("Cap. 4: Simulador 6x1 vs 5x2", "ebook", "qrcode", "transicao_5x2", "cap4_simulador"),
        new("Cap. 6: Botão PDF Interativo", "ebook", "pdf_link", "transicao_5x2", "cap6_cta_pdf"),
        new("Contracapa / Checklist Final", "ebook", "qrcode", "transicao_5x2", "contracapa_qr")
    ];

    private readonly IResultTelemetry? _telemetry;

    public GetEbookGtmKitUseCase(IResultTelemetry? telemetry = null)
    {
        _telemetry = telemetry;
    }

    public Result<EbookGtmKitDto> Execute(GetEbookGtmKitQuery query)
    {
        return ApplicationTelemetry.Observe(
            "GetEbookGtmKit",
            null,
            _telemetry,
            () =>
            {
                if (query is null ||
                    !Uri.TryCreate(query.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
                    (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                {
                    return Result.Failure<EbookGtmKitDto>(
                        Error.Validation("Gtm.InvalidBaseUrl", "A URL base pública deve ser um endereço HTTP/HTTPS absoluto válido."));
                }

                var normalizedBaseUrl = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
                var items = new List<EbookGtmTouchpointDto>(CanonicalTouchpoints.Length);

                foreach (var spec in CanonicalTouchpoints)
                {
                    var linkResult = GtmConversionLinkService.BuildEbookConversionLink(
                        new BuildEbookConversionLinkCommand(
                            normalizedBaseUrl,
                            spec.UtmSource,
                            spec.UtmMedium,
                            spec.UtmCampaign,
                            spec.UtmContent));

                    if (linkResult.IsFailure)
                    {
                        return Result.Failure<EbookGtmKitDto>(linkResult.Error);
                    }

                    var link = linkResult.Value;
                    items.Add(new EbookGtmTouchpointDto(
                        spec.TouchpointName,
                        link.UtmSource,
                        link.UtmMedium,
                        link.UtmCampaign,
                        link.UtmContent!,
                        link.LandingUrl,
                        link.QrCodeSvg,
                        $"virada5x2-{spec.UtmContent}.svg"));
                }

                return Result.Success(new EbookGtmKitDto(normalizedBaseUrl, items));
            });
    }
}
