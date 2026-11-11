using System.IO.Compression;
using System.Text;
using Application.Common.Interfaces;
using Application.UseCases.Demand;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Demand;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class DemandMappingTests : BunitContext
{
    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();

    public DemandMappingTests()
    {
        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddScoped<GetCurrentDemandWorkspaceUseCase>();
        Services.AddScoped<ParseSalesHistoryUseCase>();
        Services.AddScoped<GenerateDemandDiagnosisUseCase>();
        Services.AddSingleton<IToastService>(_toastService);
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingRequiredCallToAction()
    {
        // Act
        var cut = Render<DemandMapping>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Importação e Mapa de Demanda");
        cut.Find("[data-testid=\"demand-onboarding-required\"]").TextContent.Should().Contain("Onboarding");
        cut.Markup.Should().Contain("/onboarding");
    }

    [Fact]
    public void Render_WhenCompanyExistsWithoutDemand_ShouldRenderDropzoneAndEmptyHeatmap()
    {
        // Arrange
        SeedCompany();

        // Act
        var cut = Render<DemandMapping>();

        // Assert
        cut.Find("[data-testid=\"demand-dropzone\"]").Should().NotBeNull();
        cut.Find(".demand-heatmap__empty").TextContent.Should().Contain("Nenhum mapa de demanda");
    }

    [Fact]
    public void Render_WhenDemandSlotsAlreadyExist_ShouldRenderPopulatedHeatmapOnLoad()
    {
        // Arrange
        var company = SeedCompany();
        var tenantId = _currentUserService.TenantId;
        _demandSlotRepository.Seed(
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 12, 10m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 100m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 12, 85m).Value);

        // Act
        var cut = Render<DemandMapping>();

        // Assert
        cut.FindAll(".demand-heatmap__cell--peak").Should().HaveCount(2);
        cut.Find("[data-testid=\"demand-kpis\"]").TextContent.Should().Contain("100");
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task FileUpload_WhenValidCsvUploaded_ShouldImportPersistAndUpdateHeatmapReactively()
    {
        // Arrange
        SeedCompany();
        var cut = Render<DemandMapping>();

        var csvContent = new StringBuilder()
            .AppendLine("DiaSemana;Hora;Volume")
            .AppendLine("Segunda;12;15")
            .AppendLine("Terca;12;10")
            .AppendLine("Sexta;12;100")
            .AppendLine("Sabado;12;90")
            .ToString();

        var inputFile = cut.FindComponent<InputFile>();

        // Act
        await cut.InvokeAsync(() =>
            inputFile.UploadFiles(InputFileContent.CreateFromText(csvContent, "vendas.csv")));

        // Assert
        _demandSlotRepository.Slots.Should().NotBeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(2); // ParseSalesHistory + GenerateDemandDiagnosis
        cut.FindAll(".demand-heatmap__cell--peak").Should().HaveCount(2);
        cut.Find("[data-testid=\"demand-warnings\"]").TextContent.Should().Contain("fora da janela");
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public async Task FileUpload_WhenValidXlsxUploaded_ShouldImportAndRenderHeatmap()
    {
        // Arrange
        SeedCompany();
        var cut = Render<DemandMapping>();
        var xlsxBytes = CreateMinimalXlsx(
            ["DiaSemana", "Hora", "Volume"],
            ["Sexta", "12", "120"],
            ["Sabado", "12", "100"],
            ["Terca", "12", "20"]);

        var inputFile = cut.FindComponent<InputFile>();

        // Act
        await cut.InvokeAsync(() =>
            inputFile.UploadFiles(InputFileContent.CreateFromBinary(xlsxBytes, "historico.xlsx")));

        // Assert
        _demandSlotRepository.Slots.Should().NotBeEmpty();
        cut.FindAll(".demand-heatmap__cell--peak").Should().HaveCount(2);
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public async Task FileUpload_WhenExtensionIsUnsupported_ShouldNotPersistAndShowErrorFeedback()
    {
        // Arrange
        SeedCompany();
        var cut = Render<DemandMapping>();
        var inputFile = cut.FindComponent<InputFile>();

        // Act
        await cut.InvokeAsync(() =>
            inputFile.UploadFiles(InputFileContent.CreateFromText("conteudo", "vendas.txt")));

        // Assert
        _demandSlotRepository.Slots.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
        cut.Find("[role=\"alert\"]").TextContent.Should().Contain(".csv ou .xlsx");
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Error);
    }

    [Fact]
    public async Task ManualMatrix_WhenTypicalCurveAppliedAndSaved_ShouldPersistAndUpdateHeatmapReactively()
    {
        // Arrange
        SeedCompany();
        var cut = Render<DemandMapping>();

        // Act - switch to manual mode
        cut.Find("[data-testid=\"mode-tab-manual\"]").Click();
        cut.FindAll("[data-testid=\"manual-cell-Monday-12\"]").Should().BeEmpty(); // Monday is closed

        cut.Find("[data-testid=\"manual-preset-typical\"]").Click();
        cut.Find("[data-testid=\"manual-cell-Friday-12\"]").Change("150");
        await cut.Find("[data-testid=\"manual-matrix-form\"]").SubmitAsync();

        // Assert
        _demandSlotRepository.Slots.Should().Contain(s =>
            s.DayOfWeek == DayOfWeek.Friday &&
            s.Hour == 12 &&
            s.ExpectedVolume == 150m);
        _unitOfWork.SaveChangesCount.Should().Be(2);
        cut.Find(".demand-heatmap__cell--peak").Should().NotBeNull();
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public void HeatmapControls_WhenToggledToAbsoluteAndSlotSelected_ShouldUpdateDisplayAndAllowQuickEdit()
    {
        // Arrange
        var company = SeedCompany();
        var tenantId = _currentUserService.TenantId;
        _demandSlotRepository.Seed(
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Tuesday, 12, 25m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 12, 120m).Value);

        var cut = Render<DemandMapping>();

        // Act - toggle absolute volume
        cut.Find("[data-testid=\"heatmap-toggle-absolute\"]").Click();

        // Assert
        cut.Find(".demand-heatmap__cell--peak").TextContent.Trim().Should().Be("120");

        // Act - select peak cell
        cut.Find(".demand-heatmap__cell--peak").Click();

        // Assert
        cut.Find("[data-testid=\"heatmap-quick-edit\"]").Should().NotBeNull();
        cut.Find("[data-testid=\"quick-edit-volume\"]").GetAttribute("value").Should().Be("120");

        // Act - quick edit slot volume down and save
        cut.Find("[data-testid=\"quick-edit-volume\"]").Change("40");
        cut.Find("[data-testid=\"quick-edit-save\"]").Click();

        // Assert
        _demandSlotRepository.Slots.Should().Contain(s =>
            s.DayOfWeek == DayOfWeek.Friday &&
            s.Hour == 12 &&
            s.ExpectedVolume == 40m);
        cut.Find("[data-testid=\"quick-edit-volume\"]").GetAttribute("value").Should().Be("40");
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public void ManualMatrix_WhenDayReplicatedAndCleared_ShouldUpdateCellsAndWeeklyTotal()
    {
        // Arrange
        SeedCompany();
        var cut = Render<DemandMapping>();

        cut.Find("[data-testid=\"mode-tab-manual\"]").Click();
        cut.Find("[data-testid=\"manual-cell-Tuesday-12\"]").Change("50");

        // Act - replicate Tuesday to other open days (Wed, Thu, Fri, Sat, Sun = 6 open days * 50 = 300)
        cut.Find("[data-testid=\"manual-copy-weekdays\"]").Click();

        // Assert
        cut.Find("[data-testid=\"manual-cell-Friday-12\"]").GetAttribute("value").Should().Be("50");
        cut.Find(".demand-mapping__manual-total").TextContent.Should().Contain("300");

        // Act - clear matrix
        cut.Find("[data-testid=\"manual-preset-clear\"]").Click();

        // Assert
        cut.Find("[data-testid=\"manual-cell-Friday-12\"]").GetAttribute("value").Should().Be("0");
        cut.Find(".demand-mapping__manual-total").TextContent.Should().Contain("0");
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Cantina do Bairro",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(14, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;
        _companyRepository.Seed(company);
        return company;
    }

    private static byte[] CreateMinimalXlsx(params string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);

            WriteZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            WriteZipEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Vendas" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);

            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sheetXml = new StringBuilder();
            sheetXml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
            sheetXml.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

            for (var r = 0; r < rows.Length; r++)
            {
                sheetXml.Append($"""<row r="{r + 1}">""");
                for (var c = 0; c < rows[r].Length; c++)
                {
                    var col = (char)('A' + c);
                    var value = System.Security.SecurityElement.Escape(rows[r][c]);
                    sheetXml.Append($"""<c r="{col}{r + 1}" t="inlineStr"><is><t>{value}</t></is></c>""");
                }

                sheetXml.Append("</row>");
            }

            sheetXml.Append("</sheetData></worksheet>");
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml", sheetXml.ToString());
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        public Company? CurrentCompany { get; private set; }

        public void Seed(Company company) => CurrentCompany = company;

        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(CurrentCompany?.Id == id ? CurrentCompany : null);

        public Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(CurrentCompany);

        public Task AddAsync(Company company, CancellationToken ct = default)
        {
            CurrentCompany = company;
            return Task.CompletedTask;
        }

        public void Update(Company company) => CurrentCompany = company;
    }

    private sealed class InMemoryDemandSlotRepository : IDemandSlotRepository
    {
        public List<DemandSlot> Slots { get; } = [];

        public void Seed(params DemandSlot[] slots) => Slots.AddRange(slots);

        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Slots.FirstOrDefault(s => s.Id == id));

        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(
            Guid companyId,
            DayOfWeek dayOfWeek,
            int hour,
            CancellationToken ct = default) =>
            Task.FromResult(Slots.FirstOrDefault(s =>
                s.CompanyId == companyId &&
                s.DayOfWeek == dayOfWeek &&
                s.Hour == hour));

        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DemandSlot>>(
                Slots.Where(s => s.CompanyId == companyId)
                    .OrderBy(s => s.DayOfWeek)
                    .ThenBy(s => s.Hour)
                    .ToList());

        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default)
        {
            Slots.Add(demandSlot);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default)
        {
            Slots.AddRange(demandSlots);
            return Task.CompletedTask;
        }

        public void Update(DemandSlot demandSlot)
        {
        }

        public void RemoveRange(IEnumerable<DemandSlot> demandSlots)
        {
            foreach (var slot in demandSlots.ToList())
            {
                Slots.Remove(slot);
            }
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }
}
