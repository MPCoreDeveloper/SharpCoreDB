// <copyright file="InMemoryCommandDispatcherTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using SharpDispatch;

namespace SharpCoreDB.CQRS.Tests;

/// <summary>
/// Unit tests for <see cref="InMemoryCommandDispatcher"/>.
/// </summary>
public class InMemoryCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_ReturnsSuccessfulResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new InMemoryCommandDispatcher();
        dispatcher.RegisterHandler(new TestCommandHandler());

        var result = await dispatcher.DispatchAsync(new TestCommand("ok"), cancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DispatchAsync_WithoutRegisteredHandler_ReturnsFailedResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new InMemoryCommandDispatcher();

        var result = await dispatcher.DispatchAsync(new TestCommand("no-handler"), cancellationToken);

        Assert.False(result.Success);
        Assert.Contains("No handler", result.Message);
    }

    private readonly record struct TestCommand(string Value) : ICommand;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<CommandDispatchResult> HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CommandDispatchResult.Ok(command.Value));
        }
    }
}
