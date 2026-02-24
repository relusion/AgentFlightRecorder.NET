using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SemanticKernelSample;

/// <summary>
/// Sample tool functions for demonstrating recording and replay.
/// Returns deterministic data — no real APIs called.
/// </summary>
internal sealed class SampleTools
{
    [KernelFunction("GetWeather")]
    [Description("Gets the current weather for a location.")]
    public string GetWeather(
        [Description("The city to get weather for")] string location,
        [Description("API key for weather service")] string? apiKey = null)
    {
        // Deterministic response
        return $"72°F and sunny in {location}";
    }

    [KernelFunction("GetCurrentTime")]
    [Description("Gets the current time in a timezone.")]
    public string GetCurrentTime(
        [Description("The timezone abbreviation")] string timezone)
    {
        // Deterministic response
        return $"2:30 PM {timezone}";
    }
}
