using Application.DTOs.Rosters;
using Application.UnitTests.Fakes;
using Application.UseCases.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.UseCases.Rosters;

public class GetCompatibleSwapOptionsUseCaseTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 16);

    private readonly FakeRosterShareLinkService _shareLinkService = new();
    private readonly FakeScopedTenantContext _scopedTenantContext = new();
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeEmployeeRepository _employeeRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeRosterRepository _rosterRepository = new();
    private readonly FakeShiftSwapRequestRepository _swapRepository = new();
    private readonly FakeScopedAwareCurrentUserService _currentUserService;

    public GetCompatibleSwapOptionsUseCaseTests()
    {
        _currentUserService = new FakeScopedAwareCurrentUserService(_scopedTenantContext);
    }

    private GetCompatibleSwapOptionsUseCase CreateUseCase() =>
        new(
            _shareLinkService,
            _scopedTenantContext,
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _swapRepository,
            _currentUserService);

    [Fact]
    public async Task ExecuteAsync_WhenCandidatesIncludePrimaryPolyvalentAndCltIncompatible_ShouldReturnOnlyValidOptions()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Polivalente",
            BusinessType.Restaurant,
            new TimeOnly(10, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        // Terça-feira 11h-19h com demanda Peak
        for (var hour = 11; hour < 19; hour++)
        {
            var slot = DemandSlot.Create(
                tenantId, company.Id, DayOfWeek.Tuesday, hour, 45m).Value;
            slot.EvaluateRelativeDemand(45m).IsSuccess.Should().BeTrue();
            _demandSlotRepository.Seed(slot);
        }

        var requester = Employee.Create(tenantId, company.Id, "Ana Garçom", StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]).Value;
        var primaryPeer = Employee.Create(tenantId, company.Id, "Bruno Garçom", StationRole.Waiter).Value;
        var autonomousBarPeer = Employee.Create(tenantId, company.Id, "Carla Bar", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Autonomous).Value]).Value;
        var supportOnlyPeer = Employee.Create(tenantId, company.Id, "Diego Apoio", StationRole.Bar,
            [SecondarySkill.Create(StationRole.Waiter, SkillProficiency.Support).Value]).Value;
        var cltViolatingPeer = Employee.Create(tenantId, company.Id, "Eduardo Conflito", StationRole.Waiter).Value;

        _employeeRepository.Seed(requester, primaryPeer, autonomousBarPeer, supportOnlyPeer, cltViolatingPeer);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var tuesday = WeekStart.AddDays(1);
        var thursday = WeekStart.AddDays(3);

        var requesterShift = roster.AssignShift(requester, tuesday, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Peak).Value;
        var primaryPeerShift = roster.AssignShift(primaryPeer, thursday, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var autonomousBarShift = roster.AssignShift(autonomousBarPeer, thursday, new TimeOnly(16, 0), new TimeOnly(23, 0), StationRole.Bar).Value;
        // Eduardo trabalha segunda 17:00-01:00 e quarta 04:00-12:00: qualquer troca ou cobertura na terça 11:00-19:00 viola 11h
        roster.AssignShift(cltViolatingPeer, WeekStart, new TimeOnly(17, 0), new TimeOnly(1, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        roster.AssignShift(cltViolatingPeer, WeekStart.AddDays(2), new TimeOnly(4, 0), new TimeOnly(12, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        roster.Publish().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        const string token = "swap-options-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            token,
            requester.Id,
            requesterShift.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(o => o.TargetEmployeeId == primaryPeer.Id && o.IsMutualSwap && o.TargetShiftId == primaryPeerShift.Id);
        result.Value.Should().Contain(o => o.TargetEmployeeId == primaryPeer.Id && !o.IsMutualSwap && o.TargetShiftId == null);
        result.Value.Should().Contain(o =>
            o.TargetEmployeeId == autonomousBarPeer.Id &&
            o.IsMutualSwap &&
            o.TargetShiftId == autonomousBarShift.Id &&
            o.RequesterReplacementCoverageOrigin == CoverageOrigin.SecondarySkill);

        result.Value.Should().NotContain(o => o.TargetEmployeeId == supportOnlyPeer.Id);
        result.Value.Should().NotContain(o => o.TargetEmployeeId == cltViolatingPeer.Id);
        _scopedTenantContext.ScopedTenantId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetShiftHasPendingSwap_ShouldExcludeBusyShiftFromOptions()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Ocupada",
            BusinessType.Restaurant,
            new TimeOnly(10, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var requester = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var peer = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        var third = Employee.Create(tenantId, company.Id, "Carla", StationRole.Waiter).Value;
        _employeeRepository.Seed(requester, peer, third);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var requesterShift = roster.AssignShift(requester, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var busyPeerShift = roster.AssignShift(peer, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var freePeerShift = roster.AssignShift(peer, WeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var thirdShift = roster.AssignShift(third, WeekStart.AddDays(4), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        var pendingSwap = ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id,
            peer.Id, busyPeerShift.Id,
            third.Id, thirdShift.Id).Value;
        _swapRepository.Seed(pendingSwap);

        const string token = "busy-shift-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            token,
            requester.Id,
            requesterShift.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(o => o.TargetShiftId == busyPeerShift.Id);
        result.Value.Should().Contain(o => o.TargetShiftId == freePeerShift.Id);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetEmployeeHasPendingOneWayCoverageOnSameDayOrAtMaxDays_ShouldExcludeOneWayOption()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Cobertura",
            BusinessType.Restaurant,
            new TimeOnly(10, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var requester = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var otherRequester = Employee.Create(tenantId, company.Id, "Carla", StationRole.Waiter).Value;
        var sameDayPendingPeer = Employee.Create(tenantId, company.Id, "Bruno Mesmo Dia", StationRole.Waiter).Value;
        var maxDaysPendingPeer = Employee.Create(tenantId, company.Id, "Diego Teto 5x2", StationRole.Waiter).Value;
        var availablePeer = Employee.Create(tenantId, company.Id, "Eva Livre", StationRole.Waiter).Value;
        _employeeRepository.Seed(requester, otherRequester, sameDayPendingPeer, maxDaysPendingPeer, availablePeer);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var targetDate = WeekStart.AddDays(1); // Terça
        var requesterShift = roster.AssignShift(requester, targetDate, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var otherSameDayShift = roster.AssignShift(otherRequester, targetDate, new TimeOnly(14, 0), new TimeOnly(22, 0), StationRole.Waiter).Value;

        // Diego já trabalha 4 dias (Qua, Qui, Sex, Sáb) e tem 1 cobertura unilateral pendente no Domingo -> total 5 dias
        roster.AssignShift(maxDaysPendingPeer, WeekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        roster.AssignShift(maxDaysPendingPeer, WeekStart.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        roster.AssignShift(maxDaysPendingPeer, WeekStart.AddDays(4), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        roster.AssignShift(maxDaysPendingPeer, WeekStart.AddDays(5), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).IsSuccess.Should().BeTrue();
        var otherSundayShift = roster.AssignShift(otherRequester, WeekStart.AddDays(6), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        _rosterRepository.Seed(roster);

        // Bruno já tem solicitação pendente para cobrir o turno de terça de Carla (TargetShiftId == null)
        _swapRepository.Seed(ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id,
            otherRequester.Id, otherSameDayShift.Id,
            sameDayPendingPeer.Id, targetShiftId: null).Value);

        // Diego já tem solicitação pendente para cobrir o turno de domingo de Carla (atinge 5 dias com pendências)
        _swapRepository.Seed(ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id,
            otherRequester.Id, otherSundayShift.Id,
            maxDaysPendingPeer.Id, targetShiftId: null).Value);

        const string token = "pending-oneway-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            token,
            requester.Id,
            requesterShift.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(o => o.TargetEmployeeId == sameDayPendingPeer.Id && !o.IsMutualSwap);
        result.Value.Should().NotContain(o => o.TargetEmployeeId == maxDaysPendingPeer.Id && !o.IsMutualSwap);
        result.Value.Should().Contain(o => o.TargetEmployeeId == availablePeer.Id && !o.IsMutualSwap);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequesterShiftHasActiveRequest_ShouldReturnConflict()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Conflito",
            BusinessType.Restaurant,
            new TimeOnly(10, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var requester = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var peer = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;
        _employeeRepository.Seed(requester, peer);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var requesterShift = roster.AssignShift(requester, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        _rosterRepository.Seed(roster);

        _swapRepository.Seed(ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id,
            requester.Id, requesterShift.Id,
            peer.Id, targetShiftId: null).Value);

        const string token = "active-req-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            token,
            requester.Id,
            requesterShift.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ShiftSwap.ActiveRequestExists");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRosterIsArchived_ShouldReturnConflict()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var company = Company.Create(
            tenantId,
            "Casa Arquivada",
            BusinessType.Restaurant,
            new TimeOnly(10, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        _companyRepository.Seed(company);

        var requester = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        _employeeRepository.Seed(requester);

        var roster = Roster.Create(tenantId, company.Id, WeekStart).Value;
        var requesterShift = roster.AssignShift(requester, WeekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        roster.Archive().IsSuccess.Should().BeTrue();
        _rosterRepository.Seed(roster);

        const string token = "archived-roster-token";
        _shareLinkService.RegisterToken(
            token,
            new RosterShareTokenPayloadDto(tenantId, company.Id, roster.Id, WeekStart, DateTime.UtcNow.AddDays(5)));

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCompatibleSwapOptionsCommand(
            token,
            requester.Id,
            requesterShift.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RosterShare.ArchivedRoster");
    }
}
