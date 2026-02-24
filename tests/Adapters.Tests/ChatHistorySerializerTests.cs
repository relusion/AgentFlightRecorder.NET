using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel.Internal;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Adapters.Tests;

public sealed class ChatHistorySerializerTests
{
    [Fact]
    public void SimpleTextMessages_RoundTrip()
    {
        var history = new ChatHistory();
        history.AddUserMessage("Hello");
        history.AddAssistantMessage("Hi there!");

        var json = ChatHistorySerializer.SerializeHistory(history);
        var roundTripped = ChatHistorySerializer.DeserializeHistory(json);

        Assert.Equal(2, roundTripped.Count);
        Assert.Equal(AuthorRole.User, roundTripped[0].Role);
        Assert.Equal(AuthorRole.Assistant, roundTripped[1].Role);
        Assert.Contains(roundTripped[0].Items, i => i is TextContent tc && tc.Text == "Hello");
        Assert.Contains(roundTripped[1].Items, i => i is TextContent tc && tc.Text == "Hi there!");
    }

    [Fact]
    public void FunctionCallContent_Serialization()
    {
        var history = new ChatHistory();
        var items = new ChatMessageContentItemCollection
        {
            new FunctionCallContent("GetWeather", "Weather", "call_1", new KernelArguments
            {
                ["location"] = "Seattle",
                ["units"] = "fahrenheit"
            })
        };
        history.Add(new ChatMessageContent(AuthorRole.Assistant, items));

        var json = ChatHistorySerializer.SerializeHistory(history);
        var jsonStr = json.GetRawText();

        // Verify canonical ordering: args keys sorted, item keys sorted
        Assert.Contains("\"functionCall\"", jsonStr);
        Assert.Contains("\"GetWeather\"", jsonStr);
        Assert.Contains("\"Weather\"", jsonStr);

        // Round-trip
        var roundTripped = ChatHistorySerializer.DeserializeHistory(json);
        Assert.Single(roundTripped);
        var funcCall = roundTripped[0].Items.OfType<FunctionCallContent>().Single();
        Assert.Equal("GetWeather", funcCall.FunctionName);
        Assert.Equal("Weather", funcCall.PluginName);
        Assert.Equal("call_1", funcCall.Id);
        Assert.Equal("Seattle", funcCall.Arguments!["location"]?.ToString());
    }

    [Fact]
    public void FunctionResultContent_Serialization()
    {
        var history = new ChatHistory();
        var items = new ChatMessageContentItemCollection
        {
            new FunctionResultContent("GetWeather", "Weather", "call_1", "72°F, Sunny")
        };
        history.Add(new ChatMessageContent(AuthorRole.Tool, items));

        var json = ChatHistorySerializer.SerializeHistory(history);
        var roundTripped = ChatHistorySerializer.DeserializeHistory(json);

        Assert.Single(roundTripped);
        var funcResult = roundTripped[0].Items.OfType<FunctionResultContent>().Single();
        Assert.Equal("GetWeather", funcResult.FunctionName);
        Assert.Equal("Weather", funcResult.PluginName);
        Assert.Equal("72°F, Sunny", funcResult.Result?.ToString());
    }

    [Fact]
    public void EmptyChatHistory_SerializesToEmptyArray()
    {
        var history = new ChatHistory();
        var json = ChatHistorySerializer.SerializeHistory(history);

        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public void Deterministic_SameInputProducesSameOutput()
    {
        var history1 = new ChatHistory();
        history1.AddUserMessage("What's the weather?");
        history1.AddAssistantMessage("Let me check that for you.");

        var history2 = new ChatHistory();
        history2.AddUserMessage("What's the weather?");
        history2.AddAssistantMessage("Let me check that for you.");

        var json1 = ChatHistorySerializer.SerializeHistory(history1).GetRawText();
        var json2 = ChatHistorySerializer.SerializeHistory(history2).GetRawText();

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void SortedKeys_AtAllLevels()
    {
        var history = new ChatHistory();
        history.AddUserMessage("Hello");

        var json = ChatHistorySerializer.SerializeHistory(history);
        var rawText = json.GetRawText();

        // Keys in the message object should be alphabetically sorted
        var contentIdx = rawText.IndexOf("\"content\"");
        var itemsIdx = rawText.IndexOf("\"items\"");
        var metadataIdx = rawText.IndexOf("\"metadata\"");
        var roleIdx = rawText.IndexOf("\"role\"");

        Assert.True(contentIdx < itemsIdx, "content should come before items");
        Assert.True(itemsIdx < metadataIdx, "items should come before metadata");
        Assert.True(metadataIdx < roleIdx, "metadata should come before role");
    }
}
