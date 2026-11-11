using Application.Common;

namespace Application.UnitTests.Common;

public class ResultApplicationTests
{
    [Fact]
    public void ApplicationCommon_WhenUsingResultAndError_ShouldResolveTypesCorrectly()
    {
        // Arrange
        var error = Error.Conflict("Shift.Conflict", "Turno em conflito.");

        // Act
        var successResult = Result.Success("OK");
        var failureResult = Result.Failure<string>(error);

        // Assert
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value.Should().Be("OK");
        failureResult.IsFailure.Should().BeTrue();
        failureResult.Error.Should().Be(error);
    }
}

