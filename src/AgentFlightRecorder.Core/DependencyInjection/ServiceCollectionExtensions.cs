using Microsoft.Extensions.DependencyInjection;

namespace AgentFlightRecorder.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering AgentFlightRecorder services.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlightRecorder(
        this IServiceCollection services,
        Action<FlightRecorderBuilder> configure)
    {
        var builder = new FlightRecorderBuilder(services);
        configure(builder);
        builder.Build();
        return services;
    }
}
