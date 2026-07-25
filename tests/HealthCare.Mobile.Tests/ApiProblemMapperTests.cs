using System.Net;
using FluentAssertions;
using HealthCare.Mobile.Core.Api;

namespace HealthCare.Mobile.Tests;

public sealed class ApiProblemMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorKind.Server)]
    public void FromStatusCode_Maps_Standard_Kinds(HttpStatusCode status, ApiErrorKind kind)
    {
        var problem = ApiProblemMapper.FromStatusCode(status);
        problem.Kind.Should().Be(kind);
        problem.UserMessage.Should().NotBeNullOrWhiteSpace();
        problem.UserMessage.Should().NotContain("Exception");
        problem.UserMessage.Should().NotContain("SQL");
    }

    [Fact]
    public void FromStatusCode_Maps_Validation_Errors_Without_Raw_Body_Leak()
    {
        var body = """
                   {
                     "title": "Validation failed",
                     "detail": "One or more validation errors occurred.",
                     "errorCode": "validation.failed",
                     "errors": { "Email": ["Required"] }
                   }
                   """;

        var problem = ApiProblemMapper.FromStatusCode(HttpStatusCode.BadRequest, body);

        problem.Kind.Should().Be(ApiErrorKind.Validation);
        problem.ErrorCode.Should().Be("validation.failed");
        problem.ValidationErrors.Should().ContainKey("Email");
        problem.UserMessage.Should().NotContain(body);
    }

    [Fact]
    public void FromStatusCode_Ignores_Unparseable_Bodies()
    {
        var problem = ApiProblemMapper.FromStatusCode(HttpStatusCode.BadGateway, "<<<not-json>>>");
        problem.Kind.Should().Be(ApiErrorKind.Server);
        problem.Detail.Should().BeNull();
    }

    [Fact]
    public void FromException_Maps_Network_And_Timeout()
    {
        ApiProblemMapper.FromException(new HttpRequestException("down")).Kind.Should().Be(ApiErrorKind.Network);
        ApiProblemMapper.FromException(new TaskCanceledException()).Kind.Should().Be(ApiErrorKind.Timeout);
    }
}
