using System.Diagnostics;
using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Enums;
using Xunit.Abstractions;

namespace Application.UnitTests.UseCases.Rosters;

public class RosterEngineStressTests
{
    private readonly ITestOutputHelper _output;

    public RosterEngineStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(10, ScheduleModel.RotatingDaysOff, 120, 1400)]
    [InlineData(25, ScheduleModel.RotatingDaysOff, 200, 3500)]
    [InlineData(50, ScheduleModel.RotatingDaysOff, 300, 6500)]
    [InlineData(50, ScheduleModel.FixedClosure, 300, 6500)]
    public async Task ExecuteAsync_UnderSolvableComplexConstraints_ShouldMeetCltComplianceCoverageAndBudgets(
        int employeeCount,
        ScheduleModel scheduleModel,
        int maxAverageMillisecondsBudget,
        double maxAllocatedKilobytesBudget)
    {
        // Arrange
        var targetWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildSolvableComplexScenario(
            employeeCount,
            scheduleModel,
            targetWeekStart);

        // Warmup JIT fora da medição
        _ = await RunGenerationOnceAsync(scenario);

        const int measuredIterations = 3;
        var latenciesMs = new List<double>(measuredIterations);
        var allocationsKb = new List<double>(measuredIterations);
        GeneratedRosterResultDto? firstRun = null;

        // Act
        for (var i = 0; i < measuredIterations; i++)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();

            var result = await RunGenerationOnceAsync(scenario);

            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var allocatedKb = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / 1024.0;

            result.IsSuccess.Should().BeTrue($"falha ao gerar escala para {employeeCount} colaboradores");
            result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue("restrições duras CLT jamais podem ser violadas");
            result.Value.ComplianceReport.Alerts.Should().NotContain(a => a.Category == RosterAlertCategory.CltHardConstraint);
            result.Value.ComplianceReport.PeakCoveragePercentage.Should().BeGreaterThanOrEqualTo(80m);
            result.Value.Shifts.Should().NotBeEmpty();

            AssertSundayCapsAndUnavailableDaysRespected(scenario, result.Value);

            latenciesMs.Add(elapsedMs);
            allocationsKb.Add(allocatedKb);
            _output.WriteLine(
                $"[Solvable/{scheduleModel}] Team={employeeCount} Iter={i + 1}: {elapsedMs:F2} ms | Alloc={allocatedKb:F1} KB | Shifts={result.Value.Shifts.Count} | PeakCoverage={result.Value.ComplianceReport.PeakCoveragePercentage:F1}%");

            if (firstRun is null)
            {
                firstRun = result.Value;
            }
            else
            {
                result.Value.Shifts.Count.Should().Be(firstRun.Shifts.Count);
                result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(firstRun.ComplianceReport.PeakCoveragePercentage);
                result.Value.Shifts
                    .Select(s => (s.EmployeeId, s.Date, s.StartTime, s.AssignedRole))
                    .Should()
                    .Equal(firstRun.Shifts.Select(s => (s.EmployeeId, s.Date, s.StartTime, s.AssignedRole)));
            }
        }

        // Assert
        var averageMs = latenciesMs.Average();
        var averageAllocKb = allocationsKb.Average();
        _output.WriteLine(
            $"[Solvable/{scheduleModel}] Team={employeeCount} Avg={averageMs:F2} ms (Budget <= {maxAverageMillisecondsBudget} ms) | AvgAlloc={averageAllocKb:F1} KB (Budget <= {maxAllocatedKilobytesBudget:F0} KB)");

        averageMs.Should().BeLessThanOrEqualTo(maxAverageMillisecondsBudget);
        averageAllocKb.Should().BeLessThanOrEqualTo(maxAllocatedKilobytesBudget);
    }

    [Fact]
    public async Task ExecuteAsync_UnderThreeShiftOvernightAndBoundaryConstraints_ShouldRespect11hRestAndSundayStreak()
    {
        // Arrange
        var targetWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildThreeShiftExtendedScenario(
            employeeCount: 50,
            targetWeekStartDate: targetWeekStart);

        _ = await RunGenerationOnceAsync(scenario);

        const int measuredIterations = 3;
        var latenciesMs = new List<double>(measuredIterations);
        var allocationsKb = new List<double>(measuredIterations);

        // Act
        for (var i = 0; i < measuredIterations; i++)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();
            var result = await RunGenerationOnceAsync(scenario);
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var allocatedKb = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / 1024.0;

            result.IsSuccess.Should().BeTrue();
            result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
            result.Value.ComplianceReport.Alerts.Should().NotContain(a => a.Category == RosterAlertCategory.CltHardConstraint);
            AssertSundayCapsAndUnavailableDaysRespected(scenario, result.Value);

            latenciesMs.Add(elapsedMs);
            allocationsKb.Add(allocatedKb);
        }

        var averageMs = latenciesMs.Average();
        var averageAllocKb = allocationsKb.Average();
        _output.WriteLine(
            $"[3Shifts-24h] Team=50 Avg={averageMs:F2} ms (Budget <= 350 ms) | AvgAlloc={averageAllocKb:F1} KB (Budget <= 11000 KB)");

        // Assert
        averageMs.Should().BeLessThanOrEqualTo(350);
        averageAllocKb.Should().BeLessThanOrEqualTo(11000);
    }

    [Fact]
    public async Task ExecuteAsync_AcrossFourConsecutiveWeeksWith50Employees_ShouldMaintainSundayRotationAndBudgets()
    {
        // Arrange
        var firstWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildSolvableComplexScenario(
            employeeCount: 50,
            ScheduleModel.RotatingDaysOff,
            firstWeekStart);

        var companyRepo = new FakeCompanyRepository();
        companyRepo.Seed(scenario.Company);
        var employeeRepo = new FakeEmployeeRepository();
        employeeRepo.Seed(scenario.Employees.ToArray());
        var demandRepo = new FakeDemandSlotRepository();
        demandRepo.Seed(scenario.DemandSlots.ToArray());
        var rosterRepo = new FakeRosterRepository();
        rosterRepo.Seed(scenario.AdjacentRosters.Where(r => r.WeekStartDate < firstWeekStart).ToArray());

        var currentUser = new FakeCurrentUserService
        {
            TenantId = scenario.TenantId,
            IsAuthenticated = true
        };

        var useCase = new GenerateRoster5x2UseCase(
            companyRepo,
            employeeRepo,
            demandRepo,
            rosterRepo,
            currentUser,
            new FakeUnitOfWork());

        // Act
        var startedAt = Stopwatch.GetTimestamp();
        var sundayWorkByEmployee = new Dictionary<Guid, List<bool>>();

        for (var week = 0; week < 4; week++)
        {
            var weekStart = firstWeekStart.AddDays(week * 7);
            var result = await useCase.ExecuteAsync(new GenerateRoster5x2Command(
                weekStart,
                scenario.Company.Id,
                MaxConsecutiveSundaysWorked: 2,
                ReplaceExistingDraft: true,
                ShiftTemplates: scenario.Templates,
                StaffingRules: scenario.StaffingRules));

            result.IsSuccess.Should().BeTrue();
            result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
            result.Value.ComplianceReport.Alerts.Should().NotContain(a => a.Category == RosterAlertCategory.CltHardConstraint);

            foreach (var employee in scenario.Employees)
            {
                if (!sundayWorkByEmployee.TryGetValue(employee.Id, out var history))
                {
                    history = new List<bool>(4);
                    sundayWorkByEmployee[employee.Id] = history;
                }

                history.Add(result.Value.Shifts.Any(s =>
                    s.EmployeeId == employee.Id &&
                    s.Date.DayOfWeek == DayOfWeek.Sunday));
            }
        }

        var totalElapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        _output.WriteLine($"[4-Week-Chain] Team=50 Total={totalElapsedMs:F2} ms (Budget <= 1200 ms)");

        // Assert: nunca 3 domingos consecutivos nas 4 semanas geradas
        foreach (var (_, history) in sundayWorkByEmployee)
        {
            for (var i = 2; i < history.Count; i++)
            {
                (history[i - 2] && history[i - 1] && history[i]).Should().BeFalse(
                    "a política dominical limita no máximo 2 domingos consecutivos trabalhados");
            }
        }

        totalElapsedMs.Should().BeLessThanOrEqualTo(1200);
    }

    [Theory]
    [InlineData(50, ScheduleModel.RotatingDaysOff, 400, 8500)]
    [InlineData(50, ScheduleModel.FixedClosure, 400, 8500)]
    public async Task ExecuteAsync_UnderOversubscribedConflictScenario_ShouldDegradeGracefullyWithoutCltViolations(
        int employeeCount,
        ScheduleModel scheduleModel,
        int maxAverageMillisecondsBudget,
        double maxAllocatedKilobytesBudget)
    {
        // Arrange
        var targetWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildOversubscribedConflictScenario(
            employeeCount,
            scheduleModel,
            targetWeekStart);

        _ = await RunGenerationOnceAsync(scenario);

        const int measuredIterations = 3;
        var latenciesMs = new List<double>(measuredIterations);
        var allocationsKb = new List<double>(measuredIterations);

        // Act
        for (var i = 0; i < measuredIterations; i++)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();

            var result = await RunGenerationOnceAsync(scenario);

            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var allocatedKb = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / 1024.0;

            result.IsSuccess.Should().BeTrue();
            result.Value.ComplianceReport.IsCltCompliant.Should().BeTrue();
            result.Value.ComplianceReport.Alerts.Should().NotContain(a => a.Category == RosterAlertCategory.CltHardConstraint);
            result.Value.ComplianceReport.Alerts.Should().Contain(a => a.Category == RosterAlertCategory.PeakCoverage);

            latenciesMs.Add(elapsedMs);
            allocationsKb.Add(allocatedKb);
        }

        var averageMs = latenciesMs.Average();
        var averageAllocKb = allocationsKb.Average();
        _output.WriteLine(
            $"[Oversubscribed/{scheduleModel}] Team={employeeCount} Avg={averageMs:F2} ms (Budget <= {maxAverageMillisecondsBudget} ms) | AvgAlloc={averageAllocKb:F1} KB (Budget <= {maxAllocatedKilobytesBudget:F0} KB)");

        // Assert
        averageMs.Should().BeLessThanOrEqualTo(maxAverageMillisecondsBudget);
        averageAllocKb.Should().BeLessThanOrEqualTo(maxAllocatedKilobytesBudget);
    }

    [Fact]
    public async Task ValidateAndSwapSearch_With50Employees_ShouldCompleteWithinLatencyAndAllocationBudgets()
    {
        // Arrange
        var targetWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildSolvableComplexScenario(
            employeeCount: 50,
            ScheduleModel.RotatingDaysOff,
            targetWeekStart);

        var companyRepo = new FakeCompanyRepository();
        companyRepo.Seed(scenario.Company);
        var employeeRepo = new FakeEmployeeRepository();
        employeeRepo.Seed(scenario.Employees.ToArray());
        var demandRepo = new FakeDemandSlotRepository();
        demandRepo.Seed(scenario.DemandSlots.ToArray());
        var rosterRepo = new FakeRosterRepository();
        rosterRepo.Seed(scenario.AdjacentRosters.ToArray());

        var currentUser = new FakeCurrentUserService
        {
            TenantId = scenario.TenantId,
            IsAuthenticated = true
        };

        var generateUseCase = new GenerateRoster5x2UseCase(
            companyRepo,
            employeeRepo,
            demandRepo,
            rosterRepo,
            currentUser,
            new FakeUnitOfWork());

        var generated = await generateUseCase.ExecuteAsync(new GenerateRoster5x2Command(
            scenario.TargetWeekStartDate,
            scenario.Company.Id,
            MaxConsecutiveSundaysWorked: 2,
            ReplaceExistingDraft: true,
            ShiftTemplates: scenario.Templates,
            StaffingRules: scenario.StaffingRules));

        generated.IsSuccess.Should().BeTrue();

        var validateUseCase = new ValidateRosterComplianceUseCase(
            rosterRepo,
            companyRepo,
            employeeRepo,
            demandRepo,
            currentUser);

        var shareLinkService = new FakeRosterShareLinkService();
        var link = shareLinkService.CreateShareLink(
            scenario.TenantId,
            scenario.Company.Id,
            generated.Value.RosterId,
            scenario.TargetWeekStartDate).Value;

        var scopedTenant = new FakeScopedTenantContext();
        var swapUseCase = new GetCompatibleSwapOptionsUseCase(
            shareLinkService,
            scopedTenant,
            companyRepo,
            employeeRepo,
            demandRepo,
            rosterRepo,
            new FakeShiftSwapRequestRepository(),
            new FakeScopedAwareCurrentUserService(scopedTenant));

        var sampleShift = generated.Value.Shifts[0];

        // Warmup
        _ = await validateUseCase.ExecuteAsync(new ValidateRosterComplianceCommand(
            generated.Value.RosterId,
            scenario.Templates,
            scenario.StaffingRules));
        _ = await swapUseCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            link.Token,
            sampleShift.EmployeeId,
            sampleShift.ShiftId));

        // Act
        var valStart = Stopwatch.GetTimestamp();
        var validationResult = await validateUseCase.ExecuteAsync(new ValidateRosterComplianceCommand(
            generated.Value.RosterId,
            scenario.Templates,
            scenario.StaffingRules));
        var valMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

        var swapStart = Stopwatch.GetTimestamp();
        var swapOptionsResult = await swapUseCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            link.Token,
            sampleShift.EmployeeId,
            sampleShift.ShiftId));
        var swapMs = Stopwatch.GetElapsedTime(swapStart).TotalMilliseconds;

        _output.WriteLine($"[Validate+Swap/50] Validate={valMs:F2} ms | SwapSearch={swapMs:F2} ms (Options={swapOptionsResult.Value.Count})");

        // Assert
        validationResult.IsSuccess.Should().BeTrue();
        validationResult.Value.IsCltCompliant.Should().BeTrue();
        swapOptionsResult.IsSuccess.Should().BeTrue();
        swapOptionsResult.Value.Should().NotBeEmpty();

        valMs.Should().BeLessThanOrEqualTo(300);
        swapMs.Should().BeLessThanOrEqualTo(400);
    }

    private static void AssertSundayCapsAndUnavailableDaysRespected(
        RosterStressScenario scenario,
        GeneratedRosterResultDto generated)
    {
        var employeeById = scenario.Employees.ToDictionary(e => e.Id);

        foreach (var shift in generated.Shifts)
        {
            var employee = employeeById[shift.EmployeeId];
            employee.UnavailableDays.Should().NotContain(
                shift.Date.DayOfWeek,
                $"colaborador {employee.Name} está indisponível em {shift.Date.DayOfWeek}");

            if (shift.Date.DayOfWeek == DayOfWeek.Sunday)
            {
                scenario.SundayCappedEmployeeIds.Should().NotContain(
                    shift.EmployeeId,
                    $"colaborador {employee.Name} já atingiu o teto de 2 domingos consecutivos");
            }
        }
    }

    private static async Task<Result<GeneratedRosterResultDto>> RunGenerationOnceAsync(
        RosterStressScenario scenario)
    {
        var companyRepo = new FakeCompanyRepository();
        companyRepo.Seed(scenario.Company);

        var employeeRepo = new FakeEmployeeRepository();
        employeeRepo.Seed(scenario.Employees.ToArray());

        var demandRepo = new FakeDemandSlotRepository();
        demandRepo.Seed(scenario.DemandSlots.ToArray());

        var rosterRepo = new FakeRosterRepository();
        rosterRepo.Seed(scenario.AdjacentRosters.ToArray());

        var currentUser = new FakeCurrentUserService
        {
            TenantId = scenario.TenantId,
            IsAuthenticated = true
        };

        var useCase = new GenerateRoster5x2UseCase(
            companyRepo,
            employeeRepo,
            demandRepo,
            rosterRepo,
            currentUser,
            new FakeUnitOfWork());

        return await useCase.ExecuteAsync(new GenerateRoster5x2Command(
            scenario.TargetWeekStartDate,
            scenario.Company.Id,
            MaxConsecutiveSundaysWorked: 2,
            ReplaceExistingDraft: true,
            ShiftTemplates: scenario.Templates,
            StaffingRules: scenario.StaffingRules));
    }
}
