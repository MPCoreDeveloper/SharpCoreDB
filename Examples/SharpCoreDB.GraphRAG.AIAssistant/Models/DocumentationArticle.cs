namespace SharpCoreDB.GraphRAG.AIAssistant.Models;

/// <summary>
/// Represents a technical documentation article with metadata.
/// </summary>
public sealed class DocumentationArticle
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string Category { get; init; }
    public required string[] Tags { get; init; }
    public required string Url { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Difficulty level: Beginner, Intermediate, Advanced
    /// </summary>
    public string DifficultyLevel { get; init; } = "Intermediate";

    /// <summary>
    /// Estimated reading time in minutes
    /// </summary>
    public int ReadingTimeMinutes { get; init; }
}
