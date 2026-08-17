namespace SharpCoreDB.GraphRAG.AIAssistant.Models;

/// <summary>
/// Types of relationships between documentation articles.
/// Used for graph traversal to find related content.
/// </summary>
public enum RelationshipType
{
    /// <summary>
    /// Article A must be understood before Article B
    /// Example: "HTTP Basics" is prerequisite for "REST API Design"
    /// </summary>
    Prerequisite,

    /// <summary>
    /// Article B depends on concepts/code from Article A
    /// Example: "JWT Authentication" depends on "User Models"
    /// </summary>
    DependsOn,

    /// <summary>
    /// Articles cover related topics at same level
    /// Example: "Docker Deployment" related to "Kubernetes Deployment"
    /// </summary>
    RelatedTo,

    /// <summary>
    /// Article B is a follow-up/next step after Article A
    /// Example: "Getting Started" -> "Advanced Features"
    /// </summary>
    FollowsFrom,

    /// <summary>
    /// Articles reference the same API/library
    /// Example: "LINQ Queries" and "Entity Framework" both use System.Linq
    /// </summary>
    SharesAPI
}

/// <summary>
/// Represents a directed relationship between two documentation articles
/// </summary>
public sealed class DocumentRelationship
{
    public required long SourceDocId { get; init; }
    public required long TargetDocId { get; init; }
    public required RelationshipType RelationType { get; init; }

    /// <summary>
    /// Relationship strength/importance (0.0 to 1.0)
    /// Higher weight = more important relationship
    /// </summary>
    public double Weight { get; init; } = 1.0;
}
