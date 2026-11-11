using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class GetSharedRosterWorkspaceUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeRosterShareLinkService _shareLinkService = new();
    private readonly FakeScopedTenantContext _scopedTenantContext = new();
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();

    private GetSharedRosterWorkspaceUseCase CreateUseCase() =>
        new(
            _shareLinkService,
            _scopedTenantContext,
            _companyRepository,
            _employeeRepository,
            _rosterRepository,
            _swapRepository);

    [Fact]
    public async Task ExecuteAsync_WhenTokenIsInvalid_ShouldFailWithoutOpeningTenantScope()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetSharedRosterWorkspaceCommand("invalid-token"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterShare.InvalidToken");
        _scopedTenantContext.EnteredTenants.Should().BeEmpty();
        _scopedTenantContext.ScopedTenantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterIsArchived_ShouldReturnConflictFailure()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Teste",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        roster.Archive().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        const string token = "valid-token-archived";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetSharedRosterWorkspaceCommand(token));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterShare.ArchivedRoster");
        _scopedTenantContext.ScopedTenantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidToken_ShouldReturnWeeklyShiftsExactDaysOffAndSwapRequests()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Bistrô 5x2",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            closedDays: [DayOfWeek.Monday]).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana", StationRole.Kitchen).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        // Ana trabalha Terça..Sábado (folgas: Segunda [fechada] e Domingo)
        for (var offset = 1; offset <= 5; offset++)
        {
            roster.AssignShift(ana, WeekStart.AddDays(offset), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen).IsSuccess.Should().BeTrue();
        }
        var brunoShift = roster.AssignShift(bruno, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var anaFirstShift = roster.Shifts.First(s => s.EmployeeId == ana.Id);
        var swap = ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id,
            ana.Id, anaFirstShift.Id, bruno.Id, brunoShift.Id,
            reason: "Compromisso médico").Value;
        _swapRepository.Seed(swap);

        const string token = "valid-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(7)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetSharedRosterWorkspaceCommand(token, ana.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyName.Should().Be("Bistrô 5x2");
        result.Value.SelectedEmployeeId.Should().Be(ana.Id);
        result.Value.Employees.Should().HaveCount(2);

        var anaSummary = result.Value.Employees.Single(e => e.EmployeeId == ana.Id);
        anaSummary.WorkedDaysCount.Should().Be(5);
        anaSummary.DaysOffCount.Should().Be(2);
        anaSummary.DaysOff.Select(d => d.DayOfWeek).Should().Equal(DayOfWeek.Monday, DayOfWeek.Sunday);
        anaSummary.DaysOff.Single(d => d.DayOfWeek == DayOfWeek.Monday).IsCompanyClosedDay.Should().BeTrue();
        anaSummary.DaysOff.Single(d => d.DayOfWeek == DayOfWeek.Sunday).IsCompanyClosedDay.Should().BeFalse();

        result.Value.SwapRequests.Should().ContainSingle(s => s.Id == swap.Id && s.RequesterEmployeeName == "Ana");
        _scopedTenantContext.EnteredTenants.Should().ContainSingle().Which.Should().Be(tenantId);
        _scopedTenantContext.ScopedTenantId.Should().BeNull();
    }
}

