using Domain.Common;

namespace Domain.UnitTests.Common;

public class ResultTests
{
    private sealed class TestableResult : Result
    {
        public TestableResult(bool isSuccess, Error error)
            : base(isSuccess, error)
        {
        }
    }

    [Fact]
    public void Success_WhenCalled_ShouldReturnSuccessResultWithErrorNone()
    {
        // Arrange & Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_WhenCalledWithValidError_ShouldReturnFailureResultWithSpecifiedError()
    {
        // Arrange
        var error = Error.Validation("Test.Error", "Mensagem de erro de validação.");

        // Act
        var result = Result.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Failure_WhenCalledWithErrorNone_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var act = () => Result.Failure(Error.None);

        // Act & Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Combinação inválida de sucesso e erro.");
    }

    [Fact]
    public void Constructor_WhenSuccessWithNonEmptyError_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var error = Error.Failure("Test.Invalid", "Erro não permitido em sucesso.");
        var act = () => new TestableResult(true, error);

        // Act & Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Combinação inválida de sucesso e erro.");
    }

    [Fact]
    public void GenericSuccess_WhenCalledWithValue_ShouldReturnSuccessAndExposeValue()
    {
        // Arrange
        const int expectedValue = 42;

        // Act
        var result = Result.Success(expectedValue);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
        result.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void GenericFailure_WhenCalledWithError_ShouldReturnFailureAndThrowOnValueAccess()
    {
        // Arrange
        var error = Error.NotFound("Item.NotFound", "Item não localizado.");

        // Act
        var result = Result.Failure<string>(error);
        var accessValue = () => _ = result.Value;

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        accessValue.Should().Throw<InvalidOperationException>()
            .WithMessage("Não é possível acessar o valor de um resultado com falha.");
    }

    [Fact]
    public void ImplicitOperator_WhenValueIsNotNull_ShouldReturnSuccessResult()
    {
        // Arrange
        const string value = "Escala 5x2";

        // Act
        Result<string> result = value;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(value);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void ImplicitOperator_WhenValueIsNull_ShouldReturnFailureWithNullValueError()
    {
        // Arrange
        string? nullValue = null;

        // Act
        Result<string> result = nullValue;

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.NullValue);
    }
}

