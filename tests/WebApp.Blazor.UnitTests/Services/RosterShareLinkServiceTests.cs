using Microsoft.AspNetCore.DataProtection;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Services;

public class RosterShareLinkServiceTests
{
    [Fact]
    public void CreateAndValidateToken_WhenValidRoster_ShouldRoundTripPayload()
    {
        // Arrange
        var provider = DataProtectionProvider.Create("Virada5x2.Tests");
        var service = new RosterShareLinkService(provider);
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var rosterId = Guid.NewGuid();
        var weekStart = new DateOnly(2026, 11, 16);

        // Act
        var created = service.CreateShareLink(tenantId, companyId, rosterId, weekStart, TimeSpan.FromDays(7));

        // Assert
        created.IsSuccess.Should().BeTrue();
        created.Value.ShareUrl.Should().StartWith("/shared/roster?token=");
        created.Value.Token.Should().NotBeNullOrWhiteSpace();

        var validated = service.ValidateToken(created.Value.Token);
        validated.IsSuccess.Should().BeTrue();
        validated.Value.TenantId.Should().Be(tenantId);
        validated.Value.CompanyId.Should().Be(companyId);
        validated.Value.RosterId.Should().Be(rosterId);
        validated.Value.WeekStartDate.Should().Be(weekStart);
    }

    [Fact]
    public void ValidateToken_WhenTamperedOrEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        var provider = DataProtectionProvider.Create("Virada5x2.Tests");
        var service = new RosterShareLinkService(provider);

        // Act
        var emptyResult = service.ValidateToken(string.Empty);
        var invalidResult = service.ValidateToken("invalid-token-value");

        // Assert
        emptyResult.IsFailure.Should().BeTrue();
        invalidResult.IsFailure.Should().BeTrue();
        invalidResult.Error.Code.Should().Be("RosterShare.InvalidToken");
    }
}

