using Application.UnitTests.Fakes;
using Application.UseCases.Demand;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Demand;

public class GetCurrentDemandWorkspaceUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();

    private GetCurrentDemandWorkspaceUseCase CreateUseCase() =>
        new(_companyRepository, _demandSlotRepository, _currentUserService);

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentDemandWorkspaceCommand());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Demand.EmptyTenant");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentDemandWorkspaceCommand());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyExistsWithoutSlots_ShouldReturnOperatingGridAndNullDiagnosis()
    {
        // Arrange
        // Virada noturna 18:00-02:00 => horas 0, 1, 18, 19, 20, 21, 22, 23; Segunda fechada.
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bar Noturno",
            BusinessType.Bar,
            new TimeOnly(18, 0),
            new TimeOnly(2, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;
        _companyRepository.Seed(company);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentDemandWorkspaceCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        var workspace = result.Value;
        workspace.Company.CompanyId.Should().Be(company.Id);
        workspace.Company.Name.Should().Be("Bar Noturno");
        workspace.Company.OperatesOvernight.Should().BeTrue();
        workspace.Diagnosis.Should().BeNull();

        workspace.OperatingHours.Should().Equal(0, 1, 18, 19, 20, 21, 22, 23);
        workspace.OperatingSlots.Should().Contain(s => s.DayOfWeek == DayOfWeek.Monday && s.Hour == 19 && !s.IsOpen);
        workspace.OperatingSlots.Should().Contain(s => s.DayOfWeek == DayOfWeek.Friday && s.Hour == 19 && s.IsOpen);
        workspace.OperatingSlots.Should().Contain(s => s.DayOfWeek == DayOfWeek.Saturday && s.Hour == 1 && s.IsOpen);
        workspace.OperatingSlots.Should().NotContain(s => s.Hour == 12);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasDemandSlots_ShouldReturnDiagnosisWithoutMutatingRepository()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Centro",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(14, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;
        _companyRepository.Seed(company);

        var tenantId = _currentUserService.TenantId;
        _demandSlotRepository.Seed(
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 11, 10m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 12, 20m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 100m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 12, 80m).Value);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GetCurrentDemandWorkspaceCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        var workspace = result.Value;
        workspace.Diagnosis.Should().NotBeNull();
        workspace.Diagnosis!.PeakVolume.Should().Be(100m);
        workspace.Diagnosis.GlobalPeakSlot.Should().NotBeNull();
        workspace.Diagnosis.GlobalPeakSlot!.DayOfWeek.Should().Be(DayOfWeek.Friday);
        workspace.Diagnosis.PeakSlots.Should().HaveCount(2);
        workspace.Diagnosis.RecommendedTwoDaysOff.Should().StartWith(DayOfWeek.Monday);
        _demandSlotRepository.UpdatedSlots.Should().BeEmpty();
    }
}

