using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Recording;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Replay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentFlightRecorder.Adapters.SemanticKernel.DependencyInjection;

/// <summary>
/// Extension methods for registering FlightRecorder integration with Semantic Kernel.
/// </summary>
public static class KernelBuilderExtensions
{
    /// <summary>
    /// Adds FlightRecorder integration to the Semantic Kernel builder.
    /// Replaces IChatCompletionService with the recording/replay adapter.
    /// </summary>
    public static IKernelBuilder AddFlightRecorder(
        this IKernelBuilder builder,
        FlightRecorder flightRecorder,
        Action<SemanticKernelAdapterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(flightRecorder);

        var options = new SemanticKernelAdapterOptions();
        configure?.Invoke(options);

        RegisterServices(builder.Services, flightRecorder, options);

        return builder;
    }

    internal static void RegisterServices(
        IServiceCollection services,
        FlightRecorder flightRecorder,
        SemanticKernelAdapterOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton<ICanonicalJsonSerializer, CanonicalJsonSerializer>();

        if (flightRecorder.Mode == FlightMode.Record)
        {
            // Capture the last IChatCompletionService before replacement
            var innerDescriptor = services.LastOrDefault(
                d => d.ServiceType == typeof(IChatCompletionService));
            if (innerDescriptor is not null)
                services.Remove(innerDescriptor);

            services.AddSingleton<IChatCompletionService>(sp =>
            {
                var serializer = sp.GetRequiredService<ICanonicalJsonSerializer>();

                ILlmClient innerLlmClient;
                var existingLlmClient = sp.GetService<ILlmClient>();
                if (existingLlmClient is not null)
                {
                    innerLlmClient = existingLlmClient;
                }
                else if (innerDescriptor is not null)
                {
                    var innerChat = ResolveFromDescriptor<IChatCompletionService>(sp, innerDescriptor);
                    innerLlmClient = new WrappedChatCompletionClient(innerChat);
                }
                else
                {
                    throw new InvalidOperationException(
                        "No ILlmClient or IChatCompletionService registered. " +
                        "Register a chat completion service (e.g., AddAzureOpenAIChatCompletion) " +
                        "or an ILlmClient before calling AddFlightRecorder.");
                }

                var recordingClient = new RecordingLlmClient(innerLlmClient, flightRecorder, serializer);
                return new SemanticKernelChatCompletionAdapter(recordingClient, options);
            });

            if (options.RecordFunctionCalls)
            {
                services.AddSingleton<IAutoFunctionInvocationFilter>(sp =>
                {
                    var serializer = sp.GetRequiredService<ICanonicalJsonSerializer>();

                    // Kernel unavailable during Build() — defer to first invocation
                    return new FlightRecorderFunctionFilter(kernel =>
                    {
                        var innerExecutor = new SemanticKernelToolExecutor(kernel);
                        return new RecordingToolExecutor(innerExecutor, flightRecorder, serializer);
                    }, options);
                });
            }
        }
        else if (flightRecorder.Mode == FlightMode.Replay)
        {
            services.AddSingleton<IChatCompletionService>(sp =>
            {
                var serializer = sp.GetRequiredService<ICanonicalJsonSerializer>();
                var replayIndex = sp.GetRequiredService<ReplayIndex>();

                var replayClient = new ReplayLlmClient(replayIndex, serializer);
                return new SemanticKernelChatCompletionAdapter(replayClient, options);
            });

            if (options.RecordFunctionCalls)
            {
                services.AddSingleton<IAutoFunctionInvocationFilter>(sp =>
                {
                    var serializer = sp.GetRequiredService<ICanonicalJsonSerializer>();
                    var replayIndex = sp.GetRequiredService<ReplayIndex>();

                    var replayExecutor = new ReplayToolExecutor(replayIndex, serializer);
                    return new FlightRecorderFunctionFilter(replayExecutor, options);
                });
            }
        }
    }

    private static T ResolveFromDescriptor<T>(IServiceProvider sp, ServiceDescriptor descriptor) where T : class
    {
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationInstance is T keyedInstance)
                return keyedInstance;
            if (descriptor.KeyedImplementationFactory is not null)
                return (T)descriptor.KeyedImplementationFactory(sp, descriptor.ServiceKey);
            if (descriptor.KeyedImplementationType is not null)
                return (T)ActivatorUtilities.CreateInstance(sp, descriptor.KeyedImplementationType);
        }
        else
        {
            if (descriptor.ImplementationInstance is T instance)
                return instance;
            if (descriptor.ImplementationFactory is not null)
                return (T)descriptor.ImplementationFactory(sp);
            if (descriptor.ImplementationType is not null)
                return (T)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Cannot resolve {typeof(T).Name} from the captured service descriptor.");
    }
}
