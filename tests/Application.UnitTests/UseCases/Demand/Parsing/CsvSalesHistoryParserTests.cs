using System.Text;
using Application.UseCases.Demand.Parsing;

namespace Application.UnitTests.UseCases.Demand.Parsing;

public class CsvSalesHistoryParserTests
{
    private readonly CsvSalesHistoryParser _parser = new();

    private static MemoryStream CreateUtf8Stream(string content, bool includeBom = false)
    {
        var preamble = includeBom ? Encoding.UTF8.GetPreamble() : [];
        var body = Encoding.UTF8.GetBytes(content);
        var buffer = new byte[preamble.Length + body.Length];
        preamble.CopyTo(buffer, 0);
        body.CopyTo(buffer, preamble.Length);
        return new MemoryStream(buffer);
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
    public void Parse_WhenStreamIsEmpty_ShouldReturnValidationFailure()
    {
        // Arrange
        using var stream = CreateUtf8Stream("   \r\n  ");

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.EmptyFile");
    }

    [Fact]
    public void Parse_WhenTransactionalCsvWithSemicolonAndMultiWeekDates_ShouldAverageByDayOfWeekAndHour()
    {
        // Arrange
        // 2026-10-05 e 2026-10-12 são segundas-feiras; 2026-10-09 é sexta-feira.
        var csv = """
            DataHora;Pedidos;Observacao
            05/10/2026 19:10;15;"Mesa 1, Salão"
            05/10/2026 19:45;25;"Balcão"
            12/10/2026 19:20;60;"Salão"
            09/10/2026 20:00;120,50;"Pico sexta"
            """;
        using var stream = CreateUtf8Stream(csv, includeBom: true);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RawRowsProcessed.Should().Be(4);
        result.Value.Slots.Should().HaveCount(2);

        var mondaySlot = result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Monday && s.Hour == 19);
        mondaySlot.ExpectedVolume.Should().Be(50.00m); // (40 + 60) / 2 segundas-feiras

        var fridaySlot = result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Friday && s.Hour == 20);
        fridaySlot.ExpectedVolume.Should().Be(120.50m);
    }

    [Fact]
    public void Parse_WhenTransactionalCsvUsesSeparateDateAndTimeColumnsWithComma_ShouldParseCorrectly()
    {
        // Arrange
        var csv = """
            Date,Time,Count
            2026-10-10,14:15,32.5
            2026-10-10,14:50,17.5
            """;
        using var stream = CreateUtf8Stream(csv);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RawRowsProcessed.Should().Be(2);
        var slot = result.Value.Slots.Should().ContainSingle().Subject;
        slot.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        slot.Hour.Should().Be(14);
        slot.ExpectedVolume.Should().Be(50.00m);
    }

    [Fact]
    public void Parse_WhenWeeklyMatrixCsvWithPortugueseAndNumericDays_ShouldConsolidateSlots()
    {
        // Arrange
        var csv = """
            DiaSemana;Hora;Volume
            Segunda-feira;12:00;45
            Sáb;21h;150,25
            Sunday;13;80
            5;19;95.75
            Segunda;12;5
            """;
        using var stream = CreateUtf8Stream(csv);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RawRowsProcessed.Should().Be(5);
        result.Value.Slots.Should().HaveCount(4);
        result.Value.Warnings.Should().ContainSingle(w => w.Contains("duplicado", StringComparison.OrdinalIgnoreCase));

        result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Monday && s.Hour == 12)
            .ExpectedVolume.Should().Be(50m);
        result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Saturday && s.Hour == 21)
            .ExpectedVolume.Should().Be(150.25m);
        result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Sunday && s.Hour == 13)
            .ExpectedVolume.Should().Be(80m);
        result.Value.Slots.Single(s => s.DayOfWeek == DayOfWeek.Friday && s.Hour == 19)
            .ExpectedVolume.Should().Be(95.75m);
    }

    [Fact]
    public void Parse_WhenOnlyHeaderExists_ShouldReturnValidationFailure()
    {
        // Arrange
        using var stream = CreateUtf8Stream("DataHora;Volume\r\n");

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.NoDataRows");
    }

    [Fact]
    public void Parse_WhenRequiredColumnsAreMissing_ShouldReturnValidationFailure()
    {
        // Arrange
        using var stream = CreateUtf8Stream("Produto;Preco\r\nPrato;45,00");

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DemandImport.InvalidColumns");
    }

    [Theory]
    [InlineData("DiaSemana;Hora;Volume\r\nSegunda;24;10", "DemandImport.InvalidHour")]
    [InlineData("DiaSemana;Hora;Volume\r\nSegunda;12;-5", "DemandImport.NegativeVolume")]
    [InlineData("DiaSemana;Hora;Volume\r\nFeriado;12;10", "DemandImport.InvalidRow")]
    [InlineData("DataHora;Volume\r\n31/02/2026 12:00;10", "DemandImport.InvalidRow")]
    public void Parse_WhenRowContainsInvalidValues_ShouldReturnValidationFailure(string csv, string expectedCode)
    {
        // Arrange
        using var stream = CreateUtf8Stream(csv);

        // Act
        var result = _parser.Parse(stream);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }
}

