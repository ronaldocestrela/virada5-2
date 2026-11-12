using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class ValidateRosterComplianceUseCaseTests
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16); // Segunda-feira

    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private ValidateRosterComplianceUseCase CreateUseCase() =>
        new(
            _rosterRepository,
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _currentUserService);

    private Company SeedCompany(ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff, params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bar Compliance",
            BusinessType.Bar,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            scheduleModel,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(Guid companyId, string name, StationRole role, bool isActive = true)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            role).Value;

        if (!isActive)
        {
            employee.Deactivate();
        }

        _employeeRepository.Seed(employee);
        return employee;
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsNull_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterCompliance.InvalidCommand");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(Guid.NewGuid()));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterCompliance.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(Guid.Empty));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterCompliance.InvalidRosterId");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(Guid.NewGuid()));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterViolatesBoundaryInterShiftRestAndSundayRotation_ShouldReportNonCompliantAlerts()
    {
        // Arrange
        var company = SeedCompany();
        var employee = SeedEmployee(company.Id, "Carlos", StationRole.Waiter);

        // Semana anterior terminou domingo às 23:00
        var prevRoster = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            DefaultWeekStart.AddDays(-7),
            SundayRotationPolicy.BiweeklyCycle).Value;
        prevRoster.AssignShift(
            employee,
            new DateOnly(2026, 11, 15),
            new TimeOnly(15, 0),
            new TimeOnly(23, 0),
            StationRole.Waiter);
        _rosterRepository.Seed(prevRoster);

        // Escala atual foi criada isoladamente sem contexto de fronteira, alocando segunda às 08:00 (9h < 11h) e domingo consecutivo
        var currentRoster = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            DefaultWeekStart,
            SundayRotationPolicy.BiweeklyCycle).Value;
        currentRoster.AssignShift(employee, DefaultWeekStart, new TimeOnly(8, 0), new TimeOnly(16, 0), StationRole.Waiter);
        currentRoster.AssignShift(employee, DefaultWeekStart.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        _rosterRepository.Seed(currentRoster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(currentRoster.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsCltCompliant.Should().BeFalse();
        result.Value.Alerts.Should().Contain(a =>
            a.Category == RosterAlertCategory.CltHardConstraint &&
            a.Severity == RosterAlertSeverity.Error &&
            a.EmployeeId == employee.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterIsValidAndCoversAllPeaks_ShouldReturnCompliantReportWithoutMutatingRepository()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "Julia", StationRole.Waiter);

        var slotPeak = DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Saturday, 12, 100m).Value;
        _demandSlotRepository.Seed(slotPeak);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        roster.AssignShift(waiter, DefaultWeekStart.AddDays(5), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();
        var command = new ValidateRosterComplianceCommand(
            roster.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 1, ModerateHeadcount: 0, LowOrIdleHeadcount: 0)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsCltCompliant.Should().BeTrue();
        result.Value.PeakCoveragePercentage.Should().Be(100m);
        result.Value.TotalShifts.Should().Be(1);
        _rosterRepository.UpdatedRosters.Should().BeEmpty();
        _rosterRepository.AddedRosters.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmployeeDeactivatedOrShiftOnClosedDay_ShouldReportErrors()
    {
        // Arrange
        var company = SeedCompany(ScheduleModel.FixedClosure, DayOfWeek.Monday);
        var waiter = SeedEmployee(company.Id, "Amanda", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        // Segunda-feira é fechamento fixo; depois Amanda é desativada
        roster.AssignShift(waiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        waiter.Deactivate();
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(roster.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsCltCompliant.Should().BeFalse();
        result.Value.Alerts.Should().Contain(a => a.Code == "Roster.ShiftOnClosedDay" && a.Severity == RosterAlertSeverity.Error);
        result.Value.Alerts.Should().Contain(a => a.Code == "Employee.Inactive" && a.Severity == RosterAlertSeverity.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTemplatesOrStaffingRulesAreInvalid_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var invalidTemplateResult = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(
            Guid.NewGuid(),
            ShiftTemplates: [new ShiftTemplateInputDto("", new TimeOnly(11, 0), new TimeOnly(19, 0))]));

        var invalidRuleResult = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(
            Guid.NewGuid(),
            StaffingRules: [new StationStaffingRuleDto(StationRole.Waiter, -1, 0, 0)]));

        // Assert
        invalidTemplateResult.IsFailure.Should().BeTrue();
        invalidTemplateResult.Error.Code.Should().Be("RosterCompliance.InvalidShiftTemplate");
        invalidRuleResult.IsFailure.Should().BeTrue();
        invalidRuleResult.Error.Code.Should().Be("RosterCompliance.InvalidStaffingRule");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCoverageOrPreferenceAlertsOccur_ShouldFormatRolesAndDaysInPortuguese()
    {
        // Arrange
        var company = SeedCompany(ScheduleModel.RotatingDaysOff);
        var cook = Employee.Create(
            _currentUserService.TenantId,
            company.Id,
            "João da Silva",
            StationRole.Kitchen,
            preferredDaysOff: [DayOfWeek.Sunday]).Value;
        _employeeRepository.Seed(cook);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        var sunday = DefaultWeekStart.AddDays(6);
        roster.AssignShift(cook, sunday, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Kitchen);
        _rosterRepository.Seed(roster);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ValidateRosterComplianceCommand(
            roster.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Abertura", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Kitchen, 1, 1, 1)]));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var stationAlert = result.Value.Alerts.First(a => a.Code == "Roster.UncoveredStationSlot");
        stationAlert.Message.Should().Contain("Cozinha");
        stationAlert.Message.Should().NotContain("Kitchen");

        var preferenceAlert = result.Value.Alerts.Single(a => a.Code == "Employee.PreferredDayOffOverridden");
        preferenceAlert.Message.Should().Contain("Domingo");
        preferenceAlert.Message.Should().NotContain("Sunday");
    }
}
