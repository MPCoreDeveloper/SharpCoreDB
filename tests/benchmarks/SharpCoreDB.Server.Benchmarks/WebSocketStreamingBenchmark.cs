// <copyright file="WebSocketStreamingBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Benchmarks;

using System.Net.WebSockets;
using System.Text;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Benchmarks WebSocket streaming throughput for database operations.
/// </summary>
[MemoryDiagnoser]
public class WebSocketStreamingBenchmark
{
    private ServerBenchmarkHarness? _harness;

    [GlobalSetup]
    public void Setup()
    {
        _harness = new ServerBenchmarkHarness();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _harness?.Dispose();
    }

    [Benchmark]
    public async Task SendQueryMessageAsync()
    {
        using var webSocket = await _harness.CreateWebSocketClientAsync().ConfigureAwait(false);

        var message = Encoding.UTF8.GetBytes("{\"type\":\"query\",\"sql\":\"SELECT 1\",\"database\":\"test\"}");
        await webSocket.SendAsync(message, WebSocketMessageType.Text, true, _harness.CancellationToken).ConfigureAwait(false);

        // Receive response
        var buffer = new byte[4096];
        var result = await webSocket.ReceiveAsync(buffer, _harness.CancellationToken).ConfigureAwait(false);
        _ = Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    [Benchmark]
    public async Task SendNonQueryMessageAsync()
    {
        using var webSocket = await _harness.CreateWebSocketClientAsync().ConfigureAwait(false);

        var message = Encoding.UTF8.GetBytes("{\"type\":\"nonquery\",\"sql\":\"CREATE TABLE IF NOT EXISTS test_table (id INTEGER PRIMARY KEY, name TEXT)\",\"database\":\"test\"}");
        await webSocket.SendAsync(message, WebSocketMessageType.Text, true, _harness.CancellationToken).ConfigureAwait(false);

        // Receive response
        var buffer = new byte[4096];
        var result = await webSocket.ReceiveAsync(buffer, _harness.CancellationToken).ConfigureAwait(false);
        _ = Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    [Benchmark]
    public async Task SendBatchMessageAsync()
    {
        using var webSocket = await _harness.CreateWebSocketClientAsync().ConfigureAwait(false);

        var statements = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            statements.Add($"INSERT INTO test_table (name) VALUES ('test{i}')");
        }

        var payload = new { type = "batch", statements, database = "test" };
        var message = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload));
        await webSocket.SendAsync(message, WebSocketMessageType.Text, true, _harness.CancellationToken).ConfigureAwait(false);

        // Receive response
        var buffer = new byte[4096];
        var result = await webSocket.ReceiveAsync(buffer, _harness.CancellationToken).ConfigureAwait(false);
        _ = Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
