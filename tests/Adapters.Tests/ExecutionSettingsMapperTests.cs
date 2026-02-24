using AgentFlightRecorder.Adapters.SemanticKernel.Internal;
using Microsoft.SemanticKernel;

namespace Adapters.Tests;

public sealed class ExecutionSettingsMapperTests
{
    [Fact]
    public void NullSettings_ReturnsNull()
    {
        var result = ExecutionSettingsMapper.MapToSettings(null);
        Assert.Null(result);
    }

    [Fact]
    public void NullDictionary_ReturnsNull()
    {
        var result = ExecutionSettingsMapper.MapFromSettings(null);
        Assert.Null(result);
    }

    [Fact]
    public void ModelId_Extraction()
    {
        var settings = new PromptExecutionSettings { ModelId = "gpt-4" };
        var dict = ExecutionSettingsMapper.MapToSettings(settings);

        Assert.NotNull(dict);
        Assert.Equal("gpt-4", dict["modelId"]);
    }

    [Fact]
    public void ExtensionData_Extraction()
    {
        var settings = new PromptExecutionSettings
        {
            ModelId = "gpt-4",
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = 0.7,
                ["max_tokens"] = 1000,
            }
        };

        var dict = ExecutionSettingsMapper.MapToSettings(settings);

        Assert.NotNull(dict);
        Assert.Equal("gpt-4", dict["modelId"]);
        Assert.Equal(0.7, dict["temperature"]);
        Assert.Equal(1000, dict["max_tokens"]);
    }

    [Fact]
    public void RoundTrip_Fidelity()
    {
        var original = new PromptExecutionSettings
        {
            ModelId = "gpt-4",
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = 0.7,
            }
        };

        var dict = ExecutionSettingsMapper.MapToSettings(original);
        var restored = ExecutionSettingsMapper.MapFromSettings(dict);

        Assert.NotNull(restored);
        Assert.Equal("gpt-4", restored.ModelId);
        Assert.NotNull(restored.ExtensionData);
        Assert.Equal(0.7, restored.ExtensionData["temperature"]);
    }

    [Fact]
    public void EmptySettings_ReturnsNull()
    {
        var settings = new PromptExecutionSettings();
        var result = ExecutionSettingsMapper.MapToSettings(settings);

        Assert.Null(result);
    }
}
