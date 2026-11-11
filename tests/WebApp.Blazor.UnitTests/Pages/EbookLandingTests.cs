using Application.Common.Interfaces;
using Bunit;
using Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Gtm;

namespace WebApp.Blazor.UnitTests.Pages;

public class EbookLandingTests : BunitContext
{
    [Fact]
    public void Render_WhenOpenedWithUtmQuery_ShouldRecordConversionAndRenderQrCodeAndCta()
    {
        // Arrange
        var telemetry = new SpyTelemetry();
        Services.AddSingleton<IResultTelemetry>(telemetry);

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=cap_final&utm_content=rodape");

        // Act
        var cut = Render<EbookLanding>();

        // Assert
        var registerCta = cut.Find("[data-testid='ebook-register-cta']");
        registerCta.GetAttribute("href").Should().Be(
            "/Account/Register?utm_source=ebook&utm_medium=qrcode&utm_campaign=cap_final&utm_content=rodape");
        cut.Find("[data-testid='app-qr-code'] svg").Should().NotBeNull();
        telemetry.Events.Should().ContainSingle(e =>
            e.EventName == "ebook_landing_view" &&
            e.UtmSource == "ebook" &&
            e.UtmMedium == "qrcode" &&
            e.UtmCampaign == "cap_final");
    }

    private sealed class SpyTelemetry : IResultTelemetry
    {
        public List<(string EventName, string? UtmSource, string? UtmMedium, string? UtmCampaign)> Events { get; } = [];

        public void RecordUseCaseOutcome(string useCaseName, Result result, long elapsedMilliseconds, Guid? tenantId = null, string? errorCategory = null)
        {
        }

        public void RecordConversionEvent(string eventName, string? utmSource = null, string? utmMedium = null, string? utmCampaign = null)
        {
            Events.Add((eventName, utmSource, utmMedium, utmCampaign));
        }
    }
}

