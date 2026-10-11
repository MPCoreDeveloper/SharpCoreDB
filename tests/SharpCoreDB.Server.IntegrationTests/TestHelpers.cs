// <copyright file="TestHelpers.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Core;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Minimal ServerCallContext for unit/integration testing without a real gRPC server.
/// C# 15: Collection expressions, primary constructor patterns.
/// </summary>
public sealed class TestServerCallContext : ServerCallContext
{
    private readonly Metadata _requestHeaders = [];
    private readonly Metadata _responseTrailers = [];
    private readonly CancellationToken _cancellationToken;

    private TestServerCallContext(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Creates a new test context with optional cancellation.
    /// </summary>
    public static TestServerCallContext Create(CancellationToken cancellationToken = default) =>
        new(cancellationToken);

    protected override string MethodCore => "/test/Method";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "127.0.0.1:0";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore => _responseTrailers;
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore =>
        new(string.Empty, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}

/// <summary>
/// In-memory IServerStreamWriter for capturing gRPC server-streaming responses.
/// C# 15: Collection expressions.
/// </summary>
/// <typeparam name="T">Response message type.</typeparam>
public sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> _responses = [];
    private readonly Lock _lock = new();

    /// <summary>Gets all captured response messages.</summary>
    public IReadOnlyList<T> Responses
    {
        get
        {
            lock (_lock)
            {
                return [.. _responses];
            }
        }
    }

    /// <inheritdoc />
    public WriteOptions? WriteOptions { get; set; }

    /// <inheritdoc />
    public Task WriteAsync(T message)
    {
        lock (_lock)
        {
            _responses.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a message with cancellation support (gRPC overload).
    /// </summary>
    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteAsync(message);
    }
}
