using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class GenerateRoster5x2UseCaseTests
{
    private static readonly DateOnly DefaultWeekStart = new(2026, 11, 16); // Segunda-feira

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private GenerateRoster5x2UseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _currentUserService,
            _unitOfWork);

    private Company SeedCompany(
        TimeOnly? opening = null,
        TimeOnly? closing = null,
        ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff,
        params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Horizonte",
            BusinessType.Restaurant,
            opening ?? new TimeOnly(11, 0),
            closing ?? new TimeOnly(19, 0),
            scheduleModel,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole primaryRole,
        IEnumerable<SecondarySkill>? secondarySkills = null,
        IEnumerable<DayOfWeek>? preferredDaysOff = null,
        IEnumerable<DayOfWeek>? unavailableDays = null,
        bool isActive = true)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            primaryRole,
            secondarySkills,
            preferredDaysOff,
            unavailableDays).Value;

        if (!isActive)
        {
            employee.Deactivate();
        }

        _employeeRepository.Seed(employee);
        return employee;
    }

    private void SeedWeeklyDemandProfile(
        Guid companyId,
        TimeOnly open,
        TimeOnly close,
        Func<DayOfWeek, int, decimal> volumeSelector)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var isWithin = open < close
                    ? hour >= open.Hour && hour < close.Hour
                    : hour >= open.Hour || hour < close.Hour;

                if (!isWithin)
                {
                    continue;
                }

                var slot = DemandSlot.Create(
                    _currentUserService.TenantId,
                    companyId,
                    day,
                    hour,
                    volumeSelector(day, hour)).Value;

                _demandSlotRepository.Seed(slot);
            }
        }
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
        result.Error.Code.Should().Be("RosterGeneration.InvalidCommand");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(DefaultWeekStart));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenWeekStartDateIsDefault_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(default));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.InvalidWeekStartDate");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task ExecuteAsync_WhenSundayPolicyIsOutOfRange_ShouldReturnValidationFailure(int maxConsecutiveSundays)
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(
            DefaultWeekStart,
            MaxConsecutiveSundaysWorked: maxConsecutiveSundays));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SundayRotationPolicy.InvalidMaxConsecutiveSundays");
    }

    [Fact]
    public async Task ExecuteAsync_WhenShiftTemplateExceedsTwelveHours_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            ShiftTemplates:
            [
                new ShiftTemplateInputDto("Turno Longo", new TimeOnly(9, 0), new TimeOnly(22, 0))
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.InvalidShiftTemplate");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStaffingRuleHasNegativeHeadcount_ShouldReturnValidationFailure()
    {
        // Arrange
        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            StaffingRules:
            [
                new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: -1, ModerateHeadcount: 1, LowOrIdleHeadcount: 1)
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.InvalidStaffingRule");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(DefaultWeekStart, CompanyId: Guid.NewGuid()));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoActiveEmployeesExist_ShouldReturnValidationFailure()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Inativo", StationRole.Waiter, isActive: false);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(DefaultWeekStart, company.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.NoActiveEmployees");
    }

    [Fact]
    public async Task ExecuteAsync_WhenActiveDraftExistsAndReplaceIsFalse_ShouldReturnConflictFailure()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Garçom 1", StationRole.Waiter);
        var existing = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        _rosterRepository.Seed(existing);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(DefaultWeekStart, company.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.DraftAlreadyExists");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPublishedRosterExistsEvenWithReplaceTrue_ShouldReturnConflictFailure()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "Garçom 1", StationRole.Waiter);
        var published = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        published.AssignShift(waiter, DefaultWeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        published.Publish();
        _rosterRepository.Seed(published);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ReplaceExistingDraft: true));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterGeneration.PublishedRosterExists");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDraftExistsAndReplaceIsTrue_ShouldArchivePreviousDraftAndPersistNewDraft()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Garçom 1", StationRole.Waiter);
        SeedEmployee(company.Id, "Garçom 2", StationRole.Waiter);
        var existingDraft = Roster.Create(_currentUserService.TenantId, company.Id, DefaultWeekStart).Value;
        _rosterRepository.Seed(existingDraft);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ReplaceExistingDraft: true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RosterStatus.Draft);
        result.Value.ReplacedPreviousDraft.Should().BeTrue();
        existingDraft.Status.Should().Be(RosterStatus.Archived);
        _rosterRepository.UpdatedRosters.Should().Contain(existingDraft);
        _rosterRepository.AddedRosters.Should().ContainSingle(r => r.Id == result.Value.RosterId);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGenerating5x2Roster_ShouldEnforceCltMaxFiveWorkDaysAndMinimumTwoDaysOffPerEmployee()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        var waiters = new[]
        {
            SeedEmployee(company.Id, "Ana", StationRole.Waiter),
            SeedEmployee(company.Id, "Bruno", StationRole.Waiter),
            SeedEmployee(company.Id, "Carla", StationRole.Waiter)
        };

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) => day switch
        {
            DayOfWeek.Monday or DayOfWeek.Tuesday => 20m,
            DayOfWeek.Wednesday or DayOfWeek.Thursday => 60m,
            _ => 100m
        });

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 3, ModerateHeadcount: 2, LowOrIdleHeadcount: 1)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
        result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(100m);

        foreach (var waiter in waiters)
        {
            var summary = result.Value.ComplianceReport.EmployeeSummaries.Single(s => s.EmployeeId == waiter.Id);
            summary.WorkedDaysCount.Should().BeLessThanOrEqualTo(5);
            summary.DaysOffCount.Should().BeGreaterThanOrEqualTo(2);
        }

        var persisted = await _rosterRepository.GetByIdWithShiftsAsync(result.Value.RosterId);
        persisted.Should().NotBeNull();
        persisted!.Publish().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDemandHasValleysAndPeaks_ShouldAllocateDaysOffInValleysAndAchieveFullPeakCoverage()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        SeedEmployee(company.Id, "Cozinha 1", StationRole.Kitchen);
        SeedEmployee(company.Id, "Cozinha 2", StationRole.Kitchen);
        SeedEmployee(company.Id, "Cozinha 3", StationRole.Kitchen);

        // Segunda e Terça = vale (15); Quarta e Quinta = moderado (60); Sexta/Sábado/Domingo = pico (100)
        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) => day switch
        {
            DayOfWeek.Monday or DayOfWeek.Tuesday => 15m,
            DayOfWeek.Wednesday or DayOfWeek.Thursday => 60m,
            _ => 100m
        });

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Kitchen, PeakHeadcount: 3, ModerateHeadcount: 2, LowOrIdleHeadcount: 1)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(100m);
        result.Value.ComplianceReport.Alerts.Should().NotContain(a => a.Category == RosterAlertCategory.PeakCoverage);

        var peakDayShifts = result.Value.Shifts.Count(s =>
            s.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday);
        var valleyDayShifts = result.Value.Shifts.Count(s =>
            s.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday);

        peakDayShifts.Should().Be(9);
        valleyDayShifts.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPreviousAndNextWeeksExist_ShouldRespectElevenHourInterShiftRestAndSundayRotationPolicy()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(8, 0), new TimeOnly(22, 0));
        var restrictedSundayWaiter = SeedEmployee(company.Id, "Lucas", StationRole.Waiter);
        var backupWaiter = SeedEmployee(company.Id, "Marcos", StationRole.Waiter);

        // Lucas trabalhou o domingo imediatamente anterior (2026-11-15) até as 23:00
        var prevWeekStart = DefaultWeekStart.AddDays(-7); // 2026-11-09
        var prevRoster = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            prevWeekStart,
            SundayRotationPolicy.BiweeklyCycle).Value;
        prevRoster.AssignShift(
            restrictedSundayWaiter,
            new DateOnly(2026, 11, 15), // Domingo
            new TimeOnly(15, 0),
            new TimeOnly(23, 0),
            StationRole.Waiter);
        _rosterRepository.Seed(prevRoster);

        // Na semana seguinte (2026-11-23), Marcos inicia às 08:00 de segunda, bloqueando turno noturno de domingo até 23:00 para Marcos
        var nextWeekStart = DefaultWeekStart.AddDays(7);
        var nextRoster = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            nextWeekStart,
            SundayRotationPolicy.BiweeklyCycle).Value;
        nextRoster.AssignShift(
            backupWaiter,
            nextWeekStart,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            StationRole.Waiter);
        _rosterRepository.Seed(nextRoster);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(8, 0), new TimeOnly(22, 0), (day, hour) =>
            day == DayOfWeek.Monday && hour < 12 ? 90m : 50m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            MaxConsecutiveSundaysWorked: 1, // BiweeklyCycle: Lucas NÃO pode trabalhar domingo 2026-11-22
            ShiftTemplates:
            [
                new ShiftTemplateInputDto("Manhã", new TimeOnly(8, 0), new TimeOnly(16, 0)),
                new ShiftTemplateInputDto("Noite", new TimeOnly(14, 0), new TimeOnly(22, 0))
            ],
            StaffingRules:
            [
                new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 1, ModerateHeadcount: 1, LowOrIdleHeadcount: 1)
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();

        // Lucas não pode trabalhar segunda de manhã (08:00 < 23:00 + 11h = 10:00)
        result.Value.Shifts.Should().NotContain(s =>
            s.EmployeeId == restrictedSundayWaiter.Id &&
            s.Date == DefaultWeekStart &&
            s.StartTime == new TimeOnly(8, 0));

        // Lucas não pode trabalhar domingo 2026-11-22 (BiweeklyCycle, já trabalhou 2026-11-15)
        result.Value.Shifts.Should().NotContain(s =>
            s.EmployeeId == restrictedSundayWaiter.Id &&
            s.DayOfWeek == DayOfWeek.Sunday);

        // Marcos não pode pegar o turno de domingo que termina às 22:00 se segunda inicia às 08:00 (10h < 11h)
        result.Value.Shifts.Should().NotContain(s =>
            s.EmployeeId == backupWaiter.Id &&
            s.DayOfWeek == DayOfWeek.Sunday &&
            s.EndTime == new TimeOnly(22, 0));
    }

    [Fact]
    public async Task ExecuteAsync_WhenPrimaryRoleIsUnavailableOnModerateDay_ShouldUsePolyvalenceMatrixAndEmitInfoAlert()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        SeedEmployee(
            company.Id,
            "Barman Titular",
            StationRole.Bar,
            unavailableDays: [DayOfWeek.Wednesday, DayOfWeek.Thursday]);

        var polyvalentWaiter = SeedEmployee(
            company.Id,
            "Garçom Polivalente",
            StationRole.Waiter,
            secondarySkills: [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Support).Value]);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) =>
            day is DayOfWeek.Friday or DayOfWeek.Saturday ? 100m : 60m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules:
            [
                new StationStaffingRuleDto(StationRole.Bar, PeakHeadcount: 1, ModerateHeadcount: 1, LowOrIdleHeadcount: 1),
                new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 1, ModerateHeadcount: 0, LowOrIdleHeadcount: 0)
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().Contain(s =>
            s.EmployeeId == polyvalentWaiter.Id &&
            s.AssignedRole == StationRole.Bar &&
            s.CoverageOrigin == CoverageOrigin.SecondarySkill &&
            (s.DayOfWeek == DayOfWeek.Wednesday || s.DayOfWeek == DayOfWeek.Thursday));

        result.Value.ComplianceReport.SecondaryCoverageShifts.Should().BeGreaterThan(0);
        result.Value.ComplianceReport.Alerts.Should().Contain(a =>
            a.Category == RosterAlertCategory.PolyvalenceUsage &&
            a.Severity == RosterAlertSeverity.Info &&
            a.EmployeeId == polyvalentWaiter.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSupportProficiencyOnlyOnPeakDay_ShouldNotAssignSupportToPeakAndShouldEmitPeakUncoveredAlert()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        SeedEmployee(
            company.Id,
            "Garçom Apoio",
            StationRole.Waiter,
            secondarySkills: [SecondarySkill.Create(StationRole.Cashier, SkillProficiency.Support).Value]);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) =>
            day == DayOfWeek.Saturday ? 100m : 40m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules:
            [
                new StationStaffingRuleDto(StationRole.Cashier, PeakHeadcount: 1, ModerateHeadcount: 0, LowOrIdleHeadcount: 0)
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().BeEmpty();
        result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(0m);
        result.Value.ComplianceReport.Alerts.Should().Contain(a =>
            a.Category == RosterAlertCategory.PeakCoverage &&
            a.Severity == RosterAlertSeverity.Error &&
            a.Role == StationRole.Cashier);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTemplatesAndRulesAreOmitted_ShouldInferShiftsAndStaffingFromCompanyAndTeam()
    {
        // Arrange: Casa aberta 14h (10:00..24:00) com fechamento fixo segunda-feira e sem DemandSlots
        var company = SeedCompany(
            new TimeOnly(10, 0),
            new TimeOnly(23, 0), // 13h -> deve inferir 2 templates <= 12h
            ScheduleModel.FixedClosure,
            DayOfWeek.Monday);

        SeedEmployee(company.Id, "Cozinha", StationRole.Kitchen);
        SeedEmployee(company.Id, "Garçom 1", StationRole.Waiter);
        SeedEmployee(company.Id, "Garçom 2", StationRole.Waiter);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(DefaultWeekStart, company.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.UsedInferredShiftTemplates.Should().BeTrue();
        result.Value.UsedInferredStaffingRules.Should().BeTrue();
        result.Value.Shifts.Should().NotBeEmpty();
        result.Value.Shifts.Should().NotContain(s => s.DayOfWeek == DayOfWeek.Monday);
        result.Value.Shifts.Should().OnlyContain(s => (s.EndTime - s.StartTime) <= TimeSpan.FromHours(12));
        result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
        result.Value.ComplianceReport.Alerts.Should().Contain(a => a.Code == "Demand.NoSlotsFoundUsingDefault");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyOperatesOvernight_ShouldGenerateOvernightShiftsWithinCltRest()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(18, 0), new TimeOnly(2, 0));
        SeedEmployee(company.Id, "Bartender 1", StationRole.Bar);
        SeedEmployee(company.Id, "Bartender 2", StationRole.Bar);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(18, 0), new TimeOnly(2, 0), (day, _) =>
            day is DayOfWeek.Friday or DayOfWeek.Saturday ? 100m : 50m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            StaffingRules: [new StationStaffingRuleDto(StationRole.Bar, PeakHeadcount: 2, ModerateHeadcount: 1, LowOrIdleHeadcount: 1)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
        result.Value.Shifts.Should().OnlyContain(s =>
            s.StartTime == new TimeOnly(18, 0) &&
            s.EndTime == new TimeOnly(2, 0));
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmployeeHasPreferredDaysOff_ShouldHonorPreferenceWheneverFeasible()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        var pedro = SeedEmployee(
            company.Id,
            "Pedro",
            StationRole.Waiter,
            preferredDaysOff: [DayOfWeek.Wednesday]);
        SeedEmployee(company.Id, "Tiago", StationRole.Waiter);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) =>
            day is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday ? 100m : 50m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 2, ModerateHeadcount: 1, LowOrIdleHeadcount: 1)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Shifts.Should().NotContain(s => s.EmployeeId == pedro.Id && s.DayOfWeek == DayOfWeek.Wednesday);
        result.Value.ComplianceReport.PreferredDaysOffHonoredCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAutonomousSecondarySkillExistsForPeak_ShouldCoverPeakAndEmitPolyvalenceAlert()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        var multiRoleWaiter = SeedEmployee(
            company.Id,
            "Lucas Autônomo",
            StationRole.Waiter,
            secondarySkills: [SecondarySkill.Create(StationRole.Cashier, SkillProficiency.Autonomous).Value]);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) =>
            day == DayOfWeek.Saturday ? 100m : 40m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules:
            [
                new StationStaffingRuleDto(StationRole.Cashier, PeakHeadcount: 1, ModerateHeadcount: 0, LowOrIdleHeadcount: 0)
            ]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(100m);
        result.Value.Shifts.Should().ContainSingle(s =>
            s.EmployeeId == multiRoleWaiter.Id &&
            s.AssignedRole == StationRole.Cashier &&
            s.CoverageOrigin == CoverageOrigin.SecondarySkill &&
            s.DayOfWeek == DayOfWeek.Saturday);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGapExistsInHistoricalWeeks_ShouldResetSundayConsecutiveStreak()
    {
        // Arrange: trabalhou domingo há 2 semanas (2026-11-08), mas NÃO há escala na semana anterior (2026-11-09..15)
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(19, 0));
        var waiter = SeedEmployee(company.Id, "Rafael", StationRole.Waiter);

        var twoWeeksAgo = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            DefaultWeekStart.AddDays(-14),
            SundayRotationPolicy.BiweeklyCycle).Value;
        twoWeeksAgo.AssignShift(waiter, new DateOnly(2026, 11, 8), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter);
        _rosterRepository.Seed(twoWeeksAgo);

        SeedWeeklyDemandProfile(company.Id, new TimeOnly(11, 0), new TimeOnly(19, 0), (day, _) =>
            day == DayOfWeek.Sunday ? 100m : 40m);

        var useCase = CreateUseCase();
        var command = new GenerateRoster5x2Command(
            DefaultWeekStart,
            company.Id,
            MaxConsecutiveSundaysWorked: 1,
            ShiftTemplates: [new ShiftTemplateInputDto("Único", new TimeOnly(11, 0), new TimeOnly(19, 0))],
            StaffingRules: [new StationStaffingRuleDto(StationRole.Waiter, PeakHeadcount: 1, ModerateHeadcount: 0, LowOrIdleHeadcount: 0)]);

        // Act
        var result = await useCase.ExecuteAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(100m);
        result.Value.Shifts.Should().ContainSingle(s => s.EmployeeId == waiter.Id && s.DayOfWeek == DayOfWeek.Sunday);
    }
}
