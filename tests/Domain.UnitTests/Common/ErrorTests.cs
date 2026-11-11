using Domain.Common;

namespace Domain.UnitTests.Common;

public class ErrorTests
{
    [Fact]
    public void None_WhenAccessed_ShouldHaveEmptyCodeAndMessage()
    {
        // Arrange & Act
        var error = Error.None;

        // Assert
        error.Code.Should().BeEmpty();
        error.Message.Should().BeEmpty();
    }

    [Fact]
    public void NullValue_WhenAccessed_ShouldHaveExpectedCodeAndMessage()
    {
        // Arrange & Act
        var error = Error.NullValue;

        // Assert
        error.Code.Should().Be("Error.NullValue");
        error.Message.Should().Be("Valor nulo não permitido.");
    }

    [Fact]
    public void Validation_WhenCalled_ShouldCreateErrorWithProvidedCodeAndMessage()
    {
        // Arrange
        const string code = "Employee.InvalidName";
        const string message = "O nome do colaborador é obrigatório.";

        // Act
        var error = Error.Validation(code, message);

        // Assert
        error.Code.Should().Be(code);
        error.Message.Should().Be(message);
    }

    [Fact]
    public void NotFound_WhenCalled_ShouldCreateErrorWithProvidedCodeAndMessage()
    {
        // Arrange
        const string code = "Company.NotFound";
        const string message = "Estabelecimento não encontrado.";

        // Act
        var error = Error.NotFound(code, message);

        // Assert
        error.Code.Should().Be(code);
        error.Message.Should().Be(message);
    }

    [Fact]
    public void Conflict_WhenCalled_ShouldCreateErrorWithProvidedCodeAndMessage()
    {
        // Arrange
        const string code = "Shift.OverlapConflict";
        const string message = "Conflito de horário detectado para o turno.";

        // Act
        var error = Error.Conflict(code, message);

        // Assert
        error.Code.Should().Be(code);
        error.Message.Should().Be(message);
    }

    [Fact]
    public void Failure_WhenCalled_ShouldCreateErrorWithProvidedCodeAndMessage()
    {
        // Arrange
        const string code = "Roster.GenerationFailed";
        const string message = "Não foi possível gerar a escala com as restrições informadas.";

        // Act
        var error = Error.Failure(code, message);

        // Assert
        error.Code.Should().Be(code);
        error.Message.Should().Be(message);
    }

    [Fact]
    public void Equality_TwoErrorsWithSameCodeAndMessage_ShouldBeEqual()
    {
        // Arrange
        var error1 = new Error("Test.Code", "Test message");
        var error2 = Error.Validation("Test.Code", "Test message");

        // Act & Assert
        error1.Should().Be(error2);
        (error1 == error2).Should().BeTrue();
    }
}

