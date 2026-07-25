using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Web.ClinicDashboard;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicDashboardProblemMessagesTests
{
    [Theory]
    [InlineData(ClinicDashboardErrorCodes.AccessDenied, "You do not have permission to view the clinic dashboard.")]
    [InlineData(ClinicDashboardErrorCodes.ClinicScopeRequired, "Select a clinic before loading the clinic dashboard.")]
    [InlineData(ClinicDashboardErrorCodes.ClinicNotFound, "Clinic was not found.")]
    public void Maps_Known_Error_Codes(string errorCode, string expected)
    {
        var ex = new ApiProblemException(403, "Forbidden", "detail", errorCode);
        ClinicDashboardProblemMessages.ToUserMessage(ex).Should().Be(expected);
    }

    [Fact]
    public void Falls_Back_Safely_Without_Raw_Exception_Text()
    {
        var ex = new ApiProblemException(500, "Unexpected", "at HealthCare.Infrastructure.Foo", "unknown.code");
        var message = ClinicDashboardProblemMessages.ToUserMessage(ex);
        message.Should().Be("Unexpected");
        message.Should().NotContain("Infrastructure");
        message.Should().NotContain("at HealthCare");
    }
}
