using System.Text;
using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Adapters.SemanticKernel.DependencyInjection;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.DependencyInjection;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Serialization;
using AgentFlightRecorder.Sinks.Jsonl;
using AzureOpenAISample.Plugins;
using AzureOpenAISample.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

Console.WriteLine("=== AgentFlightRecorder.NET — Azure OpenAI Sample ===");
Console.WriteLine();

// ============================================================
// Configuration: environment variables + appsettings.json
// ============================================================
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(Program).Assembly)
    .AddEnvironmentVariables()
    .Build();

var endpoint = config["AZURE_OPENAI_ENDPOINT"];
var apiKey = config["AZURE_OPENAI_API_KEY"];
var deploymentName = config["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";
var hmacKeyString = config["TRACE_HMAC_KEY"] ?? "sample-hmac-signing-key-for-demo";

// Validate required credentials
var missing = new List<string>();
if (string.IsNullOrWhiteSpace(endpoint)) missing.Add("AZURE_OPENAI_ENDPOINT");
if (string.IsNullOrWhiteSpace(apiKey)) missing.Add("AZURE_OPENAI_API_KEY");

if (missing.Count > 0)
{
    Console.Error.WriteLine("ERROR: Missing required environment variables:");
    foreach (var v in missing)
        Console.Error.WriteLine($"  - {v}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Setup instructions:");
    Console.Error.WriteLine("  1. Create an Azure OpenAI resource in the Azure portal");
    Console.Error.WriteLine("  2. Deploy a model (e.g., gpt-4o-mini)");
    Console.Error.WriteLine("  3. Set user secrets (from the AzureOpenAISample directory):");
    Console.Error.WriteLine("     dotnet user-secrets set \"AZURE_OPENAI_ENDPOINT\" \"https://your-resource.openai.azure.com/\"");
    Console.Error.WriteLine("     dotnet user-secrets set \"AZURE_OPENAI_API_KEY\" \"your-api-key\"");
    Console.Error.WriteLine("     Optionally set AZURE_OPENAI_DEPLOYMENT_NAME in appsettings.json (defaults to gpt-4o-mini)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("For an offline sample that requires no credentials, see samples/SemanticKernelSample.");
    return 1;
}

var traceFile = Path.Combine(Path.GetTempPath(), $"azure-sample-{Guid.NewGuid():N}.jsonl");
var hmacKeyBytes = Encoding.UTF8.GetBytes(hmacKeyString);

// ============================================================
// DI Setup: FlightRecorder via ServiceCollection
// (Demonstrates FlightRecorderBuilder fluent API)
// ============================================================
// For hosted applications, use Host.CreateDefaultBuilder().ConfigureServices() instead.
var services = new ServiceCollection();

services.AddFlightRecorder(builder => builder
    .SetMode(FlightMode.Record)
    .UseSink(sp => new JsonlFileSink(traceFile, sp.GetRequiredService<ICanonicalJsonSerializer>()))
    .UseRedaction(r => r.AddApiKeyRedactor().AddPiiRedactor())
    .UseHmacSigning(hmacKeyBytes)
    .UseIntegrity()
    .ConfigureBackpressure(BackpressureStrategy.Drop, 512));

await using var serviceProvider = services.BuildServiceProvider();
var recorder = serviceProvider.GetRequiredService<FlightRecorder>();
var serializer = serviceProvider.GetRequiredService<ICanonicalJsonSerializer>();

// ============================================================
// Kernel Setup: Azure OpenAI + FlightRecorder integration
// ============================================================
var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.AddAzureOpenAIChatCompletion(deploymentName, endpoint!, apiKey!);
kernelBuilder.Plugins.AddFromType<WeatherPlugin>();
kernelBuilder.Plugins.AddFromType<UtilityPlugin>();

// Wire FlightRecorder into the Kernel — replaces IChatCompletionService
// with the recording adapter, and registers FlightRecorderFunctionFilter.
kernelBuilder.AddFlightRecorder(recorder, options =>
{
    options.ProviderName = "AzureOpenAI";
    options.DefaultModelName = deploymentName;
    options.RecordFunctionCalls = true;
});

var kernel = kernelBuilder.Build();

Console.WriteLine($"Deployment: {deploymentName}");
Console.WriteLine($"Trace file: {traceFile}");
Console.WriteLine();

try
{
    // ============================================================
    // Phase 1: RECORD — Multi-turn conversation with Azure OpenAI
    // ============================================================
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine("  Phase 1: RECORD");
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine();

    var recordedOutputs = await RecordScenario.RunAsync(kernel, recorder, serializer);

    Console.WriteLine();
    Console.WriteLine($"  Recorded {recordedOutputs.Count} assistant responses.");
    Console.WriteLine($"  Trace written to: {traceFile}");
    Console.WriteLine();

    // ============================================================
    // Phase 2: REPLAY — Replay from recorded trace (offline)
    // ============================================================
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine("  Phase 2: REPLAY");
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine();
    // In test projects, FlightRecorderFixture automates this setup.
    // Production apps use AddFlightRecorder with Replay mode for test environments.

    var replayedOutputs = await ReplayScenario.RunAsync(traceFile, serializer, deploymentName);

    Console.WriteLine();
    Console.WriteLine($"  Replayed {replayedOutputs.Count} assistant responses.");
    Console.WriteLine();

    // ============================================================
    // Phase 3: VERIFY — Integrity, HMAC, and output comparison
    // ============================================================
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine("  Phase 3: VERIFY");
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine();

    await VerifyScenario.RunAsync(traceFile, serializer, hmacKeyBytes, replayedOutputs);
}
catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {httpEx.Message}");
    Console.Error.WriteLine("  → Check that AZURE_OPENAI_API_KEY is valid and not expired.");
    return 1;
}
catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {httpEx.Message}");
    Console.Error.WriteLine($"  → Check that deployment '{deploymentName}' exists in your Azure OpenAI resource.");
    return 1;
}
catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {httpEx.Message}");
    Console.Error.WriteLine("  → Rate limit exceeded. Wait a moment and try again.");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
finally
{
    try { if (File.Exists(traceFile)) File.Delete(traceFile); }
    catch { /* Best-effort cleanup of temp file */ }
}

Console.WriteLine();
Console.WriteLine("=== All phases completed successfully ===");
return 0;
