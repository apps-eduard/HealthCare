using FluentAssertions;
using HealthCare.Mobile.Core.Navigation;

namespace HealthCare.Mobile.Tests;

public sealed class PatientRoutesTests
{
    [Theory]
    [InlineData("/home", true)]
    [InlineData("/profile", true)]
    [InlineData("/clinics", true)]
    [InlineData("/appointments", true)]
    [InlineData("/sign-in", false)]
    [InlineData("/register", false)]
    [InlineData("/connectivity", false)]
    [InlineData("/", false)]
    public void RequiresAuthentication_Matches_Foundation_Routes(string path, bool required)
    {
        PatientRoutes.RequiresAuthentication(path).Should().Be(required);
    }

    [Theory]
    [InlineData("home", "/home")]
    [InlineData("/home?x=1", "/home")]
    [InlineData("/home/", "/home")]
    [InlineData("", "/")]
    public void Normalize_Canonicalizes_Paths(string input, string expected)
    {
        PatientRoutes.Normalize(input).Should().Be(expected);
    }
}
