// <copyright file="ConnectionPoolBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Benchmarks;

using BenchmarkDotNet.Attributes;
using Grpc.Net.Client;

/// <summary>
/// Benchmarks connection pooling and concurrent connections.
/// </summary>
[MemoryDiagnoser]
public class ConnectionPoolBenchmark
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
    public async Task CreateAndDisposeGrpcChannelAsync()
    {
        using var channel = _harness.CreateGrpcChannel();
        var client = new SharpCoreDB.Server.Protocol.DatabaseService.DatabaseServiceClient(channel);

        var request = new SharpCoreDB.Server.Protocol.QueryRequest
        {
            SessionId = "benchmark-session",
            Sql = "SELECT 1"
        };

        var call = client.ExecuteQuery(request, cancellationToken: _harness.CancellationToken);
        while (await call.ResponseStream.MoveNext(_harness.CancellationToken).ConfigureAwait(false))
        {
            _ = call.ResponseStream.Current.Rows.Count;
        }
    }

    [Benchmark]
    public async Task ConcurrentQueriesAsync()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var channel = _harness.CreateGrpcChannel();
                var client = new SharpCoreDB.Server.Protocol.DatabaseService.DatabaseServiceClient(channel);

                var request = new SharpCoreDB.Server.Protocol.QueryRequest
                {
                    SessionId = "benchmark-session",
                    Sql = "SELECT 1"
                };

                var call = client.ExecuteQuery(request, cancellationToken: _harness.CancellationToken);
                while (await call.ResponseStream.MoveNext(_harness.CancellationToken).ConfigureAwait(false))
                {
                    _ = call.ResponseStream.Current.Rows.Count;
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task CreateAndDisposeWebSocketAsync()
    {
        using var webSocket = await _harness.CreateWebSocketClientAsync().ConfigureAwait(false);

        var message = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"query\",\"sessionId\":\"benchmark-session\",\"sql\":\"SELECT 1\"}");
        await webSocket.SendAsync(message, System.Net.WebSockets.WebSocketMessageType.Text, true, _harness.CancellationToken).ConfigureAwait(false);

        var buffer = new byte[4096];
        var result = await webSocket.ReceiveAsync(buffer, _harness.CancellationToken).ConfigureAwait(false);
        _ = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
