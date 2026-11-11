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
    [InlineData(10, ScheduleModel.RotatingDaysOff, 500)]
    [InlineData(25, ScheduleModel.RotatingDaysOff, 1000)]
    [InlineData(50, ScheduleModel.RotatingDaysOff, 2500)]
    [InlineData(50, ScheduleModel.FixedClosure, 2500)]
    public async Task ExecuteAsync_UnderComplexConstraints_ShouldMeetCltComplianceAndLatencyBudget(
        int employeeCount,
        ScheduleModel scheduleModel,
        int maxAverageMillisecondsBudget)
    {
        // Arrange
        var targetWeekStart = new DateOnly(2026, 11, 16);
        var scenario = RosterEngineStressProfileBuilder.BuildComplexScenario(
            employeeCount,
            scheduleModel,
            targetWeekStart);

        // Warmup JIT fora da medição principal
        _ = await RunGenerationOnceAsync(scenario);

        const int measuredIterations = 3;
        var latenciesMs = new List<double>(measuredIterations);
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
            result.Value.Shifts.Should().NotBeEmpty();

            latenciesMs.Add(elapsedMs);
            _output.WriteLine(
                $"[{scheduleModel}] Team={employeeCount} Iter={i + 1}: {elapsedMs:F2} ms | Alloc={allocatedKb:F1} KB | Shifts={result.Value.Shifts.Count} | PeakCoverage={result.Value.ComplianceReport.PeakCoveragePercentage:F1}%");

            if (firstRun is null)
            {
                firstRun = result.Value;
            }
            else
            {
                result.Value.Shifts.Count.Should().Be(firstRun.Shifts.Count);
                result.Value.ComplianceReport.PeakCoveragePercentage.Should().Be(firstRun.ComplianceReport.PeakCoveragePercentage);
            }
        }

        // Assert
        var averageMs = latenciesMs.Average();
        _output.WriteLine($"[{scheduleModel}] Team={employeeCount} Average={averageMs:F2} ms (Budget <= {maxAverageMillisecondsBudget} ms)");
        averageMs.Should().BeLessThanOrEqualTo(maxAverageMillisecondsBudget);
    }

    private static async Task<Domain.Common.Result<GeneratedRosterResultDto>> RunGenerationOnceAsync(
        RosterStressScenario scenario)
    {
        var companyRepo = new FakeCompanyRepository();
        companyRepo.Seed(scenario.Company);

        var employeeRepo = new FakeEmployeeRepository();
        employeeRepo.Seed(scenario.Employees.ToArray());

        var demandRepo = new FakeDemandSlotRepository();
        demandRepo.Seed(scenario.DemandSlots.ToArray());

        var rosterRepo = new FakeRosterRepository();
        rosterRepo.Seed(scenario.PreviousWeekRoster);

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
