// <copyright file="WebSocketProtocol.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpCoreDB.Server.Core.WebSockets;

/// <summary>
/// Message types for the WebSocket JSON protocol.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WebSocketMessageType>))]
public enum WebSocketMessageType
{
    /// <summary>Authenticate with a JWT token.</summary>
    Auth,

    /// <summary>Execute a SELECT query (streaming result).</summary>
    Query,

    /// <summary>Execute a non-query (INSERT/UPDATE/DELETE).</summary>
    Execute,

    /// <summary>Execute a batch of statements.</summary>
    Batch,

    /// <summary>Subscribe to change notifications on a table.</summary>
    Subscribe,

    /// <summary>Unsubscribe from change notifications.</summary>
    Unsubscribe,

    /// <summary>Ping/keep-alive.</summary>
    Ping,

    /// <summary>Server result message (query rows).</summary>
    Result,

    /// <summary>Server error message.</summary>
    Error,

    /// <summary>Server acknowledgement (auth success, non-query affected rows).</summary>
    Ack,

    /// <summary>Server change notification.</summary>
    Notification,

    /// <summary>Server pong response.</summary>
    Pong,
}

/// <summary>
/// Incoming WebSocket message from the client.
/// C# 15: Records for immutable DTOs.
/// </summary>
public sealed record WebSocketRequest
{
    /// <summary>Message type.</summary>
    [JsonPropertyName("type")]
    public required WebSocketMessageType Type { get; init; }

    /// <summary>Client-generated request ID for correlation.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>SQL statement (for Query, Execute, Batch).</summary>
    [JsonPropertyName("sql")]
    public string? Sql { get; init; }

    /// <summary>Target database name (defaults to server default).</summary>
    [JsonPropertyName("database")]
    public string? Database { get; init; }

    /// <summary>JWT bearer token (for Auth message).</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    /// <summary>Table name (for Subscribe/Unsubscribe).</summary>
    [JsonPropertyName("table")]
    public string? Table { get; init; }

    /// <summary>Query parameters.</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object?>? Parameters { get; init; }

    /// <summary>Batch SQL statements.</summary>
    [JsonPropertyName("statements")]
    public List<string>? Statements { get; init; }
}

/// <summary>
/// Outgoing WebSocket message to the client.
/// </summary>
public sealed record WebSocketResponse
{
    /// <summary>Message type.</summary>
    [JsonPropertyName("type")]
    public required WebSocketMessageType Type { get; init; }

    /// <summary>Correlating request ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Whether the operation succeeded.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Result rows (for Query results).</summary>
    [JsonPropertyName("rows")]
    public List<Dictionary<string, object?>>? Rows { get; init; }

    /// <summary>Column names (first result frame only).</summary>
    [JsonPropertyName("columns")]
    public List<string>? Columns { get; init; }

    /// <summary>Whether more rows follow (streaming).</summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; init; }

    /// <summary>Affected row count (for Execute/Batch).</summary>
    [JsonPropertyName("affectedRows")]
    public int AffectedRows { get; init; }

    /// <summary>Error message (for Error responses).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Error code.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Server timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Additional message (for Ack).</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Shared JSON serializer options for the WebSocket protocol.
/// C# 15: Lazy singleton.
/// </summary>
public static class WebSocketJsonOptions
{
    /// <summary>Shared serializer options with camelCase and enum string conversion.</summary>
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
