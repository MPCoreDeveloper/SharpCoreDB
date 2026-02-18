// <copyright file="GraphFunctionProviderTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Graph;

public class GraphFunctionProviderTests
{
    [Fact]
    public void Evaluate_GraphTraverse_ReturnsExpectedCount()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());
        var provider = new GraphFunctionProvider(engine);

        var result = provider.Evaluate("GRAPH_TRAVERSE", [table, 1L, "next", 2]);

        var rowIds = Assert.IsType<long[]>(result);
        Assert.Equal(3, rowIds.Length);
    }

    [Fact]
    public void Evaluate_GraphTraverse_Dijkstra_ReturnsExpectedCount()
    {
        var table = CreateLinearGraphTable();
        var engine = new GraphTraversalEngine(new GraphSearchOptions());
        var provider = new GraphFunctionProvider(engine);

        var result = provider.Evaluate("GRAPH_TRAVERSE", [table, 1L, "next", 2, "DIJKSTRA"]);

        var rowIds = Assert.IsType<long[]>(result);
        Assert.Equal(3, rowIds.Length);
    }

    private static FakeGraphTable CreateLinearGraphTable()
    {
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1L, ["next"] = 2L },
            new() { ["id"] = 2L, ["next"] = 3L },
            new() { ["id"] = 3L, ["next"] = 4L },
            new() { ["id"] = 4L, ["next"] = DBNull.Value }
        };

        return new FakeGraphTable(rows, "next");
    }
}
