// <copyright file="RestApiBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Benchmarks;

using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;

/// <summary>
/// Benchmarks REST API throughput for database operations.
/// </summary>
[MemoryDiagnoser]
public class RestApiBenchmark
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
    public async Task GetQueryAsync()
    {
        var url = $"{_harness.BaseAddress}api/query?sql=SELECT%201&database=test";
        using var response = await _harness.HttpClient.GetAsync(url, _harness.CancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync(_harness.CancellationToken).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task PostNonQueryAsync()
    {
        var url = $"{_harness.BaseAddress}api/nonquery";
        var content = JsonContent.Create(new
        {
            sql = "CREATE TABLE IF NOT EXISTS test_table (id INTEGER PRIMARY KEY, name TEXT)",
            database = "test"
        });

        using var response = await _harness.HttpClient.PostAsync(url, content, _harness.CancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    public async Task PostBatchAsync()
    {
        var url = $"{_harness.BaseAddress}api/batch";
        var statements = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            statements.Add($"INSERT INTO test_table (name) VALUES ('test{i}')");
        }

        var content = JsonContent.Create(new
        {
            statements,
            database = "test"
        });

        using var response = await _harness.HttpClient.PostAsync(url, content, _harness.CancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
