// <copyright file="ServerBenchmarkHarness.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Benchmarks;

using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using SharpCoreDB.Server;

/// <summary>
/// Harness for server performance benchmarks, providing test server setup and client connections.
/// </summary>
public sealed class ServerBenchmarkHarness : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerBenchmarkHarness"/> class.
    /// </summary>
    public ServerBenchmarkHarness()
    {
        _factory = new WebApplicationFactory<Program>();
        _httpClient = _factory.CreateClient();
    }

    /// <summary>
    /// Gets the HTTP client for REST API benchmarks.
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Gets the base address of the test server.
    /// </summary>
    public Uri BaseAddress => _httpClient.BaseAddress;

    /// <summary>
    /// Gets a cancellation token for benchmark operations.
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// Creates a gRPC channel for gRPC benchmarks.
    /// </summary>
    public Grpc.Net.Client.GrpcChannel CreateGrpcChannel()
    {
        return Grpc.Net.Client.GrpcChannel.ForAddress(BaseAddress, new Grpc.Net.Client.GrpcChannelOptions
        {
            HttpClient = _httpClient
        });
    }

    /// <summary>
    /// Creates a WebSocket client for WebSocket benchmarks.
    /// </summary>
    public async Task<System.Net.WebSockets.ClientWebSocket> CreateWebSocketClientAsync(string path = "/ws")
    {
        var webSocket = new System.Net.WebSockets.ClientWebSocket();
        var uri = new Uri(BaseAddress, path);
        await webSocket.ConnectAsync(uri, CancellationToken).ConfigureAwait(false);
        return webSocket;
    }

    /// <summary>
    /// Disposes the harness and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _cts.Cancel();
        _cts.Dispose();
        _httpClient.Dispose();
        _factory.Dispose();
        _disposed = true;
    }
}
