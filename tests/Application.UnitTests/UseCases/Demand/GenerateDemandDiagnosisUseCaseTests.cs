using Application.UnitTests.Fakes;
using Application.UseCases.Demand;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Demand;

public class GenerateDemandDiagnosisUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private GenerateDemandDiagnosisUseCase CreateUseCase() =>
        new(_companyRepository, _demandSlotRepository, _currentUserService, _unitOfWork);

    private Company SeedCompany(
        TimeOnly? opening = null,
        TimeOnly? closing = null,
        ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff,
        params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Diagnóstico",
            BusinessType.Restaurant,
            opening ?? new TimeOnly(11, 0),
            closing ?? new TimeOnly(14, 0),
            scheduleModel,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateDemandDiagnosisCommand());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Demand.EmptyTenant");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyNotFound_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateDemandDiagnosisCommand(Guid.NewGuid()));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyHasNoDemandSlots_ShouldReturnValidationFailure()
    {
        // Arrange
        var company = SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateDemandDiagnosisCommand(company.Id));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandDiagnosis.EmptyDemandSlots");
    }

    [Fact]
    public async Task ExecuteAsync_WhenSlotsExist_ShouldClassifyPeaksValleysIdlenessAndPersistUpdates()
    {
        // Arrange
        // Aberto 11h-14h (horas 11, 12, 13). Segunda fechada.
        var company = SeedCompany(
            new TimeOnly(11, 0),
            new TimeOnly(14, 0),
            ScheduleModel.FixedClosure,
            DayOfWeek.Monday);

        var tenantId = _currentUserService.TenantId;
        var slots = new[]
        {
            // Sexta: pico (100), moderado (60), baixo (40)
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 11, 40m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 100m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 13, 60m).Value,
            // Terça: todos baixos/ociosos (10, 20, 15) -> dia mais fraco entre os abertos
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 11, 10m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 12, 20m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 13, 15m).Value,
            // Sábado: picos (80, 90, 75)
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 11, 80m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 12, 90m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 13, 75m).Value,
            // Slot residual fora do horário (não deve entrar como vale aberto)
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Monday, 12, 5m).Value
        };
        _demandSlotRepository.Seed(slots);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateDemandDiagnosisCommand(company.Id));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var diagnosis = result.Value;

        diagnosis.CompanyId.Should().Be(company.Id);
        diagnosis.PeakVolume.Should().Be(100m);
        diagnosis.GlobalPeakSlot.Should().NotBeNull();
        diagnosis.GlobalPeakSlot!.DayOfWeek.Should().Be(DayOfWeek.Friday);
        diagnosis.GlobalPeakSlot.Hour.Should().Be(12);

        // 4 Peak slots (Fri 12, Sat 11, Sat 12, Sat 13)
        diagnosis.PeakSlots.Should().HaveCount(4);
        diagnosis.PeakSlots.Select(s => (s.DayOfWeek, s.Hour)).Should().StartWith((DayOfWeek.Friday, 12));

        // 4 Valley slots abertos (Fri 11 [Low 0.40] e Tue 11,12,13 [Idle <= 0.25]); Monday 12h está fechado
        diagnosis.ValleySlots.Should().HaveCount(4);
        diagnosis.ValleySlots.Should().NotContain(s => s.DayOfWeek == DayOfWeek.Monday);

        // 9 open slots, 4 idle/low => 44.44%
        diagnosis.IdlenessPercentage.Should().Be(44.44m);
        diagnosis.DailySummaries.Should().HaveCount(7);

        // Recomendação 5x2: Segunda (fechada fixa) e Terça (menor demanda aberta com slots) ou outro dia zerado
        diagnosis.RecommendedTwoDaysOff.Should().HaveCount(2);
        diagnosis.RecommendedTwoDaysOff[0].Should().Be(DayOfWeek.Monday);

        // ADR 0012: Update deve ser chamado para todos os slots modificados antes do SaveChangesAsync
        _demandSlotRepository.UpdatedSlots.Should().HaveCount(slots.Length);
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllSlotsHaveZeroVolume_ShouldHandleZeroPeakSafely()
    {
        // Arrange
        var company = SeedCompany();
        var slot1 = DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Wednesday, 11, 0m).Value;
        var slot2 = DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Wednesday, 12, 0m).Value;
        _demandSlotRepository.Seed(slot1, slot2);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new GenerateDemandDiagnosisCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PeakVolume.Should().Be(0m);
        result.Value.TotalWeeklyVolume.Should().Be(0m);
        result.Value.PeakSlots.Should().BeEmpty();
        result.Value.ValleySlots.Should().HaveCount(2);
        result.Value.IdlenessPercentage.Should().Be(100.00m);
        _demandSlotRepository.UpdatedSlots.Should().HaveCount(2);
        _unitOfWork.SaveChangesCount.Should().Be(1);
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
        result.Error.Code.Should().Be("DemandDiagnosis.InvalidCommand");
    }

    [Fact]
    public async Task ExecuteAsync_AfterFullWeekImport_ShouldRecommendTwoLowestDemandOpenDays()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(12, 0), new TimeOnly(13, 0), ScheduleModel.RotatingDaysOff);
        var entries = new[]
        {
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Sunday, 12, 90m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Monday, 12, 10m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Tuesday, 12, 15m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Wednesday, 12, 50m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Thursday, 12, 60m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Friday, 12, 100m),
            new Application.DTOs.Demand.ManualDemandEntryDto(DayOfWeek.Saturday, 12, 95m)
        };

        var importUseCase = new ParseSalesHistoryUseCase(_companyRepository, _demandSlotRepository, _currentUserService, _unitOfWork);
        var importResult = await importUseCase.ExecuteAsync(new ParseSalesHistoryCommand(
            Application.DTOs.Demand.DemandImportSource.ManualMatrix,
            CompanyId: company.Id,
            ManualEntries: entries,
            FillMissingOperatingHoursWithZero: false));
        importResult.IsSuccess.Should().BeTrue();

        var diagnosisUseCase = CreateUseCase();

        // Act
        var diagnosisResult = await diagnosisUseCase.ExecuteAsync(new GenerateDemandDiagnosisCommand(company.Id));

        // Assert
        diagnosisResult.IsSuccess.Should().BeTrue();
        diagnosisResult.Value.RecommendedTwoDaysOff.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday);
        diagnosisResult.Value.TotalWeeklyVolume.Should().Be(420m);
        diagnosisResult.Value.PeakVolume.Should().Be(100m);
    }
}
