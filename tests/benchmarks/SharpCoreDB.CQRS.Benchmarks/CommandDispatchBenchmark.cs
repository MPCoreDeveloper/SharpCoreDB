// <copyright file="CommandDispatchBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS.Benchmarks;

using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Compares dispatch overhead across the three available <see cref="ICommandDispatcher"/> implementations.
/// Tests both reference-type (class) and value-type (struct) commands to surface boxing differences.
/// </summary>
/// <remarks>
/// Run with: <c>dotnet run -c Release -- --filter *CommandDispatch*</c>
/// Expected results (approximate, Release build, x64):
/// <list type="table">
///   <listheader><term>Benchmark</term><description>~ns / op | Alloc</description></listheader>
///   <item><term>ServiceProvider class</term><description>50–150 ns | 0 B (singleton cached by DI)</description></item>
///   <item><term>Optimized class</term><description>10–15 ns  | 0 B</description></item>
///   <item><term>InMemory class</term><description>25–50 ns  | 0 B</description></item>
///   <item><term>ServiceProvider struct</term><description>50–150 ns | 0 B</description></item>
///   <item><term>Optimized struct</term><description>10–15 ns  | 0 B (no boxing via InvokeTyped)</description></item>
///   <item><term>InMemory struct</term><description>25–50 ns  | 0 B</description></item>
/// </list>
/// Handler returns a static pre-allocated <see cref="Task{T}"/> so handler overhead is
/// excluded from the measurement; all allocations shown are purely from the dispatcher layer.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class CommandDispatchBenchmark
{
    private ICommandDispatcher _serviceProviderDispatcher = null!;
    private ICommandDispatcher _optimizedDispatcher = null!;
    private ICommandDispatcher _inMemoryDispatcher = null!;

    // Static command instances — no per-iteration allocation from command construction.
    private static readonly ClassCommand ClassCmd = new("bench");
    private static readonly StructCommand StructCmd = new("bench");

    [GlobalSetup]
    public void Setup()
    {
        // ── Baseline: ServiceProviderCommandDispatcher ────────────────────────
        var spServices = new ServiceCollection();
        spServices.AddSharpCoreDBCqrs();
        spServices.AddCommandHandler<ClassCommand, NoOpClassHandler>();
        spServices.AddCommandHandler<StructCommand, NoOpStructHandler>();
        _serviceProviderDispatcher = spServices.BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

        // ── Optimized: FrozenDictionary + pre-built typed delegates ───────────
        var optServices = new ServiceCollection();
        optServices.AddSharpCoreDBCqrs();
        optServices.AddCommandHandler<ClassCommand, NoOpClassHandler>();
        optServices.AddCommandHandler<StructCommand, NoOpStructHandler>();
        optServices.AddOptimizedCommandDispatcher();
        _optimizedDispatcher = optServices.BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

        // ── InMemory: explicit registration, ConcurrentDictionary lookup ──────
        var inMemory = new InMemoryCommandDispatcher();
        inMemory.RegisterHandler(new NoOpClassHandler());
        inMemory.RegisterHandler(new NoOpStructHandler());
        _inMemoryDispatcher = inMemory;
    }

    // ── Class (reference type) commands ──────────────────────────────────────

    [Benchmark(Baseline = true, Description = "ServiceProvider | class cmd")]
    public Task<CommandDispatchResult> ServiceProvider_ClassCommand()
        => _serviceProviderDispatcher.DispatchAsync(ClassCmd);

    [Benchmark(Description = "Optimized | class cmd")]
    public Task<CommandDispatchResult> Optimized_ClassCommand()
        => _optimizedDispatcher.DispatchAsync(ClassCmd);

    [Benchmark(Description = "InMemory | class cmd")]
    public Task<CommandDispatchResult> InMemory_ClassCommand()
        => _inMemoryDispatcher.DispatchAsync(ClassCmd);

    // ── Struct (value type) commands — reveals boxing differences ────────────

    [Benchmark(Description = "ServiceProvider | struct cmd")]
    public Task<CommandDispatchResult> ServiceProvider_StructCommand()
        => _serviceProviderDispatcher.DispatchAsync(StructCmd);

    [Benchmark(Description = "Optimized | struct cmd")]
    public Task<CommandDispatchResult> Optimized_StructCommand()
        => _optimizedDispatcher.DispatchAsync(StructCmd);

    [Benchmark(Description = "InMemory | struct cmd")]
    public Task<CommandDispatchResult> InMemory_StructCommand()
        => _inMemoryDispatcher.DispatchAsync(StructCmd);

    // ── Command types ─────────────────────────────────────────────────────────

    private sealed record ClassCommand(string Value) : ICommand;

    private readonly record struct StructCommand(string Value) : ICommand;

    // ── Handlers return a pre-allocated Task to isolate dispatcher overhead ──

    private static readonly Task<CommandDispatchResult> CachedOk =
        Task.FromResult(CommandDispatchResult.Ok("bench"));

    private sealed class NoOpClassHandler : ICommandHandler<ClassCommand>
    {
        public Task<CommandDispatchResult> HandleAsync(ClassCommand command, CancellationToken ct = default)
            => CachedOk;
    }

    private sealed class NoOpStructHandler : ICommandHandler<StructCommand>
    {
        public Task<CommandDispatchResult> HandleAsync(StructCommand command, CancellationToken ct = default)
            => CachedOk;
    }
}
