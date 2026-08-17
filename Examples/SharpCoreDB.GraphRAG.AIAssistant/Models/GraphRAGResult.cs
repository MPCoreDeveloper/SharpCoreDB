namespace SharpCoreDB.GraphRAG.AIAssistant.Models;

/// <summary>
/// Represents the result of a GraphRAG query combining vector search and graph traversal.
/// </summary>
public sealed class GraphRAGResult
{
    public required long DocumentId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }

    /// <summary>
    /// Combined relevance score (0.0 to 1.0)
    /// Calculated from: vector similarity + graph centrality + relationship weight
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Vector similarity score from semantic search (0.0 to 1.0)
    /// </summary>
    public required double VectorSimilarity { get; init; }

    /// <summary>
    /// Graph-based importance score (0.0 to 1.0)
    /// Based on centrality metrics and relationship traversal
    /// </summary>
    public double GraphScore { get; init; }

    /// <summary>
    /// How this document was found
    /// </summary>
    public required RetrievalMethod RetrievalMethod { get; init; }

    /// <summary>
    /// If found via graph traversal, the relationship type and hop distance
    /// </summary>
    public RelationshipPath? RelationshipPath { get; init; }

    /// <summary>
    /// Source article for citations
    /// </summary>
    public string? SourceUrl { get; init; }
}

/// <summary>
/// How a document was retrieved in the GraphRAG pipeline
/// </summary>
public enum RetrievalMethod
{
    /// <summary>Direct vector similarity match</summary>
    VectorSearch,

    /// <summary>Found via graph traversal from a vector match</summary>
    GraphTraversal,

    /// <summary>Hybrid: found by both methods</summary>
    Hybrid
}

/// <summary>
/// Describes the graph path taken to find a document
/// </summary>
public sealed class RelationshipPath
{
    public required RelationshipType RelationType { get; init; }
    public required int HopDistance { get; init; }
    public required long SourceDocumentId { get; init; }
}
