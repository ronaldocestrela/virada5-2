using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class RejectShiftSwapUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RejectShiftSwapUseCase CreateUseCase() =>
        new(
            _swapRepository,
            _rosterRepository,
            _employeeRepository,
            _currentUserService,
            _unitOfWork);

    [Fact]
    public async Task ExecuteAsync_WhenManagerRejectsPendingRequest_ShouldTransitionToRejected()
    {
        // Arrange
        var (swap, _, _, _) = SeedSwap(requirePeerApproval: false);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RejectShiftSwapCommand(
            swap.Id,
            Notes: "Horário crítico sem cobertura"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ShiftSwapStatus.Rejected);
        result.Value.ReviewedByUserId.Should().Be(_currentUserService.UserId);
        result.Value.DecisionNotes.Should().Be("Horário crítico sem cobertura");
        _swapRepository.UpdatedRequests.Should().Contain(swap);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequesterCancelsPendingRequest_ShouldTransitionToCancelled()
    {
        // Arrange
        var (swap, empA, _, _) = SeedSwap(requirePeerApproval: true);
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RejectShiftSwapCommand(
            swap.Id,
            Notes: "Imprevisto resolvido",
            ActingEmployeeId: empA.Id,
            IsCancellationByRequester: true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ShiftSwapStatus.Cancelled);
        result.Value.DecisionNotes.Should().Be("Imprevisto resolvido");
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnauthorizedCancellationOrAlreadyFinalized_ShouldFail()
    {
        // Arrange
        var (swap, _, empB, _) = SeedSwap(requirePeerApproval: true);
        var useCase = CreateUseCase();

        // Act
        var wrongActor = await useCase.ExecuteAsync(new RejectShiftSwapCommand(
            swap.Id,
            ActingEmployeeId: empB.Id,
            IsCancellationByRequester: true));

        swap.RejectByManager(_currentUserService.UserId, "Encerrada").IsSuccess.Should().BeTrue();
        var alreadyFinalized = await useCase.ExecuteAsync(new RejectShiftSwapCommand(swap.Id));

        // Assert
        wrongActor.IsFailure.Should().BeTrue();
        wrongActor.Error.Code.Should().Be("ShiftSwapRequest.UnauthorizedCancellation");
        alreadyFinalized.IsFailure.Should().BeTrue();
        alreadyFinalized.Error.Code.Should().Be("ShiftSwapRequest.AlreadyFinalized");
    }

    private (ShiftSwapRequest Swap, Employee EmpA, Employee EmpB, Roster Roster) SeedSwap(bool requirePeerApproval)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Reject",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var empA = Employee.Create(_currentUserService.TenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var empB = Employee.Create(_currentUserService.TenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(empA, empB);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sA = roster.AssignShift(empA, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sB = roster.AssignShift(empB, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: requirePeerApproval).Value;
        _swapRepository.Seed(swap);

        return (swap, empA, empB, roster);
    }
}

