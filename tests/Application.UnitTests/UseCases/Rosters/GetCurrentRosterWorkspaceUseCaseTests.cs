using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class GetCurrentRosterWorkspaceUseCaseTests
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16); // Segunda-feira

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetCurrentRosterWorkspaceUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _currentUserService);

    private Company SeedCompany(ScheduleModel model = ScheduleModel.RotatingDaysOff, params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Horizonte",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            model,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole primaryRole,
        bool active = true,
        params SecondarySkill[] skills)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            primaryRole,
            skills,
            [DayOfWeek.Monday],
            [DayOfWeek.Tuesday]).Value;

        if (!active)
        {
            employee.Deactivate();
        }

        _employeeRepository.Seed(employee);
        return employee;
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterWorkspace.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyNotConfigured_ShouldReturnNotFound()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoRosterExistsForSelectedWeek_ShouldReturnEmptyRosterWithActiveEmployeesAndTemplates()
    {
        // Arrange
        var company = SeedCompany(ScheduleModel.FixedClosure, DayOfWeek.Monday);
        SeedEmployee(company.Id, "Ana", StationRole.Kitchen, active: true,
            SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value);
        SeedEmployee(company.Id, "Inativo", StationRole.Waiter, active: false);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentRosterWorkspaceCommand(DefaultWeekStart));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.CompanyName.Should().Be("Restaurante Horizonte");
        result.Value.SelectedWeekStartDate.Should().Be(DefaultWeekStart);
        result.Value.SelectedWeekEndDate.Should().Be(DefaultWeekStart.AddDays(6));
        result.Value.ClosedDays.Should().Equal(DayOfWeek.Monday);
        result.Value.ActiveRoster.Should().BeNull();
        result.Value.ActiveEmployees.Should().ContainSingle(e => e.Name == "Ana");
        result.Value.ActiveEmployees.Single().SecondarySkills.Should().Contain(s => s.Role == StationRole.Waiter);
        result.Value.AvailableShiftTemplates.Should().HaveCount(2); // 12h aberta => 2 templates inferidos
    }

    [Fact]
    public async Task ExecuteAsync_WhenWeekHasRosterAndNearbyRosters_ShouldReturnActiveRosterReportAndNearbySummaries()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(company.Id, "Ana", StationRole.Kitchen);
        var bruno = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(ana, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var nextWeekStart = DefaultWeekStart.AddDays(7);
        var nextRoster = Roster.Create(_currentUserService.TenantId, company.Id, nextWeekStart).Value;
        nextRoster.AssignShift(bruno, nextWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        nextRoster.Publish();
        _rosterRepository.Seed(nextRoster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentRosterWorkspaceCommand(DefaultWeekStart));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveRoster.Should().NotBeNull();
        result.Value.ActiveRoster!.RosterId.Should().Be(roster.Id);
        result.Value.ActiveRoster.Shifts.Should().ContainSingle();
        result.Value.ActiveRoster.ComplianceReport.IsCltCompliant.Should().BeTrue();
        result.Value.NearbyRosters.Should().HaveCount(2);
        result.Value.NearbyRosters.Should().Contain(r => r.RosterId == nextRoster.Id && r.Status == RosterStatus.Published);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProvidedDateIsMidWeek_ShouldNormalizeToMondayOfThatWeek()
    {
        // Arrange
        SeedCompany();
        var wednesday = new DateOnly(2026, 11, 18); // Quarta-feira da semana de 16/11/2026
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentRosterWorkspaceCommand(wednesday));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SelectedWeekStartDate.Should().Be(DefaultWeekStart);
        result.Value.SelectedWeekEndDate.Should().Be(DefaultWeekStart.AddDays(6));
    }
}
