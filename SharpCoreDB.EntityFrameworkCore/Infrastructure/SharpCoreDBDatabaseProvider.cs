using Microsoft.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database provider implementation for SharpCoreDB.
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBDatabaseProvider : IDatabase
{
    /// <inheritdoc />
    public string? DatabaseProductName => "SharpCoreDB";

    /// <inheritdoc />
    public string? DatabaseProductVersion => "1.0.0";
}
