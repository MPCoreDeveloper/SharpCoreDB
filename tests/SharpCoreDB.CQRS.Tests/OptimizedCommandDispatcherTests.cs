// <copyright file="OptimizedCommandDispatcherTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpDispatch;

/// <summary>
/// Unit tests for <see cref="OptimizedCommandDispatcher"/> and the
/// <see cref="DispatchServiceCollectionExtensions.AddOptimizedCommandDispatcher(IServiceCollection)"/> extension.
/// </summary>
public class OptimizedCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WithRegisteredSingletonHandler_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new ClassCommand("hello"), ct);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Message);
    }

    [Fact]
    public async Task DispatchAsync_WithStructCommand_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<StructCommand, StructCommandHandler>();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new StructCommand("struct-value"), ct);

        Assert.True(result.Success);
        Assert.Equal("struct-value", result.Message);
    }

    [Fact]
    public async Task DispatchAsync_WithoutRegisteredHandler_ReturnsFailWithHandlerName()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(new ClassCommand("missing"), ct);

        Assert.False(result.Success);
        Assert.Contains("No handler", result.Message);
        Assert.Contains(nameof(ClassCommand), result.Message);
    }

    [Fact]
    public async Task DispatchAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(new ClassCommand("cancelled"), cts.Token));
    }

    [Fact]
    public async Task DispatchAsync_ProducesSameResultAs_ServiceProviderDispatcher_ForClassCommand()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = new ClassCommand("parity-class");

        var spServices = new ServiceCollection();
        spServices.AddSharpCoreDBCqrs();
        spServices.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        using var spProvider = spServices.BuildServiceProvider();
        var spResult = await spProvider.GetRequiredService<ICommandDispatcher>()
            .DispatchAsync(command, ct);

        var optServices = new ServiceCollection();
        optServices.AddSharpCoreDBCqrs();
        optServices.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        optServices.AddOptimizedCommandDispatcher();
        using var optProvider = optServices.BuildServiceProvider();
        var optResult = await optProvider.GetRequiredService<ICommandDispatcher>()
            .DispatchAsync(command, ct);

        Assert.Equal(spResult.Success, optResult.Success);
        Assert.Equal(spResult.Message, optResult.Message);
    }

    [Fact]
    public async Task DispatchAsync_ProducesSameResultAs_ServiceProviderDispatcher_ForStructCommand()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = new StructCommand("parity-struct");

        var spServices = new ServiceCollection();
        spServices.AddSharpCoreDBCqrs();
        spServices.AddCommandHandler<StructCommand, StructCommandHandler>();
        using var spProvider = spServices.BuildServiceProvider();
        var spResult = await spProvider.GetRequiredService<ICommandDispatcher>()
            .DispatchAsync(command, ct);

        var optServices = new ServiceCollection();
        optServices.AddSharpCoreDBCqrs();
        optServices.AddCommandHandler<StructCommand, StructCommandHandler>();
        optServices.AddOptimizedCommandDispatcher();
        using var optProvider = optServices.BuildServiceProvider();
        var optResult = await optProvider.GetRequiredService<ICommandDispatcher>()
            .DispatchAsync(command, ct);

        Assert.Equal(spResult.Success, optResult.Success);
        Assert.Equal(spResult.Message, optResult.Message);
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleHandlerTypes_RoutesCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        services.AddCommandHandler<StructCommand, StructCommandHandler>();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        var classResult = await dispatcher.DispatchAsync(new ClassCommand("class"), ct);
        var structResult = await dispatcher.DispatchAsync(new StructCommand("struct"), ct);

        Assert.True(classResult.Success);
        Assert.Equal("class", classResult.Message);
        Assert.True(structResult.Success);
        Assert.Equal("struct", structResult.Message);
    }

    [Fact]
    public void AddOptimizedCommandDispatcher_ReplacesDefaultDispatcher_WithOptimizedType()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();
        services.AddCommandHandler<ClassCommand, ClassCommandHandler>();
        services.AddOptimizedCommandDispatcher();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        Assert.IsType<OptimizedCommandDispatcher>(dispatcher);
    }

    [Fact]
    public void AddSharpCoreDBCqrs_WithoutOptimized_RegistersServiceProviderDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDBCqrs();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

        Assert.IsType<ServiceProviderCommandDispatcher>(dispatcher);
    }

    private sealed record ClassCommand(string Value) : ICommand;

    private readonly record struct StructCommand(string Value) : ICommand;

    private sealed class ClassCommandHandler : ICommandHandler<ClassCommand>
    {
        public Task<CommandDispatchResult> HandleAsync(ClassCommand command, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CommandDispatchResult.Ok(command.Value));
        }
    }

    private sealed class StructCommandHandler : ICommandHandler<StructCommand>
    {
        public Task<CommandDispatchResult> HandleAsync(StructCommand command, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CommandDispatchResult.Ok(command.Value));
        }
    }
}
