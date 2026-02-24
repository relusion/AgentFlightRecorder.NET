using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AzureOpenAISample.Plugins;

public sealed class UtilityPlugin
{
    [KernelFunction("ConvertTemperature")]
    [Description("Converts temperature between Fahrenheit and Celsius.")]
    public string ConvertTemperature(
        [Description("Temperature value")] double value,
        [Description("Source unit (F or C)")] string fromUnit,
        [Description("Target unit (F or C)")] string toUnit)
    {
        var (converted, fromLabel, toLabel) = (fromUnit.ToUpperInvariant(), toUnit.ToUpperInvariant()) switch
        {
            ("F", "C") => ((value - 32) * 5 / 9, "°F", "°C"),
            ("C", "F") => (value * 9 / 5 + 32, "°C", "°F"),
            _ => (value, fromUnit, toUnit)
        };

        return $"{value:F1}{fromLabel} = {converted:F1}{toLabel}";
    }
}
