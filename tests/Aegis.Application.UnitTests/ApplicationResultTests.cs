using Aegis.Application.Results;

namespace Aegis.Application.UnitTests;

public sealed class ApplicationResultTests
{
    [Fact]
    public void Failure_does_not_expose_value_or_secret()
    {
        var result = ApplicationResult<string>.Failure(ApplicationErrorCode.InvalidCredentials);
        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
        Assert.Null(result.Value);
        Assert.Null(result.ErrorMessage);
    }
}
