using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class ApproveShiftSwapUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private ApproveShiftSwapUseCase CreateUseCase() =>
        new(
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService,
            _unitOfWork);

    [Fact]
    public async Task ExecuteAsync_WhenMutualSwapPendingManagerApprovalAndCltValid_ShouldSwapShiftsAndApproveAtomically()
    {
        // Arrange
        var company = SeedCompany();
        var waiter = SeedEmployee(company.Id, "João", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bartender = SeedEmployee(company.Id, "Marina", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var waiterShift = roster.AssignShift(waiter, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var barShift = roster.AssignShift(bartender, WeekStart.AddDays(2), new TimeOnly(16, 0), new TimeOnly(0, 0), StationRole.Bar).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            waiter.Id, waiterShift.Id, bartender.Id, barShift.Id,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ApproveShiftSwapCommand(swap.Id, "Autorizado pelo gestor"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.Approved);
        result.Value.SwapRequest.ReviewedByUserId.Should().Be(_currentUserService.UserId);
        result.Value.SwapRequest.DecisionNotes.Should().Be("Autorizado pelo gestor");
        result.Value.ValidationSummary.IsCltCompliant.Should().BeTrue();

        waiterShift.EmployeeId.Should().Be(bartender.Id);
        waiterShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);
        barShift.EmployeeId.Should().Be(waiter.Id);
        barShift.CoverageOrigin.Should().Be(CoverageOrigin.SecondarySkill);

        _rosterRepository.UpdatedRosters.Should().Contain(roster);
        _swapRepository.UpdatedRequests.Should().Contain(swap);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneWayCoverValid_ShouldReassignShiftAndApprove()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var shiftA = roster.AssignShift(empA, WeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            empA.Id, shiftA.Id, empB.Id, targetShiftId: null,
            requirePeerApproval: false).Value;
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ApproveShiftSwapCommand(swap.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        shiftA.EmployeeId.Should().Be(empB.Id);
        swap.Status.Should().Be(ShiftSwapStatus.Approved);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterChangedAfterRequestCausingInterShiftViolation_ShouldFailWithoutMutating()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var shiftA = roster.AssignShift(empA, WeekStart.AddDays(1), new TimeOnly(10, 0), new TimeOnly(18, 0), StationRole.Waiter).Value;
        var shiftB = roster.AssignShift(empB, WeekStart.AddDays(4), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        var swap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            empA.Id, shiftA.Id, empB.Id, shiftB.Id,
            requirePeerApproval: false).Value;

        // Later, empB receives a Monday overnight shift ending Tuesday 02:00 (only 8h before shiftA at 10:00)
        roster.AssignShift(empB, WeekStart, new TimeOnly(18, 0), new TimeOnly(2, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();

        _rosterRepository.Seed(roster);
        _swapRepository.Seed(swap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ApproveShiftSwapCommand(swap.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Roster.InsufficientInterShiftRest");
        swap.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        shiftA.EmployeeId.Should().Be(empA.Id);
        shiftB.EmployeeId.Should().Be(empB.Id);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenShiftAlreadySwappedToAnotherEmployee_ShouldFailWithOwnershipMismatch()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);
        var empC = SeedEmployee(company.Id, "Carla", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sA = roster.AssignShift(empA, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sB = roster.AssignShift(empB, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sC = roster.AssignShift(empC, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        var staleSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: false).Value;

        // Meanwhile sA was already swapped with sC
        roster.SwapShiftEmployees(sA.Id, empC, sC.Id, empA).IsSuccess.Should().BeTrue();

        _rosterRepository.Seed(roster);
        _swapRepository.Seed(staleSwap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ApproveShiftSwapCommand(staleSwap.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ShiftSwap.ShiftOwnershipMismatch");
        staleSwap.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStillPendingPeerApproval_ShouldFailWithoutMutating()
    {
        // Arrange
        var company = SeedCompany();
        var empA = SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        var empB = SeedEmployee(company.Id, "Bruno", StationRole.Waiter);

        var roster = Roster.Create(_currentUserService.TenantId, company.Id, WeekStart).Value;
        var sA = roster.AssignShift(empA, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var sB = roster.AssignShift(empB, WeekStart.AddDays(1), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var pendingPeerSwap = ShiftSwapRequest.Create(
            _currentUserService.TenantId, company.Id, roster.Id,
            empA.Id, sA.Id, empB.Id, sB.Id,
            requirePeerApproval: true).Value;
        _swapRepository.Seed(pendingPeerSwap);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ApproveShiftSwapCommand(pendingPeerSwap.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ShiftSwapRequest.NotPendingManagerApproval");
        sA.EmployeeId.Should().Be(empA.Id);
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Casa Aprovação",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole role,
        IEnumerable<SecondarySkill>? skills = null)
    {
        var employee = Employee.Create(_currentUserService.TenantId, companyId, name, role, skills).Value;
        _employeeRepository.Seed(employee);
        return employee;
    }
}

