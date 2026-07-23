using FluentAssertions;
using HealthCare.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace HealthCare.UnitTests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplication_Should_Register_Without_Throwing()
    {
        var services = new ServiceCollection();

        var act = () => services.AddApplication();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddApplication_Returns_Same_ServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddApplication();

        result.Should().BeSameAs(services);
    }
}
