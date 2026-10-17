using Domain.Interfaces;
using FakeApiInfrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace FakeApiInfrastructure;

public static class FakeApiSetting
{
    public static void AddFakeApiInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IExampleTitleQuery, FakeApiQueryInfra>();
    }
}