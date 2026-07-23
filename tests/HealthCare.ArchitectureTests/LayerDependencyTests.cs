using FluentAssertions;
using HealthCare.Application;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts;
using HealthCare.Domain;
using NetArchTest.Rules;

namespace HealthCare.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private const string DomainNamespace = "HealthCare.Domain";
    private const string ApplicationNamespace = "HealthCare.Application";
    private const string InfrastructureNamespace = "HealthCare.Infrastructure";
    private const string ApiNamespace = "HealthCare.Api";
    private const string ContractsNamespace = "HealthCare.Contracts";

    [Fact]
    public void Domain_Should_Not_Depend_On_Application_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(ApplicationAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_AspNetCore_HttpContext()
    {
        var result = Types.InAssembly(typeof(ApplicationAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.Http")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Contracts_Should_Not_Depend_On_Domain_Application_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(ContractsAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                DomainNamespace,
                ApplicationNamespace,
                InfrastructureNamespace,
                ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Controllers_Should_Not_Reference_DbContext_Directly()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespace("HealthCare.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("HealthCare.Infrastructure.Persistence")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Patient_Controllers_Should_Not_Implement_Tenant_Authorization_Directly()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespace("HealthCare.Api.Controllers")
            .And()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn("HealthCare.Infrastructure.Persistence.HealthCareDbContext")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));

        typeof(IPatientService).Namespace.Should().StartWith("HealthCare.Application.Patients");
        typeof(IPatientAccountLinker).Namespace.Should().StartWith("HealthCare.Application.Patients");
    }

    [Fact]
    public void Patient_Domain_Does_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(DomainAssemblyMarker).Assembly)
            .That()
            .ResideInNamespace("HealthCare.Domain.Patients")
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace, ApplicationNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Tenant_Access_Abstraction_Lives_In_Application()
    {
        typeof(ITenantAccessService).Namespace.Should().StartWith("HealthCare.Application");
        typeof(ICurrentUser).Namespace.Should().StartWith("HealthCare.Application");
        typeof(AuthorizationPolicies).Namespace.Should().StartWith("HealthCare.Application");
    }

    private static string Because(TestResult result)
    {
        if (result.FailingTypes is null || !result.FailingTypes.Any())
        {
            return "architecture rule failed";
        }

        return string.Join(", ", result.FailingTypes.Select(t => t.FullName));
    }
}
