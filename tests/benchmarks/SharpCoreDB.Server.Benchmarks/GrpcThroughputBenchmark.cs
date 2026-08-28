// <copyright file="GrpcThroughputBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Benchmarks;

using BenchmarkDotNet.Attributes;
using Grpc.Net.Client;
using SharpCoreDB.Server.Protocol;

/// <summary>
/// Benchmarks gRPC throughput for database operations.
/// </summary>
[MemoryDiagnoser]
public class GrpcThroughputBenchmark
{
    private ServerBenchmarkHarness? _harness;
    private GrpcChannel? _channel;
    private DatabaseService.DatabaseServiceClient? _client;

    [GlobalSetup]
    public void Setup()
    {
        _harness = new ServerBenchmarkHarness();
        _channel = _harness.CreateGrpcChannel();
        _client = new DatabaseService.DatabaseServiceClient(_channel);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _channel?.Dispose();
        _harness?.Dispose();
    }

    [Benchmark]
    public async Task ExecuteQueryAsync()
    {
        var request = new QueryRequest
        {
            SessionId = "benchmark-session",
            Sql = "SELECT 1"
        };

        var call = _client.ExecuteQuery(request, cancellationToken: _harness.CancellationToken);
        var count = 0;
        while (await call.ResponseStream.MoveNext(_harness.CancellationToken).ConfigureAwait(false))
        {
            count += call.ResponseStream.Current.Rows.Count;
        }
        _ = count;
    }

    [Benchmark]
    public async Task ExecuteNonQueryAsync()
    {
        var request = new NonQueryRequest
        {
            SessionId = "benchmark-session",
            Sql = "CREATE TABLE IF NOT EXISTS test_table (id INTEGER PRIMARY KEY, name TEXT)"
        };

        await _client.ExecuteNonQueryAsync(request, cancellationToken: _harness.CancellationToken).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task BatchInsertAsync()
    {
        var queries = new List<QueryRequest>();
        for (int i = 0; i < 100; i++)
        {
            queries.Add(new QueryRequest
            {
                SessionId = "benchmark-session",
                Sql = $"INSERT INTO test_table (name) VALUES ('test{i}')"
            });
        }

        var request = new BatchRequest
        {
            SessionId = "benchmark-session",
            Queries = { queries }
        };

        await _client.ExecuteBatchAsync(request, cancellationToken: _harness.CancellationToken).ConfigureAwait(false);
    }
}
