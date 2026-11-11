using Application.Common.Interfaces;
using Application.DTOs.Gtm;
using Application.UseCases.Gtm;
using Domain.Common;

namespace Application.UnitTests.UseCases.Gtm;

public class RegisterTenantOwnerUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenValidRequest_ShouldCreateTenantOwnerAndRecordGtmTelemetry()
    {
        // Arrange
        var registrationService = new FakeTenantRegistrationService();
        var telemetry = new FakeTelemetry();
        var useCase = new RegisterTenantOwnerUseCase(registrationService, telemetry);
        var command = new RegisterTenantOwnerCommand(
            FullName: "Carlos Dono",
            Email: "carlos@bistro5x2.com.br",
            Password: "SenhaForte123",
            UtmSource: "ebook",
            UtmMedium: "qrcode",
            UtmCampaign: "lancamento");

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().NotBe(Guid.Empty);
        result.Value.Email.Should().Be("carlos@bistro5x2.com.br");
        telemetry.Conversions.Should().ContainSingle(c =>
            c.EventName == "tenant_owner_registered" &&
            c.UtmSource == "ebook" &&
            c.UtmMedium == "qrcode" &&
            c.UtmCampaign == "lancamento");
        telemetry.Outcomes.Should().ContainSingle(o => o.UseCase == "RegisterTenantOwner" && o.IsSuccess);
    }

    [Theory]
    [InlineData("", "carlos@bistro.com", "SenhaForte123", "TenantRegistration.InvalidFullName")]
    [InlineData("Carlos", "invalido", "SenhaForte123", "TenantRegistration.InvalidEmail")]
    [InlineData("Carlos", "carlos@bistro.com", "123", "TenantRegistration.WeakPassword")]
    public async Task ExecuteAsync_WhenInputInvalid_ShouldReturnValidationFailureAndRecordTelemetry(
        string fullName,
        string email,
        string password,
        string expectedErrorCode)
    {
        // Arrange
        var registrationService = new FakeTenantRegistrationService();
        var telemetry = new FakeTelemetry();
        var useCase = new RegisterTenantOwnerUseCase(registrationService, telemetry);

        // Act
        var result = await useCase.ExecuteAsync(new RegisterTenantOwnerCommand(fullName, email, password));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedErrorCode);
        registrationService.Calls.Should().Be(0);
        telemetry.Outcomes.Should().ContainSingle(o =>
            o.UseCase == "RegisterTenantOwner" &&
            !o.IsSuccess &&
            o.ErrorCode == expectedErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailAlreadyExists_ShouldPropagateConflictFailure()
    {
        // Arrange
        var registrationService = new FakeTenantRegistrationService
        {
            NextResult = Result.Failure<TenantRegistrationDto>(
                Error.Conflict("TenantRegistration.DuplicateEmail", "Este e-mail já está cadastrado."))
        };
        var useCase = new RegisterTenantOwnerUseCase(registrationService);

        // Act
        var result = await useCase.ExecuteAsync(new RegisterTenantOwnerCommand(
            "Carlos Dono",
            "carlos@bistro5x2.com.br",
            "SenhaForte123"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TenantRegistration.DuplicateEmail");
    }

    private sealed class FakeTenantRegistrationService : ITenantRegistrationService
    {
        public int Calls { get; private set; }
        public Result<TenantRegistrationDto>? NextResult { get; set; }

        public Task<Result<TenantRegistrationDto>> RegisterOwnerAsync(
            string fullName,
            string email,
            string password,
            CancellationToken ct = default)
        {
            Calls++;
            if (NextResult is not null)
            {
                return Task.FromResult(NextResult);
            }

            return Task.FromResult(Result.Success(new TenantRegistrationDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                email,
                fullName)));
        }
    }

    private sealed class FakeTelemetry : IResultTelemetry
    {
        public List<(string UseCase, bool IsSuccess, string ErrorCode)> Outcomes { get; } = [];
        public List<(string EventName, string? UtmSource, string? UtmMedium, string? UtmCampaign)> Conversions { get; } = [];

        public void RecordUseCaseOutcome(
            string useCaseName,
            Result result,
            long elapsedMilliseconds,
            Guid? tenantId = null,
            string? errorCategory = null)
        {
            Outcomes.Add((useCaseName, result.IsSuccess, result.Error.Code));
        }

        public void RecordConversionEvent(
            string eventName,
            string? utmSource = null,
            string? utmMedium = null,
            string? utmCampaign = null)
        {
            Conversions.Add((eventName, utmSource, utmMedium, utmCampaign));
        }
    }
}

