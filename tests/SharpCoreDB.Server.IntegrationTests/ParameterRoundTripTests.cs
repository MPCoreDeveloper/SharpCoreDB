// <copyright file="ParameterRoundTripTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Core;
using SharpCoreDB.Server.Protocol;
using CoreDatabaseService = SharpCoreDB.Server.Core.DatabaseService;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Regression tests for parameter pass-through on the server (gRPC).
/// Guards against SharpCoreDB.Server dropping request.Parameters in
/// DatabaseService.ExecuteQuery / ExecuteNonQuery.
/// </summary>
public sealed class ParameterRoundTripTests : IAsyncLifetime
{
    private readonly TestServerFixture _fixture = new();
    private CoreDatabaseService _service = null!;
    private string _sessionId = null!;

    public async ValueTask InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _service = _fixture.CreateDatabaseService();
        _sessionId = _fixture.TestSessionId!;

        await _fixture.ExecuteSetupSqlAsync("CREATE TABLE IF NOT EXISTS round_trip (a TEXT, b INTEGER, c REAL, d TEXT)");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    private NonQueryRequest BuildInsertRequest()
    {
        var insert = new NonQueryRequest
        {
            SessionId = _sessionId,
            Sql = "INSERT INTO round_trip VALUES (@a, @b, @c, @d)",
        };

        insert.Parameters["@a"] = new ParameterValue { StringValue = "hello" };
        insert.Parameters["@b"] = new ParameterValue { IntValue = 42 };
        insert.Parameters["@c"] = new ParameterValue { DoubleValue = 3.14 };
        insert.Parameters["@d"] = new ParameterValue { StringValue = "2026-08-27" };

        return insert;
    }

    [Fact]
    public async Task ExecuteNonQuery_WithParameters_InsertsValuesIntoCorrectColumns()
    {
        // Arrange
        var request = BuildInsertRequest();

        // Act
        var response = await _service.ExecuteNonQuery(request, TestServerCallContext.Create());

        // Assert — the row must have been inserted (parameters were not dropped).
        Assert.Equal(1L, response.RowsAffected);
    }

    [Fact]
    public async Task ExecuteQuery_WithParameters_ReturnsMatchingRow()
    {
        // Arrange: seed one row via a parameterized insert.
        await _service.ExecuteNonQuery(BuildInsertRequest(), TestServerCallContext.Create());

        var query = new QueryRequest
        {
            SessionId = _sessionId,
            Sql = "SELECT * FROM round_trip WHERE b = @b",
        };
        query.Parameters["@b"] = new ParameterValue { IntValue = 42 };

        var responseWriter = new TestServerStreamWriter<QueryResponse>();

        // Act
        await _service.ExecuteQuery(query, responseWriter, TestServerCallContext.Create());

        // Assert
        var rows = responseWriter.Responses.SelectMany(r => r.Rows).ToList();
        Assert.Single(rows);

        var row = rows[0];
        Assert.Equal("hello", row.Values[0].StringValue);
        Assert.Equal(42, row.Values[1].IntValue);
        Assert.Equal(3.14, row.Values[2].DoubleValue, 3);
        Assert.Equal("2026-08-27", row.Values[3].StringValue);
    }
}
