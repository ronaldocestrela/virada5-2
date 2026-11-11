using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class GetPendingShiftSwapsUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetPendingShiftSwapsUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService);

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasManagerAndPeerPendingSwaps_ShouldSeparateCountsAndReportCltCompliance()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Gestor",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        var carla = Employee.Create(_currentUserService.TenantId, company.Id, "Carla", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno, carla);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sAna = roster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sBruno = roster.AssignShift(bruno, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sCarla = roster.AssignShift(carla, WeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var readyForManager = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, sBruno.Id,
            requirePeerApproval: false).Value;

        var waitingPeer = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            carla.Id, sCarla.Id, ana.Id, null,
            requirePeerApproval: true).Value;

        _swapRepository.Seed(readyForManager, waitingPeer);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PendingManagerApprovalCount.Should().Be(1);
        result.Value.PendingPeerApprovalCount.Should().Be(1);
        result.Value.Items.Should().HaveCount(2);

        var managerItem = result.Value.Items.Single(i => i.SwapRequest.Id == readyForManager.Id);
        managerItem.CanApproveNow.Should().BeTrue();
        managerItem.IsCltCompliant.Should().BeTrue();
        managerItem.ComplianceBlockMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDraftChangedCausingCltViolation_ShouldExposeNonCompliantWarningWithoutThrowing()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Gestor",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sAna = roster.AssignShift(ana, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            ana.Id, sAna.Id, bruno.Id, null,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        // Alteração posterior no rascunho cria conflito de interjornada para Bruno
        roster.AssignShift(bruno, WeekStart, new TimeOnly(17, 0), new TimeOnly(1, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetPendingShiftSwapsCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.CanApproveNow.Should().BeFalse();
        item.IsCltCompliant.Should().BeFalse();
        item.ComplianceBlockMessage.Should().NotBeNullOrWhiteSpace();
    }
}

