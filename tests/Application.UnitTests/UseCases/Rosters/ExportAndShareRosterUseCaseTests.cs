using System.Text;
using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class ExportAndShareRosterUseCaseTests
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly StubRosterShareLinkService _shareLinkService = new();

    [Fact]
    public async Task ExportRosterCsv_WhenRosterExists_ShouldReturnUtf8BomCsvWithSanitizedValues()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var employee = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "=Ana;Silva",
            StationRole.Kitchen).Value;
        _employeeRepository.Seed(employee);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(employee, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var useCase = new ExportRosterCsvUseCase(
            _companyRepository,
            _employeeRepository,
            _rosterRepository,
            _currentUserService);

        // Act
        var result = await useCase.ExecuteAsync(new ExportRosterCsvCommand(roster.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ContentType.Should().Be("text/csv; charset=utf-8");
        result.Value.FileName.Should().Contain("escala-5x2-2026-11-16");

        var preamble = Encoding.UTF8.GetPreamble();
        result.Value.Content.Take(preamble.Length).Should().Equal(preamble);

        var csvText = Encoding.UTF8.GetString(result.Value.Content, preamble.Length, result.Value.Content.Length - preamble.Length);
        csvText.Should().Contain("Data;Dia;Colaborador;Praca;Inicio;Fim;DuracaoHoras;OrigemCobertura;Status");
        csvText.Should().Contain("\"'=Ana;Silva\""); // Protegido contra CSV injection e escapado com aspas
        csvText.Should().Contain("2026-11-16");
        csvText.Should().Contain("11:00;19:00;8,0");
    }

    [Fact]
    public async Task ExportRosterCsv_WhenRosterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var useCase = new ExportRosterCsvUseCase(
            _companyRepository,
            _employeeRepository,
            _rosterRepository,
            _currentUserService);

        // Act
        var result = await useCase.ExecuteAsync(new ExportRosterCsvCommand(Guid.NewGuid()));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.NotFound");
    }

    [Fact]
    public async Task GenerateRosterShareLink_WhenRosterExists_ShouldDelegateToShareLinkService()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var roster = Roster.Create(_currentUserService.TenantId, companyId, DefaultWeekStart).Value;
        _rosterRepository.Seed(roster);

        var useCase = new GenerateRosterShareLinkUseCase(
            _rosterRepository,
            _currentUserService,
            _shareLinkService);

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRosterShareLinkCommand(roster.Id, 7));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RosterId.Should().Be(roster.Id);
        result.Value.ShareUrl.Should().Contain("/shared/roster?token=");
        _shareLinkService.LastLifetime.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task GenerateRosterShareLink_WhenExpirationDaysInvalid_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = new GenerateRosterShareLinkUseCase(
            _rosterRepository,
            _currentUserService,
            _shareLinkService);

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRosterShareLinkCommand(Guid.NewGuid(), 0));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterShare.InvalidExpiration");
    }

    private sealed class StubRosterShareLinkService : IRosterShareLinkService
    {
        public TimeSpan? LastLifetime { get; private set; }

        public Result<RosterShareLinkDto> CreateShareLink(
            Guid tenantId,
            Guid companyId,
            Guid rosterId,
            DateOnly weekStartDate,
            TimeSpan? lifetime = null)
        {
            LastLifetime = lifetime;
            var token = $"token-{rosterId:N}";
            return Result.Success(new RosterShareLinkDto(
                rosterId,
                token,
                $"https://localhost/shared/roster?token={token}",
                DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(14))));
        }

        public Result<RosterShareTokenPayloadDto> ValidateToken(string token) =>
            Result.Failure<RosterShareTokenPayloadDto>(
                Error.Validation("RosterShare.InvalidToken", "Token inválido no stub."));
    }
}

