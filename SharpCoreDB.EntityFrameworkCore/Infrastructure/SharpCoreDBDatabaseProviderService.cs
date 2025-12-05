using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database provider service for SharpCoreDB.
/// Identifies this provider to Entity Framework Core.
/// </summary>
public class SharpCoreDBDatabaseProviderService : IDatabaseProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string Name => "SharpCoreDB";

    /// <summary>
    /// Determines if the given options extension is for this provider.
    /// </summary>
    public bool IsConfigured(IDbContextOptions options)
    {
        return options.Extensions.OfType<SharpCoreDBOptionsExtension>().Any();
    }
}
