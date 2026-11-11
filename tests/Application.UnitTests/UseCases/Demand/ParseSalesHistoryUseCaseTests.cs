using System.IO.Compression;
using System.Security;
using System.Text;
using Application.DTOs.Demand;
using Application.UnitTests.Fakes;
using Application.UseCases.Demand;
using Domain.Entities;
using Domain.Enums;

namespace Application.UnitTests.UseCases.Demand;

public class ParseSalesHistoryUseCaseTests
{
    private readonly FakeCompanyRepository _companyRepository = new();
    private readonly FakeDemandSlotRepository _demandSlotRepository = new();
    private readonly FakeCurrentUserService _currentUserService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private ParseSalesHistoryUseCase CreateUseCase() =>
        new(_companyRepository, _demandSlotRepository, _currentUserService, _unitOfWork);

    private Company SeedCompany(
        TimeOnly? opening = null,
        TimeOnly? closing = null,
        ScheduleModel scheduleModel = ScheduleModel.RotatingDaysOff,
        params DayOfWeek[] closedDays)
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Restaurante Teste",
            BusinessType.Restaurant,
            opening ?? new TimeOnly(11, 0),
            closing ?? new TimeOnly(14, 0),
            scheduleModel,
            closedDays).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private static MemoryStream CreateCsvStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    private static MemoryStream CreateSimpleXlsxStream(string dayName, string hour, string volume)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteZipEntry(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteZipEntry(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Plan1" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" t="inlineStr"><is><t>DiaSemana</t></is></c>
                      <c r="B1" t="inlineStr"><is><t>Hora</t></is></c>
                      <c r="C1" t="inlineStr"><is><t>Volume</t></is></c>
                    </row>
                    <row r="2">
                      <c r="A2" t="inlineStr"><is><t>{SecurityElement.Escape(dayName)}</t></is></c>
                      <c r="B2"><v>{SecurityElement.Escape(hour)}</v></c>
                      <c r="C2"><v>{SecurityElement.Escape(volume)}</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content.Trim());
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
        result.Error.Code.Should().Be("DemandImport.InvalidCommand");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTenantIdIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        _currentUserService.TenantId = Guid.Empty;
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            ManualEntries: [new ManualDemandEntryDto(DayOfWeek.Friday, 12, 50m)]));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Demand.EmptyTenant");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompanyDoesNotExist_ShouldReturnNotFoundFailure()
    {
        // Arrange
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            CompanyId: Guid.NewGuid(),
            ManualEntries: [new ManualDemandEntryDto(DayOfWeek.Friday, 12, 50m)]));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Company.NotFound");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCsvFileImportedWithFillMissingHours_ShouldPersistImportedAndZeroFilledOpenHours()
    {
        // Arrange
        // Aberto de 11h às 14h (horas 11, 12, 13) exceto segunda-feira -> 6 dias * 3h = 18 slots abertos
        var company = SeedCompany(
            new TimeOnly(11, 0),
            new TimeOnly(14, 0),
            ScheduleModel.FixedClosure,
            DayOfWeek.Monday);

        using var stream = CreateCsvStream("DiaSemana;Hora;Volume\nSexta;12;100\nSabado;13;150");
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.Csv,
            CompanyId: company.Id,
            FileStream: stream,
            FileName: "vendas_pdv.csv",
            FillMissingOperatingHoursWithZero: true));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.Source.Should().Be(DemandImportSource.Csv);
        result.Value.RawRowsProcessed.Should().Be(2);
        result.Value.ImportedSlotsCount.Should().Be(18);
        result.Value.TotalExpectedWeeklyVolume.Should().Be(250m);
        _unitOfWork.SaveChangesCount.Should().Be(1);
        _demandSlotRepository.PersistedSlots.Should().HaveCount(18);
        _demandSlotRepository.PersistedSlots.Should().NotContain(s => s.DayOfWeek == DayOfWeek.Monday);
        _demandSlotRepository.PersistedSlots.Single(s => s.DayOfWeek == DayOfWeek.Friday && s.Hour == 12)
            .ExpectedVolume.Should().Be(100m);
        _demandSlotRepository.PersistedSlots.Single(s => s.DayOfWeek == DayOfWeek.Friday && s.Hour == 11)
            .ExpectedVolume.Should().Be(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenXlsxFileImportedWithoutZeroFilling_ShouldPersistOnlyImportedSlots()
    {
        // Arrange
        var company = SeedCompany();
        using var stream = CreateSimpleXlsxStream("Sexta", "12", "90");
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.Xlsx,
            FileStream: stream,
            FileName: "historico.xlsx",
            FillMissingOperatingHoursWithZero: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CompanyId.Should().Be(company.Id);
        result.Value.Source.Should().Be(DemandImportSource.Xlsx);
        result.Value.ImportedSlotsCount.Should().Be(1);
        _demandSlotRepository.PersistedSlots.Should().ContainSingle();
        _unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileExtensionDoesNotMatchSource_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompany();
        using var stream = CreateCsvStream("DiaSemana;Hora;Volume\nSexta;12;50");
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.Xlsx,
            FileStream: stream,
            FileName: "arquivo.xls"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.UnsupportedFileExtension");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenManualMatrixReimported_ShouldUpdateExistingAddNewAndRemoveObsoleteSlots()
    {
        // Arrange
        var company = SeedCompany();
        var existingFriday12 = DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Friday, 12, 30m).Value;
        var obsoleteTuesday11 = DemandSlot.Create(_currentUserService.TenantId, company.Id, DayOfWeek.Tuesday, 11, 20m).Value;
        _demandSlotRepository.Seed(existingFriday12, obsoleteTuesday11);

        var useCase = CreateUseCase();
        var entries = new[]
        {
            new ManualDemandEntryDto(DayOfWeek.Friday, 12, 85m),
            new ManualDemandEntryDto(DayOfWeek.Saturday, 13, 120m)
        };

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            CompanyId: company.Id,
            ManualEntries: entries,
            FillMissingOperatingHoursWithZero: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(DemandImportSource.ManualMatrix);
        result.Value.ImportedSlotsCount.Should().Be(2);
        _unitOfWork.SaveChangesCount.Should().Be(1);

        _demandSlotRepository.UpdatedSlots.Should().ContainSingle(s => s.Id == existingFriday12.Id);
        existingFriday12.ExpectedVolume.Should().Be(85m);

        _demandSlotRepository.AddedSlots.Should().ContainSingle(s => s.DayOfWeek == DayOfWeek.Saturday && s.Hour == 13);
        _demandSlotRepository.RemovedSlots.Should().ContainSingle(s => s.Id == obsoleteTuesday11.Id);
        _demandSlotRepository.PersistedSlots.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenManualMatrixContainsDuplicateSlot_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            ManualEntries:
            [
                new ManualDemandEntryDto(DayOfWeek.Friday, 12, 40m),
                new ManualDemandEntryDto(DayOfWeek.Friday, 12, 60m)
            ]));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.DuplicateManualSlot");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenManualMatrixIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            ManualEntries: []));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.EmptyManualEntries");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCsvContainsRowsOutsideOperatingHours_ShouldIncludeWarningAndPreserveSlot()
    {
        // Arrange
        var company = SeedCompany(new TimeOnly(11, 0), new TimeOnly(14, 0));
        using var stream = CreateCsvStream("DiaSemana;Hora;Volume\nSexta;12;100\nSexta;22;5");
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.Csv,
            CompanyId: company.Id,
            FileStream: stream,
            FileName: "vendas.csv",
            FillMissingOperatingHoursWithZero: false));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().Contain(w => w.Contains("fora da janela", StringComparison.OrdinalIgnoreCase));
        result.Value.Slots.Single(s => s.Hour == 22).IsWithinOperatingHours.Should().BeFalse();
        result.Value.Slots.Single(s => s.Hour == 12).IsWithinOperatingHours.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceIsInvalid_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand((DemandImportSource)99));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.InvalidSource");
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileStreamIsNullForCsv_ShouldReturnValidationFailure()
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.Csv,
            FileStream: null,
            FileName: "vendas.csv"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.EmptyFile");
    }

    [Theory]
    [InlineData((DayOfWeek)99, 12, 10, "DemandImport.InvalidDayOfWeek")]
    [InlineData(DayOfWeek.Monday, 24, 10, "DemandImport.InvalidHour")]
    [InlineData(DayOfWeek.Monday, 12, -1, "DemandImport.NegativeVolume")]
    public async Task ExecuteAsync_WhenManualEntryIsInvalid_ShouldReturnValidationFailure(
        DayOfWeek dayOfWeek,
        int hour,
        decimal volume,
        string expectedCode)
    {
        // Arrange
        SeedCompany();
        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new ParseSalesHistoryCommand(
            DemandImportSource.ManualMatrix,
            ManualEntries: [new ManualDemandEntryDto(dayOfWeek, hour, volume)]));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }
}
