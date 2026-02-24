using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AzureOpenAISample.Plugins;

public sealed class WeatherPlugin
{
    [KernelFunction("GetWeather")]
    [Description("Gets the current weather for a location.")]
    public string GetWeather(
        [Description("City name")] string location,
        [Description("API key for premium features (optional)")] string? apiKey = null)
    {
        // Returns deterministic data with PII-triggering content.
        // The email triggers PiiRedactor; apiKey parameter triggers ApiKeyRedactor.
        return $"72°F and sunny in {location}. Humidity: 45%. " +
               $"Contact weather@example.com for premium alerts. " +
               (apiKey is not null ? $"Premium key: {apiKey}" : "No premium key provided.");
    }

    [KernelFunction("SearchLocation")]
    [Description("Searches for location information including tourism details.")]
    public string SearchLocation(
        [Description("Search query")] string query)
    {
        // Returns location info with phone number (PII trigger).
        return $"Found: {query} — Population: 737,015. " +
               "Tourism office: 555-0199. " +
               "Visit coordinator: tourism@seattle-gov.example.com";
    }
}
