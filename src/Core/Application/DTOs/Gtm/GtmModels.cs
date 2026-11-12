namespace Application.DTOs.Gtm;

public sealed record BuildEbookConversionLinkCommand(
    string BaseUrl,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string? UtmContent = null);

public sealed record GtmConversionLinkDto(
    string LandingUrl,
    string QrCodeSvg,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string? UtmContent);

public sealed record RegisterTenantOwnerCommand(
    string FullName,
    string Email,
    string Password,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? UtmContent = null);

public sealed record TenantRegistrationDto(
    Guid UserId,
    Guid TenantId,
    string Email,
    string FullName);

public sealed record GetEbookGtmKitQuery(
    string BaseUrl);

public sealed record EbookGtmTouchpointDto(
    string TouchpointName,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string UtmContent,
    string LandingUrl,
    string QrCodeSvg,
    string SuggestedFileName);

public sealed record EbookGtmKitDto(
    string BaseUrl,
    IReadOnlyList<EbookGtmTouchpointDto> Touchpoints);

