using Domain.Common;
using Infrastructure.Data.Identity;

namespace Infrastructure.Data.UnitTests.Identity;

public class ApplicationUserTests
{
    [Fact]
    public void Create_WhenParametersAreValid_ShouldReturnSuccessWithInitializedProperties()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createdAtUtc = new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

        // Act
        Result<ApplicationUser> result = ApplicationUser.Create(
            tenantId,
            "  gerente@bistro5x2.com.br  ",
            "  Mariana Silva  ",
            createdAtUtc);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Email.Should().Be("gerente@bistro5x2.com.br");
        result.Value.UserName.Should().Be("gerente@bistro5x2.com.br");
        result.Value.FullName.Should().Be("Mariana Silva");
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAtUtc.Should().Be(createdAtUtc);
    }

    [Fact]
    public void Create_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Act
        var result = ApplicationUser.Create(Guid.Empty, "gerente@bistro5x2.com.br", "Mariana Silva");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.User.InvalidTenantId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("email-invalido")]
    public void Create_WhenEmailIsInvalid_ShouldReturnValidationFailure(string email)
    {
        // Act
        var result = ApplicationUser.Create(Guid.NewGuid(), email, "Mariana Silva");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.User.InvalidEmail");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenFullNameIsEmptyOrWhitespace_ShouldReturnValidationFailure(string fullName)
    {
        // Act
        var result = ApplicationUser.Create(Guid.NewGuid(), "gerente@bistro5x2.com.br", fullName);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.User.InvalidFullName");
    }

    [Fact]
    public void Create_WhenFullNameExceedsMaxLength_ShouldReturnValidationFailure()
    {
        // Arrange
        var oversizedName = new string('A', ApplicationUser.MaxFullNameLength + 1);

        // Act
        var result = ApplicationUser.Create(Guid.NewGuid(), "gerente@bistro5x2.com.br", oversizedName);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Identity.User.FullNameTooLong");
    }
}

