using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Integrity;
using AgentFlightRecorder.Core.Options;
using AgentFlightRecorder.Core.Redaction;
using AgentFlightRecorder.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentFlightRecorder.Core.DependencyInjection;

/// <summary>
/// Builder for configuring <see cref="FlightRecorder"/> via DI.
/// </summary>
public sealed class FlightRecorderBuilder
{
    private readonly IServiceCollection _services;
    private FlightMode _mode = FlightMode.Record;
    private BackpressureStrategy _backpressureStrategy = BackpressureStrategy.Drop;
    private int _channelCapacity = 1024;
    private TimeSpan _drainTimeout = TimeSpan.FromSeconds(30);
    private bool _synchronousMode;
    private bool _useIntegrity;
    private byte[]? _hmacSigningKey;
    private Action<RedactionBuilder>? _redactionConfigure;
    private Func<IServiceProvider, IFlightSink>? _sinkFactory;

    public FlightRecorderBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public FlightRecorderBuilder SetMode(FlightMode mode)
    {
        _mode = mode;
        return this;
    }

    public FlightRecorderBuilder UseSink(Func<IServiceProvider, IFlightSink> sinkFactory)
    {
        ArgumentNullException.ThrowIfNull(sinkFactory);
        _sinkFactory = sinkFactory;
        return this;
    }

    public FlightRecorderBuilder UseRedaction(Action<RedactionBuilder> configure)
    {
        _redactionConfigure = configure;
        return this;
    }

    public FlightRecorderBuilder UseIntegrity()
    {
        _useIntegrity = true;
        return this;
    }

    public FlightRecorderBuilder UseHmacSigning(byte[] signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        _hmacSigningKey = signingKey;
        _useIntegrity = true;
        return this;
    }

    public FlightRecorderBuilder ConfigureBackpressure(BackpressureStrategy strategy, int capacity = 1024)
    {
        _backpressureStrategy = strategy;
        _channelCapacity = capacity;
        return this;
    }

    public FlightRecorderBuilder UseSynchronousMode(bool enabled = true)
    {
        _synchronousMode = enabled;
        return this;
    }

    internal void Build()
    {
        _services.AddSingleton<ICanonicalJsonSerializer, CanonicalJsonSerializer>();

        if (_useIntegrity)
        {
            _services.AddSingleton<IIntegrityProvider, Sha256IntegrityProvider>();
        }

        _services.AddSingleton(sp =>
        {
            var serializer = sp.GetRequiredService<ICanonicalJsonSerializer>();
            var logger = sp.GetService<ILogger<FlightRecorder>>();

            IRedactor? redactor = null;
            if (_redactionConfigure is not null)
            {
                var redactionBuilder = new RedactionBuilder();
                _redactionConfigure(redactionBuilder);
                redactor = redactionBuilder.Build();
            }

            IIntegrityProvider? integrityProvider = _useIntegrity
                ? sp.GetService<IIntegrityProvider>()
                : null;

            var sink = _sinkFactory?.Invoke(sp)
                       ?? throw new InvalidOperationException("No sink configured. Call UseSink() on the builder.");

            return new FlightRecorderOptions
            {
                Mode = _mode,
                Sink = sink,
                Redactor = redactor,
                IntegrityProvider = integrityProvider,
                Logger = logger,
                SynchronousMode = _synchronousMode,
                HmacSigningKey = _hmacSigningKey,
                Backpressure = new BackpressureOptions
                {
                    Strategy = _backpressureStrategy,
                    ChannelCapacity = _channelCapacity,
                    DrainTimeout = _drainTimeout
                }
            };
        });

        _services.AddSingleton<FlightRecorder>();
        _services.AddSingleton<IFlightRecorder>(sp => sp.GetRequiredService<FlightRecorder>());
    }
}

/// <summary>
/// Builder for configuring redaction pipeline.
/// </summary>
public sealed class RedactionBuilder
{
    private readonly List<IRedactor> _redactors = [];

    public RedactionBuilder AddApiKeyRedactor()
    {
        _redactors.Add(new ApiKeyRedactor());
        return this;
    }

    public RedactionBuilder AddPiiRedactor()
    {
        _redactors.Add(new PiiRedactor());
        return this;
    }

    public RedactionBuilder AddCustomRedactor(IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        _redactors.Add(redactor);
        return this;
    }

    internal IRedactor Build() =>
        _redactors.Count == 1 ? _redactors[0] : new CompositeRedactor(_redactors);
}
