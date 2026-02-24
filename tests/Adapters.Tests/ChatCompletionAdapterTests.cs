using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Events;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Adapters.Tests;

public sealed class ChatCompletionAdapterTests
{
    [Fact]
    public async Task GetChatMessageContentsAsync_TranslatesAndDelegates()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("Hello from the LLM!")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "test-model" });

        var history = new ChatHistory();
        history.AddUserMessage("Hi");

        var results = await adapter.GetChatMessageContentsAsync(history);

        Assert.Single(results);
        Assert.Equal("Hello from the LLM!", results[0].Content);
        Assert.Single(mockClient.ReceivedRequests);
        Assert.Equal("test-model", mockClient.ReceivedRequests[0].Model);
    }

    [Fact]
    public async Task Streaming_ReturnsBufferedResults()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("Streaming response")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "test-model" });

        var history = new ChatHistory();
        history.AddUserMessage("Hi");

        var streamedContents = new List<StreamingChatMessageContent>();
        await foreach (var content in adapter.GetStreamingChatMessageContentsAsync(history))
        {
            streamedContents.Add(content);
        }

        Assert.Single(streamedContents);
        Assert.Equal("Streaming response", streamedContents[0].Content);
    }

    [Fact]
    public async Task ProviderName_FromOptions()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("ok")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions
            {
                ProviderName = "AzureOpenAI",
                DefaultModelName = "gpt-4"
            });

        var history = new ChatHistory();
        history.AddUserMessage("test");
        await adapter.GetChatMessageContentsAsync(history);

        Assert.Equal("AzureOpenAI", mockClient.ReceivedRequests[0].Provider);
    }

    [Fact]
    public async Task ModelName_FromExecutionSettings()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("ok")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "default-model" });

        var history = new ChatHistory();
        history.AddUserMessage("test");

        var settings = new PromptExecutionSettings { ModelId = "override-model" };
        await adapter.GetChatMessageContentsAsync(history, settings);

        Assert.Equal("override-model", mockClient.ReceivedRequests[0].Model);
    }

    [Fact]
    public async Task ModelName_FallsBackToDefault()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("ok")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "default-model" });

        var history = new ChatHistory();
        history.AddUserMessage("test");
        await adapter.GetChatMessageContentsAsync(history);

        Assert.Equal("default-model", mockClient.ReceivedRequests[0].Model);
    }

    [Fact]
    public async Task ModelName_FallsBackToUnknown()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("ok")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions());

        var history = new ChatHistory();
        history.AddUserMessage("test");
        await adapter.GetChatMessageContentsAsync(history);

        Assert.Equal("unknown", mockClient.ReceivedRequests[0].Model);
    }

    [Fact]
    public async Task ToolCalls_InResponse_TranslatedToFunctionCallContent()
    {
        var toolCallsJson = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                functionName = "GetWeather",
                pluginName = "Weather",
                id = "call_1",
                args = new { location = "Seattle" }
            }
        });

        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement((string?)null),
            toolCallsJson));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "test-model" });

        var history = new ChatHistory();
        history.AddUserMessage("What's the weather?");
        var results = await adapter.GetChatMessageContentsAsync(history);

        Assert.Single(results);
        var funcCall = results[0].Items.OfType<FunctionCallContent>().Single();
        Assert.Equal("GetWeather", funcCall.FunctionName);
        Assert.Equal("Weather", funcCall.PluginName);
        Assert.Equal("call_1", funcCall.Id);
        Assert.Equal("Seattle", funcCall.Arguments!["location"]?.ToString());
    }

    [Fact]
    public async Task ToolDefinitions_SerializedFromKernel()
    {
        var mockClient = new MockLlmClient(new LlmResponse(
            JsonSerializer.SerializeToElement("ok")));

        var adapter = new SemanticKernelChatCompletionAdapter(
            mockClient,
            new SemanticKernelAdapterOptions { DefaultModelName = "test-model" });

        var builder = Kernel.CreateBuilder();
        var kernel = builder.Build();
        kernel.Plugins.AddFromFunctions("TestPlugin",
        [
            KernelFunctionFactory.CreateFromMethod(
                (string location) => $"72°F in {location}",
                "GetWeather",
                "Gets the weather for a location")
        ]);

        var history = new ChatHistory();
        history.AddUserMessage("test");
        await adapter.GetChatMessageContentsAsync(history, kernel: kernel);

        Assert.NotNull(mockClient.ReceivedRequests[0].ToolDefinitions);
    }

    private sealed class MockLlmClient : ILlmClient
    {
        private readonly LlmResponse _response;
        public List<LlmRequest> ReceivedRequests { get; } = [];

        public MockLlmClient(LlmResponse response)
        {
            _response = response;
        }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            ReceivedRequests.Add(request);
            return Task.FromResult(_response);
        }
    }
}
