using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace HealthCare.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
