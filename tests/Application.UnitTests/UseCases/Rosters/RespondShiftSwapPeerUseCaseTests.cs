using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class RespondShiftSwapPeerUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RespondShiftSwapPeerUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService,
            _unitOfWork);

    [Fact]
    public async Task ExecuteAsync_WhenTargetEmployeeAcceptsAndCltRemainsValid_ShouldAdvanceToPendingManagerApproval()
    {
        // Arrange
        var (company, empA, empB, roster, sA, sB) = SeedMutualSwapScenario();
        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id, empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: true).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RespondShiftSwapPeerCommand(swap.Id, empB.Id, Accept: true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        result.Value.SwapRequest.PeerRespondedAtUtc.Should().NotBeNull();
        result.Value.ValidationSummary.IsCltCompliant.Should().BeTrue();
        _swapRepository.UpdatedRequests.Should().Contain(swap);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetEmployeeDeclines_ShouldTransitionToRejected()
    {
        // Arrange
        var (company, empA, empB, roster, sA, sB) = SeedMutualSwapScenario();
        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id, empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: true).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RespondShiftSwapPeerCommand(
            swap.Id, empB.Id, Accept: false, Notes: "Não consigo cobrir esse dia"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.Rejected);
        result.Value.SwapRequest.DecisionNotes.Should().Be("Não consigo cobrir esse dia");
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetEmployeeAcceptsButCltBecameInvalid_ShouldFailAndKeepPendingPeerApproval()
    {
        // Arrange
        var (company, empA, empB, roster, sA, sB) = SeedMutualSwapScenario();
        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id, empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: true).Value;
        _swapRepository.Seed(swap);

        // Between request and peer acceptance, empB became unavailable on sA's day
        empB.UpdateAvailability(null, [sA.Date.DayOfWeek]).IsSuccess.Should().BeTrue();

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new RespondShiftSwapPeerCommand(swap.Id, empB.Id, Accept: true));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Employee.UnavailableOnDay");
        swap.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUnauthorizedPeerOrMissingRequest_ShouldFail()
    {
        // Arrange
        var (company, empA, empB, roster, sA, sB) = SeedMutualSwapScenario();
        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id, empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: true).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var unauthorized = await useCase.ExecuteAsync(new RespondShiftSwapPeerCommand(swap.Id, empA.Id, Accept: true));
        var notFound = await useCase.ExecuteAsync(new RespondShiftSwapPeerCommand(Guid.NewGuid(), empB.Id, Accept: true));

        // Assert
        unauthorized.Error.Code.Should().Be("ShiftSwapRequest.UnauthorizedPeer");
        notFound.Error.Code.Should().Be("ShiftSwap.NotFound");
    }

    private (Company Company, Employee EmpA, Employee EmpB, Roster Roster, Shift ShiftA, Shift ShiftB) SeedMutualSwapScenario()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Teste",
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
        var sB = roster.AssignShift(empB, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        return (company, empA, empB, roster, sA, sB);
    }
}

