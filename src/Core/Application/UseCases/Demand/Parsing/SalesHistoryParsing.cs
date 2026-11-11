using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Application.DTOs.Demand;

namespace Application.UseCases.Demand.Parsing;

public sealed record ParsedDemandMatrixResult(
    IReadOnlyList<ManualDemandEntryDto> Slots,
    int RawRowsProcessed,
    IReadOnlyList<string> Warnings);

public sealed class CsvSalesHistoryParser
{
    public Result<ParsedDemandMatrixResult> Parse(Stream stream)
    {
        if (stream is null)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyFile", "O arquivo CSV não foi informado."));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyFile", "O arquivo CSV está vazio."));
        }

        var delimiter = DetectDelimiter(content);
        var rows = ParseCsvRows(content, delimiter);
        return TabularSalesHistoryAggregator.Aggregate(rows);
    }

    private static char DetectDelimiter(string content)
    {
        var inQuotes = false;
        var semicolons = 0;
        var commas = 0;

        foreach (var ch in content)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && (ch == '\r' || ch == '\n'))
            {
                if (semicolons > 0 || commas > 0)
                {
                    break;
                }

                continue;
            }

            if (!inQuotes)
            {
                if (ch == ';')
                {
                    semicolons++;
                }
                else if (ch == ',')
                {
                    commas++;
                }
            }
        }

        return semicolons >= commas ? ';' : ',';
    }

    private static List<IReadOnlyList<string>> ParseCsvRows(string content, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                currentRow.Add(currentCell.ToString());
                currentCell.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                currentRow.Add(currentCell.ToString());
                currentCell.Clear();

                if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(currentRow);
                }

                currentRow = [];
            }
            else
            {
                currentCell.Append(ch);
            }
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            if (currentRow.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(currentRow);
            }
        }

        return rows;
    }
}

public sealed class XlsxSalesHistoryParser
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public Result<ParsedDemandMatrixResult> Parse(Stream stream)
    {
        if (stream is null)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyFile", "O arquivo XLSX não foi informado."));
        }

        if (stream.CanSeek)
        {
            if (stream.Length == 0)
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.EmptyFile", "O arquivo XLSX está vazio."));
            }

            stream.Position = 0;
        }

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = ResolveFirstWorksheetEntry(archive);
            if (worksheetEntry is null)
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.InvalidXlsx", "Não foi possível localizar a planilha principal no arquivo XLSX."));
            }

            var sharedStrings = ReadSharedStrings(archive);
            var rows = ReadWorksheetRows(worksheetEntry, sharedStrings);
            return TabularSalesHistoryAggregator.Aggregate(rows);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.InvalidXlsx", "O arquivo informado não é um XLSX válido ou está corrompido."));
        }
    }

    private static ZipArchiveEntry? ResolveFirstWorksheetEntry(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");

        if (workbookEntry is not null && relsEntry is not null)
        {
            var workbookDoc = LoadXml(workbookEntry);
            var firstSheet = workbookDoc
                .Descendants(SpreadsheetNs + "sheet")
                .FirstOrDefault();

            var relId = firstSheet?.Attribute(OfficeRelNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(relId))
            {
                var relsDoc = LoadXml(relsEntry);
                var target = relsDoc
                    .Descendants(PackageRelNs + "Relationship")
                    .FirstOrDefault(r => string.Equals(r.Attribute("Id")?.Value, relId, StringComparison.Ordinal))
                    ?.Attribute("Target")
                    ?.Value;

                if (!string.IsNullOrWhiteSpace(target))
                {
                    var normalizedTarget = target.TrimStart('/');
                    if (!normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedTarget = $"xl/{normalizedTarget}";
                    }

                    var resolved = archive.GetEntry(normalizedTarget);
                    if (resolved is not null)
                    {
                        return resolved;
                    }
                }
            }
        }

        return archive.GetEntry("xl/worksheets/sheet1.xml");
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var sstEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sstEntry is null)
        {
            return [];
        }

        var doc = LoadXml(sstEntry);
        return doc
            .Descendants(SpreadsheetNs + "si")
            .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
            .ToList();
    }

    private static List<IReadOnlyList<string>> ReadWorksheetRows(
        ZipArchiveEntry worksheetEntry,
        IReadOnlyList<string> sharedStrings)
    {
        var doc = LoadXml(worksheetEntry);
        var rows = new List<IReadOnlyList<string>>();

        foreach (var rowElement in doc.Descendants(SpreadsheetNs + "row"))
        {
            var cells = new List<string>();
            foreach (var cellElement in rowElement.Elements(SpreadsheetNs + "c"))
            {
                var reference = cellElement.Attribute("r")?.Value;
                var columnIndex = ParseColumnIndex(reference, cells.Count);
                while (cells.Count <= columnIndex)
                {
                    cells.Add(string.Empty);
                }

                cells[columnIndex] = ExtractCellText(cellElement, sharedStrings);
            }

            if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                rows.Add(cells);
            }
        }

        return rows;
    }

    private static string ExtractCellText(XElement cellElement, IReadOnlyList<string> sharedStrings)
    {
        var cellType = cellElement.Attribute("t")?.Value;

        if (string.Equals(cellType, "inlineStr", StringComparison.Ordinal))
        {
            var inline = cellElement.Element(SpreadsheetNs + "is");
            return inline is null
                ? string.Empty
                : string.Concat(inline.Descendants(SpreadsheetNs + "t").Select(t => t.Value));
        }

        var rawValue = cellElement.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;

        if (string.Equals(cellType, "s", StringComparison.Ordinal))
        {
            return int.TryParse(rawValue, out var index) && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        return rawValue;
    }

    private static int ParseColumnIndex(string? cellReference, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return fallbackIndex;
        }

        var columnIndex = 0;
        var foundLetter = false;

        foreach (var ch in cellReference)
        {
            if (char.IsLetter(ch))
            {
                foundLetter = true;
                columnIndex = (columnIndex * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return foundLetter ? columnIndex - 1 : fallbackIndex;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
