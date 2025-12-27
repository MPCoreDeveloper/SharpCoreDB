namespace SharpCoreDB.DataStructures;

/// <summary>
/// Describes a database table for metadata discovery.
/// </summary>
public sealed record TableInfo
{
    /// <summary>
    /// Table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Table type (e.g., TABLE, VIEW). Default TABLE.
    /// </summary>
    public string Type { get; init; } = "TABLE";
}
