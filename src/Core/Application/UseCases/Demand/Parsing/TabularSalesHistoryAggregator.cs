using System.Globalization;
using System.Text;
using Application.DTOs.Demand;
using Domain.Entities;

namespace Application.UseCases.Demand.Parsing;

internal static class TabularSalesHistoryAggregator
{
    private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd-MM-yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm",
        "MM/dd/yyyy HH:mm:ss",
        "MM/dd/yyyy HH:mm"
    ];

    private static readonly string[] DateOnlyFormats =
    [
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "dd-MM-yyyy",
        "MM/dd/yyyy"
    ];

    internal static Result<ParsedDemandMatrixResult> Aggregate(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyFile", "O arquivo importado está vazio."));
        }

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var dataRows = rows.Skip(1).Where(r => r.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
        if (dataRows.Count == 0)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.NoDataRows", "O arquivo não contém linhas de dados após o cabeçalho."));
        }

        var volumeIndex = FindColumn(headers, "volume", "expectedvolume", "quantidade", "qtd", "qtde", "pedidos", "clientes", "atendimentos", "sales", "count", "demanda", "total");
        if (volumeIndex < 0)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.InvalidColumns", "Não foi possível identificar a coluna de volume/quantidade de vendas."));
        }

        var timestampIndex = FindColumn(headers, "datahora", "datetime", "timestamp", "createdat", "dataehora");
        var dateIndex = FindColumn(headers, "data", "date");
        var timeIndex = FindColumn(headers, "hora", "hour", "time", "horario");
        var dayOfWeekIndex = FindColumn(headers, "diasemana", "dayofweek", "weekday", "semana", "dia", "day");

        if (timestampIndex >= 0 || (dateIndex >= 0 && timeIndex >= 0))
        {
            return AggregateTransactional(dataRows, timestampIndex, dateIndex, timeIndex, volumeIndex);
        }

        if (dayOfWeekIndex >= 0 && timeIndex >= 0)
        {
            return AggregateWeeklyMatrix(dataRows, dayOfWeekIndex, timeIndex, volumeIndex);
        }

        return Result.Failure<ParsedDemandMatrixResult>(
            Error.Validation(
                "DemandImport.InvalidColumns",
                "Cabeçalho incompatível. Informe colunas (DataHora, Volume), (Data, Hora, Volume) ou (DiaSemana, Hora, Volume)."));
    }

    private static Result<ParsedDemandMatrixResult> AggregateTransactional(
        IReadOnlyList<IReadOnlyList<string>> dataRows,
        int timestampIndex,
        int dateIndex,
        int timeIndex,
        int volumeIndex)
    {
        var dailyHourTotals = new Dictionary<(DateOnly Date, int Hour), decimal>();
        var observedDatesByDayOfWeek = new Dictionary<DayOfWeek, HashSet<DateOnly>>();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var row = dataRows[i];
            var rowNumber = i + 2;

            var volumeResult = ParseVolume(GetCell(row, volumeIndex), rowNumber);
            if (volumeResult.IsFailure)
            {
                return Result.Failure<ParsedDemandMatrixResult>(volumeResult.Error);
            }

            DateOnly date;
            int hour;

            if (timestampIndex >= 0)
            {
                if (!TryParseDateTime(GetCell(row, timestampIndex), out var dateTime))
                {
                    return Result.Failure<ParsedDemandMatrixResult>(
                        Error.Validation("DemandImport.InvalidRow", $"Data/hora inválida na linha {rowNumber}."));
                }

                date = DateOnly.FromDateTime(dateTime);
                hour = dateTime.Hour;
            }
            else
            {
                if (!TryParseDateOnly(GetCell(row, dateIndex), out date))
                {
                    return Result.Failure<ParsedDemandMatrixResult>(
                        Error.Validation("DemandImport.InvalidRow", $"Data inválida na linha {rowNumber}."));
                }

                var hourResult = ParseHour(GetCell(row, timeIndex), rowNumber);
                if (hourResult.IsFailure)
                {
                    return Result.Failure<ParsedDemandMatrixResult>(hourResult.Error);
                }

                hour = hourResult.Value;
            }

            if (!observedDatesByDayOfWeek.TryGetValue(date.DayOfWeek, out var dates))
            {
                dates = [];
                observedDatesByDayOfWeek[date.DayOfWeek] = dates;
            }

            dates.Add(date);

            var key = (date, hour);
            dailyHourTotals[key] = dailyHourTotals.GetValueOrDefault(key) + volumeResult.Value;
        }

        var weeklySlots = dailyHourTotals
            .GroupBy(kvp => (kvp.Key.Date.DayOfWeek, kvp.Key.Hour))
            .Select(group =>
            {
                var occurrenceCount = observedDatesByDayOfWeek[group.Key.DayOfWeek].Count;
                var total = group.Sum(x => x.Value);
                var average = Math.Round(total / occurrenceCount, 2, MidpointRounding.AwayFromZero);
                return new ManualDemandEntryDto(group.Key.DayOfWeek, group.Key.Hour, average);
            })
            .OrderBy(slot => slot.DayOfWeek)
            .ThenBy(slot => slot.Hour)
            .ToList();

        return Result.Success(new ParsedDemandMatrixResult(weeklySlots, dataRows.Count, []));
    }

    private static Result<ParsedDemandMatrixResult> AggregateWeeklyMatrix(
        IReadOnlyList<IReadOnlyList<string>> dataRows,
        int dayOfWeekIndex,
        int hourIndex,
        int volumeIndex)
    {
        var consolidated = new Dictionary<(DayOfWeek DayOfWeek, int Hour), decimal>();
        var duplicateCount = 0;

        for (var i = 0; i < dataRows.Count; i++)
        {
            var row = dataRows[i];
            var rowNumber = i + 2;

            if (!TryParseDayOfWeek(GetCell(row, dayOfWeekIndex), out var dayOfWeek))
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.InvalidRow", $"Dia da semana inválido na linha {rowNumber}."));
            }

            var hourResult = ParseHour(GetCell(row, hourIndex), rowNumber);
            if (hourResult.IsFailure)
            {
                return Result.Failure<ParsedDemandMatrixResult>(hourResult.Error);
            }

            var volumeResult = ParseVolume(GetCell(row, volumeIndex), rowNumber);
            if (volumeResult.IsFailure)
            {
                return Result.Failure<ParsedDemandMatrixResult>(volumeResult.Error);
            }

            var key = (dayOfWeek, hourResult.Value);
            if (consolidated.ContainsKey(key))
            {
                duplicateCount++;
            }

            consolidated[key] = consolidated.GetValueOrDefault(key) + volumeResult.Value;
        }

        var warnings = duplicateCount > 0
            ? new[] { $"Foram consolidados {duplicateCount} registro(s) duplicado(s) para o mesmo dia e hora." }
            : Array.Empty<string>();

        var slots = consolidated
            .Select(kvp => new ManualDemandEntryDto(kvp.Key.DayOfWeek, kvp.Key.Hour, kvp.Value))
            .OrderBy(slot => slot.DayOfWeek)
            .ThenBy(slot => slot.Hour)
            .ToList();

        return Result.Success(new ParsedDemandMatrixResult(slots, dataRows.Count, warnings));
    }

    private static Result<decimal> ParseVolume(string raw, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<decimal>(
                Error.Validation("DemandImport.InvalidRow", $"Volume ausente na linha {rowNumber}."));
        }

        var trimmed = raw.Trim();
        decimal parsed;

        var lastComma = trimmed.LastIndexOf(',');
        var lastDot = trimmed.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            var culture = lastComma > lastDot ? PtBrCulture : CultureInfo.InvariantCulture;
            if (!decimal.TryParse(trimmed, NumberStyles.Number, culture, out parsed))
            {
                return Result.Failure<decimal>(
                    Error.Validation("DemandImport.InvalidRow", $"Volume numérico inválido na linha {rowNumber}."));
            }
        }
        else if (lastComma >= 0)
        {
            var normalized = trimmed.Replace(',', '.');
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            {
                return Result.Failure<decimal>(
                    Error.Validation("DemandImport.InvalidRow", $"Volume numérico inválido na linha {rowNumber}."));
            }
        }
        else if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return Result.Failure<decimal>(
                Error.Validation("DemandImport.InvalidRow", $"Volume numérico inválido na linha {rowNumber}."));
        }

        if (parsed < 0m)
        {
            return Result.Failure<decimal>(
                Error.Validation("DemandImport.NegativeVolume", $"Volume negativo não permitido na linha {rowNumber}."));
        }

        return Result.Success(parsed);
    }

    private static Result<int> ParseHour(string raw, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<int>(
                Error.Validation("DemandImport.InvalidHour", $"Hora ausente na linha {rowNumber}."));
        }

        var trimmed = raw.Trim();
        if (trimmed.EndsWith('h') || trimmed.EndsWith('H'))
        {
            trimmed = trimmed[..^1].Trim();
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericHour))
        {
            return numericHour is >= DemandSlot.MinHour and <= DemandSlot.MaxHour
                ? Result.Success(numericHour)
                : Result.Failure<int>(
                    Error.Validation("DemandImport.InvalidHour", $"Hora inválida ({numericHour}) na linha {rowNumber}."));
        }

        if (TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
        {
            return Result.Success(timeOnly.Hour);
        }

        return Result.Failure<int>(
            Error.Validation("DemandImport.InvalidHour", $"Formato de hora inválido na linha {rowNumber}."));
    }

    private static bool TryParseDateTime(string raw, out DateTime dateTime)
    {
        dateTime = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            return true;
        }

        if (DateTime.TryParse(trimmed, PtBrCulture, DateTimeStyles.None, out dateTime) ||
            DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
        {
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var oaDate) &&
            oaDate is >= 1d and < 2958466d)
        {
            dateTime = DateTime.FromOADate(oaDate);
            return true;
        }

        return false;
    }

    private static bool TryParseDateOnly(string raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (DateOnly.TryParseExact(trimmed, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateOnly.TryParse(trimmed, PtBrCulture, DateTimeStyles.None, out date) ||
            DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var oaDate) &&
            oaDate is >= 1d and < 2958466d)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(oaDate));
            return true;
        }

        return false;
    }

    private static bool TryParseDayOfWeek(string raw, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericDay))
        {
            if (Enum.IsDefined((DayOfWeek)numericDay))
            {
                dayOfWeek = (DayOfWeek)numericDay;
                return true;
            }

            return false;
        }

        var normalized = NormalizeHeader(trimmed);
        dayOfWeek = normalized switch
        {
            "dom" or "domingo" or "sun" or "sunday" => DayOfWeek.Sunday,
            "seg" or "segunda" or "segundafeira" or "mon" or "monday" => DayOfWeek.Monday,
            "ter" or "terca" or "tercafeira" or "tue" or "tuesday" => DayOfWeek.Tuesday,
            "qua" or "quarta" or "quartafeira" or "wed" or "wednesday" => DayOfWeek.Wednesday,
            "qui" or "quinta" or "quintafeira" or "thu" or "thursday" => DayOfWeek.Thursday,
            "sex" or "sexta" or "sextafeira" or "fri" or "friday" => DayOfWeek.Friday,
            "sab" or "sabado" or "sat" or "saturday" => DayOfWeek.Saturday,
            _ => (DayOfWeek)99
        };

        return Enum.IsDefined(dayOfWeek);
    }

    private static int FindColumn(IReadOnlyList<string> normalizedHeaders, params string[] aliases)
    {
        for (var i = 0; i < normalizedHeaders.Count; i++)
        {
            if (aliases.Contains(normalizedHeaders[i], StringComparer.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : string.Empty;

    internal static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}

