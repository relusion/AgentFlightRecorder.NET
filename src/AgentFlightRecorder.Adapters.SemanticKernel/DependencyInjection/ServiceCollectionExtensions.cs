using AgentFlightRecorder.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlightRecorder.Adapters.SemanticKernel.DependencyInjection;

/// <summary>
/// Convenience extension methods for registering the Semantic Kernel adapter via IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the FlightRecorder Semantic Kernel adapter on the service collection.
    /// </summary>
    public static IServiceCollection AddFlightRecorderSemanticKernel(
        this IServiceCollection services,
        FlightRecorder flightRecorder,
        Action<SemanticKernelAdapterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(flightRecorder);

        var options = new SemanticKernelAdapterOptions();
        configure?.Invoke(options);

        KernelBuilderExtensions.RegisterServices(services, flightRecorder, options);

        return services;
    }
}
