// <copyright file="ServiceProviderCommandDispatcherTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpDispatch;

/// <summary>
/// Unit tests for <see cref="ServiceProviderCommandDispatcher"/> and CQRS DI extensions.
/// </summary>
public class ServiceProviderCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WithRegisteredHandlerInDi_ReturnsSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<TestCommand, TestCommandHandler>();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new TestCommand("ok"), cancellationToken);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DispatchAsync_WithoutHandlerInDi_ReturnsFail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new TestCommand("missing"), cancellationToken);

        Assert.False(result.Success);
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
