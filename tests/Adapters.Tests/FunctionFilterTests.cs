using System.Text.Json;
using AgentFlightRecorder.Adapters.SemanticKernel;
using AgentFlightRecorder.Core;
using AgentFlightRecorder.Core.Models;
using Microsoft.SemanticKernel;

namespace Adapters.Tests;

public sealed class FunctionFilterTests
{
    [Fact]
    public async Task Filter_Intercepts_AndDelegatesToToolExecutor()
    {
        var mockExecutor = new MockToolExecutor(new ToolResult(
            JsonSerializer.SerializeToElement("72°F, Sunny")));

        var filter = new FlightRecorderFunctionFilter(
            mockExecutor,
            new SemanticKernelAdapterOptions { RecordFunctionCalls = true });

        var kernel = Kernel.CreateBuilder().Build();
        var function = KernelFunctionFactory.CreateFromMethod(
            () => "should not execute",
            "GetWeather",
            "Gets weather");

        kernel.Plugins.AddFromFunctions("Weather", [function]);

        // Create context for the filter
        var context = CreateAutoFunctionInvocationContext(kernel, function, new KernelArguments
        {
            ["location"] = "Seattle"
        });

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.False(nextCalled, "next() should NOT be called when RecordFunctionCalls is true");
        Assert.Single(mockExecutor.ReceivedInvocations);
        Assert.Contains("GetWeather", mockExecutor.ReceivedInvocations[0].ToolName);
        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task Filter_PassesThrough_WhenDisabled()
    {
        var mockExecutor = new MockToolExecutor(new ToolResult(
            JsonSerializer.SerializeToElement("unused")));

        var filter = new FlightRecorderFunctionFilter(
            mockExecutor,
            new SemanticKernelAdapterOptions { RecordFunctionCalls = false });

        var kernel = Kernel.CreateBuilder().Build();
        var function = KernelFunctionFactory.CreateFromMethod(
            () => "real result",
            "GetWeather");

        var context = CreateAutoFunctionInvocationContext(kernel, function, new KernelArguments());

        var nextCalled = false;
        await filter.OnAutoFunctionInvocationAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled, "next() SHOULD be called when RecordFunctionCalls is false");
        Assert.Empty(mockExecutor.ReceivedInvocations);
    }

    [Fact]
    public async Task Filter_TranslatesToolResult_ToFunctionResult()
    {
        var mockExecutor = new MockToolExecutor(new ToolResult(
            JsonSerializer.SerializeToElement("Weather result data")));

        var filter = new FlightRecorderFunctionFilter(
            mockExecutor,
            new SemanticKernelAdapterOptions { RecordFunctionCalls = true });

        var kernel = Kernel.CreateBuilder().Build();
        var function = KernelFunctionFactory.CreateFromMethod(
            () => "should not run",
            "GetWeather");

        var context = CreateAutoFunctionInvocationContext(kernel, function, new KernelArguments
        {
            ["city"] = "Portland"
        });

        await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

        Assert.NotNull(context.Result);
        var resultValue = context.Result.GetValue<string>();
        Assert.Equal("Weather result data", resultValue);
    }

    [Fact]
    public async Task Filter_ArgumentsSorted_InToolInvocation()
    {
        var mockExecutor = new MockToolExecutor(new ToolResult(
            JsonSerializer.SerializeToElement("ok")));

        var filter = new FlightRecorderFunctionFilter(
            mockExecutor,
            new SemanticKernelAdapterOptions { RecordFunctionCalls = true });

        var kernel = Kernel.CreateBuilder().Build();
        var function = KernelFunctionFactory.CreateFromMethod(
            () => "unused",
            "TestFunc");

        // Add arguments in non-alphabetical order
        var args = new KernelArguments
        {
            ["zebra"] = "z",
            ["alpha"] = "a",
            ["middle"] = "m"
        };

        var context = CreateAutoFunctionInvocationContext(kernel, function, args);
        await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

        var invocation = mockExecutor.ReceivedInvocations[0];
        var argsText = invocation.Args.GetRawText();

        // Keys should be alphabetically sorted
        var alphaIdx = argsText.IndexOf("\"alpha\"");
        var middleIdx = argsText.IndexOf("\"middle\"");
        var zebraIdx = argsText.IndexOf("\"zebra\"");

        Assert.True(alphaIdx < middleIdx, "alpha should come before middle");
        Assert.True(middleIdx < zebraIdx, "middle should come before zebra");
    }

    private static AutoFunctionInvocationContext CreateAutoFunctionInvocationContext(
        Kernel kernel,
        KernelFunction function,
        KernelArguments arguments)
    {
        // Use a ChatHistory to create a valid context
        var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
        chatHistory.AddUserMessage("test");

        // Create the context through the public constructor
        var chatMessage = new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant, "test");
        return new AutoFunctionInvocationContext(kernel, function, new FunctionResult(function, "initial"), chatHistory, chatMessage)
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
        };
    }

    private sealed class MockToolExecutor : IToolExecutor
    {
        private readonly ToolResult _result;
        public List<ToolInvocation> ReceivedInvocations { get; } = [];

        public MockToolExecutor(ToolResult result)
        {
            _result = result;
        }

        public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct)
        {
            ReceivedInvocations.Add(invocation);
            return Task.FromResult(_result);
        }
    }
}
