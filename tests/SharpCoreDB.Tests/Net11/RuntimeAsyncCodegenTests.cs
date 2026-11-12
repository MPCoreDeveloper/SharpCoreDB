// <copyright file="RuntimeAsyncCodegenTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Tests.Net11;

using System.Runtime.CompilerServices;
using SharpCoreDB;
using Xunit;

/// <summary>
/// Canaries for .NET 11 Runtime Async. The compiler feature (<c>Features=runtime-async=on</c>) is
/// switched on once, in the repository-root <c>Directory.Build.targets</c>, for every net11.0 project:
/// async methods then suspend through the runtime instead of through a compiler-generated state machine
/// per method, and a synchronously-completing <c>Task</c>/<c>Task&lt;T&gt;</c>-returning method no
/// longer allocates its result.
/// <para>
/// These tests are the reason the switch cannot silently disappear: both facts are observable, so
/// they fail if the flag is dropped (or if a project opts out) instead of showing up as a quiet
/// allocation regression.
/// </para>
/// </summary>
public sealed class RuntimeAsyncCodegenTests
{
    /// <summary>
    /// The engine assembly carries no compiler-generated async state machine. Async iterators are
    /// excluded on purpose: at .NET 11 GA the compiler still emits a state machine for
    /// <c>async IAsyncEnumerable&lt;T&gt;</c> methods (verified on the GA SDK — see
    /// <c>docs/net11/UPGRADE.md</c> §2), so those types are expected and are not a sign that the feature
    /// is off.
    /// </summary>
    [Fact]
    public void EngineAssembly_AsyncMethods_DoNotEmitCompilerAsyncStateMachines()
    {
        var offenders = typeof(Database).Assembly.GetTypes()
            .Where(type => typeof(IAsyncStateMachine).IsAssignableFrom(type))
            .Where(type => !IsAsyncIteratorStateMachine(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} compiler-generated async state machine(s) found in SharpCoreDB.dll: " +
            $"{string.Join(", ", offenders)}. .NET 11 Runtime Async (Features=runtime-async=on) — " +
            "set in the repository-root Directory.Build.targets — must make these runtime-native.");
    }

    /// <summary>
    /// An awaited, synchronously-completing async method allocates nothing per call. Before Runtime
    /// Async the compiler allocated a ~72-byte <c>Task&lt;int&gt;</c> per call; the JIT's runtime-async
    /// specialization of synchronously-completing task-returning methods now returns a shared completed
    /// task instead.
    /// <para>
    /// The <c>await</c> shape matters and is asserted deliberately: calling the same method as
    /// <c>.GetAwaiter().GetResult()</c> (or discarding it without awaiting) still materialises the
    /// <c>Task&lt;int&gt;</c> — measured at 72,0 B/call on the GA runtime. This is the shape consumers
    /// actually use, and it is the shape the win applies to.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AwaitedSynchronouslyCompletingCall_DoesNotAllocatePerCall()
    {
        const int Calls = 1_000;

        // Warm up JIT/tiering so the measured loop does not absorb one-off costs.
        for (int i = 0; i < 2_000; i++)
        {
            Assert.Equal(i + 1, await CompleteSynchronouslyAsync(i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Calls; i++)
        {
            _ = await CompleteSynchronouslyAsync(i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated == 0,
            $"An awaited, synchronously-completing call allocated {allocated} bytes over {Calls} calls " +
            $"({allocated / (double)Calls:F1} B/call); .NET 11 Runtime Async keeps this at 0. " +
            "A non-zero value means the runtime-async compiler flag is not applied to this assembly.");
    }

    private static async Task<int> CompleteSynchronouslyAsync(int value)
    {
        await Task.CompletedTask;
        return value + 1;
    }

    private static bool IsAsyncIteratorStateMachine(Type type)
        => type.GetInterfaces().Any(
            contract => contract.IsGenericType
                        && contract.GetGenericTypeDefinition() == typeof(IAsyncEnumerator<>));
}
#endif
