using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Rosters;

public class SharedShiftSwapActionsUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeRosterShareLinkService _shareLinkService = new();
    private readonly FakeScopedTenantContext _scopedTenantContext = new();
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeScopedAwareCurrentUserService _currentUserService;

    public SharedShiftSwapActionsUseCaseTests()
    {
        _currentUserService = new FakeScopedAwareCurrentUserService(_scopedTenantContext);
    }

    [Fact]
    public async Task SharedWorkflow_WhenRequestedAcceptedAndCancelledViaValidToken_ShouldEnforceTokenRosterBoundaryAndTransitions()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Bar Centro",
            BusinessType.Bar,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var s1 = roster.AssignShift(ana, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var s2 = roster.AssignShift(bruno, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        const string token = "shared-flow-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(7)));

        var coreRequest = new RequestShiftSwapUseCase(
            _companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _swapRepository, _currentUserService, _unitOfWork);
        var coreRespond = new RespondShiftSwapPeerUseCase(
            _companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _swapRepository, _currentUserService, _unitOfWork);
        var coreReject = new RejectShiftSwapUseCase(
            _swapRepository, _rosterRepository, _employeeRepository, _currentUserService, _unitOfWork);

        var requestUseCase = new RequestSharedShiftSwapUseCase(_shareLinkService, _scopedTenantContext, _rosterRepository, coreRequest);
        var respondUseCase = new RespondSharedShiftSwapPeerUseCase(_shareLinkService, _scopedTenantContext, _rosterRepository, _swapRepository, coreRespond);
        var cancelUseCase = new CancelSharedShiftSwapUseCase(_shareLinkService, _scopedTenantContext, _rosterRepository, _swapRepository, coreReject);

        // Act 1: Request via token
        var requestResult = await requestUseCase.ExecuteAsync(new RequestSharedShiftSwapCommand(
            token, ana.Id, s1.Id, bruno.Id, s2.Id, "Troca de consulta"));

        // Assert 1
        requestResult.IsSuccess.Should().BeTrue();
        requestResult.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);
        var swapId = requestResult.Value.SwapRequest.Id;

        // Act 2: Peer accepts via token
        var acceptResult = await respondUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
            token, swapId, bruno.Id, Accept: true));

        // Assert 2
        acceptResult.IsSuccess.Should().BeTrue();
        acceptResult.Value.SwapRequest.Status.Should().Be(ShiftSwapStatus.PendingManagerApproval);

        // Act 3: Requester cancels via token before manager approval
        var cancelResult = await cancelUseCase.ExecuteAsync(new CancelSharedShiftSwapCommand(
            token, swapId, ana.Id, "Não precisa mais"));

        // Assert 3
        cancelResult.IsSuccess.Should().BeTrue();
        cancelResult.Value.Status.Should().Be(ShiftSwapStatus.Cancelled);
        _scopedTenantContext.ScopedTenantId.Should().BeNull();
    }

    [Fact]
    public async Task RespondPeer_WhenSwapBelongsToAnotherRosterThanToken_ShouldFailWithNotFound()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Bar Centro",
            BusinessType.Bar,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var ana = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var bruno = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(ana, bruno);

        var rosterA = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var rosterB = Roster.Create(tenantId, company.Id, WeekStart.AddDays(7)).Value;
        var shiftB = rosterB.AssignShift(ana, WeekStart.AddDays(7), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(rosterA, rosterB);

        var foreignSwap = ShiftSwapRequest.Create(
            tenantId, company.Id, rosterB.Id, ana.Id, shiftB.Id, bruno.Id).Value;
        _swapRepository.Seed(foreignSwap);

        const string tokenForRosterA = "token-roster-a";
        _shareLinkService.RegisterToken(
            tokenForRosterA,
            new RosterShareTokenPayloadDto(tenantId, company.Id, rosterA.Id, WeekStart, DateTime.UtcNow.AddDays(7)));

        var coreRespond = new RespondShiftSwapPeerUseCase(
            _companyRepository, _employeeRepository, _demandSlotRepository, _rosterRepository, _swapRepository, _currentUserService, _unitOfWork);
        var respondUseCase = new RespondSharedShiftSwapPeerUseCase(_shareLinkService, _scopedTenantContext, _rosterRepository, _swapRepository, coreRespond);

        // Act
        var result = await respondUseCase.ExecuteAsync(new RespondSharedShiftSwapPeerCommand(
            tokenForRosterA, foreignSwap.Id, bruno.Id, Accept: true));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ShiftSwap.NotFound");
    }
}

