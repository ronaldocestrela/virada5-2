using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Application.UseCases.Demand.Parsing;

namespace Application.UnitTests.UseCases.Demand.Parsing;

public class XlsxSalesHistoryParserTests
{
    private readonly XlsxSalesHistoryParser _parser = new();

    private sealed record XlsxCellSpec(string Column, string Value, bool IsNumeric = false, bool UseInlineString = false);

    private static MemoryStream BuildXlsxStream(IReadOnlyList<IReadOnlyList<XlsxCellSpec>> rows)
    {
        var sharedStrings = new List<string>();
        var sharedStringIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        int GetSharedStringId(string text)
        {
            if (sharedStringIndex.TryGetValue(text, out var existing))
            {
                return existing;
            }

            var id = sharedStrings.Count;
            sharedStrings.Add(text);
            sharedStringIndex[text] = id;
            return id;
        }

        var sheetRowsBuilder = new StringBuilder();
        for (var r = 0; r < rows.Count; r++)
        {
            var rowNumber = r + 1;
            sheetRowsBuilder.Append($"<row r=\"{rowNumber}\">");
            foreach (var cell in rows[r])
            {
                var cellRef = $"{cell.Column}{rowNumber}";
                if (cell.IsNumeric)
                {
                    sheetRowsBuilder.Append($"<c r=\"{cellRef}\"><v>{SecurityElement.Escape(cell.Value)}</v></c>");
                }
                else if (cell.UseInlineString)
                {
                    sheetRowsBuilder.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(cell.Value)}</t></is></c>");
                }
                else
                {
                    var idx = GetSharedStringId(cell.Value);
                    sheetRowsBuilder.Append($"<c r=\"{cellRef}\" t=\"s\"><v>{idx}</v></c>");
                }
            }

            sheetRowsBuilder.Append("</row>");
        }

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
                </Types>
                """);

            WriteEntry(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            WriteEntry(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Vendas" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);

            WriteEntry(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sstItems = string.Concat(sharedStrings.Select(s => $"<si><t>{SecurityElement.Escape(s)}</t></si>"));
            WriteEntry(archive, "xl/sharedStrings.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{sharedStrings.Count}" uniqueCount="{sharedStrings.Count}">
                  {sstItems}
                </sst>
                """);

            WriteEntry(archive, "xl/worksheets/sheet1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    {sheetRowsBuilder}
                  </sheetData>
                </worksheet>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content.Trim());
    }

    [Fact]
    public void Parse_WhenStreamIsNull_ShouldReturnValidationFailure()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.EmptyFile");
    }

    [Fact]
    public void Parse_WhenBytesAreNotValidZipArchive_ShouldReturnValidationFailureWithoutThrowing()
    {
        // Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not-an-xlsx-file"));

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.InvalidXlsx");
    }

    [Fact]
    public void Parse_WhenTransactionalXlsxUsesExcelSerialDatesAndSharedStrings_ShouldAggregateCorrectly()
    {
        // Arrange
        var fri1 = new DateTime(2026, 10, 9, 20, 15, 0).ToOADate().ToString(CultureInfo.InvariantCulture);
        var fri2 = new DateTime(2026, 10, 16, 20, 45, 0).ToOADate().ToString(CultureInfo.InvariantCulture);
        var rows = new List<IReadOnlyList<XlsxCellSpec>>
        {
            new[]
            {
                new XlsxCellSpec("A", "DataHora"),
                new XlsxCellSpec("C", "Quantidade")
            },
            new[]
            {
                new XlsxCellSpec("A", fri1, IsNumeric: true),
                new XlsxCellSpec("C", "80", IsNumeric: true)
            },
            new[]
            {
                new XlsxCellSpec("A", fri2, IsNumeric: true),
                new XlsxCellSpec("C", "120", IsNumeric: true)
            }
        };
        using var stream = BuildXlsxStream(rows);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RawRowsProcessed.Should().Be(2);
        var slot = result.Value.Slots.Should().ContainSingle().Subject;
        slot.DayOfWeek.Should().Be(DayOfWeek.Friday);
        slot.Hour.Should().Be(20);
        slot.ExpectedVolume.Should().Be(100m);
    }

    [Fact]
    public void Parse_WhenWeeklyMatrixXlsxUsesInlineStringsAndGapsInColumns_ShouldParseCorrectly()
    {
        // Arrange
        var rows = new List<IReadOnlyList<XlsxCellSpec>>
        {
            new[]
            {
                new XlsxCellSpec("A", "DiaSemana", UseInlineString: true),
                new XlsxCellSpec("B", "Ignorar", UseInlineString: true),
                new XlsxCellSpec("C", "Hora", UseInlineString: true),
                new XlsxCellSpec("D", "Volume", UseInlineString: true)
            },
            new[]
            {
                new XlsxCellSpec("A", "Quinta-feira", UseInlineString: true),
                new XlsxCellSpec("C", "18", IsNumeric: true),
                new XlsxCellSpec("D", "64.5", IsNumeric: true)
            }
        };
        using var stream = BuildXlsxStream(rows);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var slot = result.Value.Slots.Should().ContainSingle().Subject;
        slot.DayOfWeek.Should().Be(DayOfWeek.Thursday);
        slot.Hour.Should().Be(18);
        slot.ExpectedVolume.Should().Be(64.5m);
    }

    [Fact]
    public void Parse_WhenWorksheetHasNoDataRows_ShouldReturnValidationFailure()
    {
        // Arrange
        var rows = new List<IReadOnlyList<XlsxCellSpec>>
        {
            new[]
            {
                new XlsxCellSpec("A", "DiaSemana"),
                new XlsxCellSpec("B", "Hora"),
                new XlsxCellSpec("C", "Volume")
            }
        };
        using var stream = BuildXlsxStream(rows);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.NoDataRows");
    }
}

