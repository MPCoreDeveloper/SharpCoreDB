using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;

namespace SharpCoreDB.EntityFrameworkCore;

/// <summary>
/// Options extension for configuring SharpCoreDB with Entity Framework Core.
/// </summary>
public class SharpCoreDBOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// Gets the connection string.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBOptionsExtension class.
    /// </summary>
    public SharpCoreDBOptionsExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBOptionsExtension class with existing extension.
    /// </summary>
    protected SharpCoreDBOptionsExtension(SharpCoreDBOptionsExtension copyFrom)
    {
        ConnectionString = copyFrom.ConnectionString;
    }

    /// <summary>
    /// Gets the extension info.
    /// </summary>
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Sets the connection string.
    /// </summary>
    public virtual SharpCoreDBOptionsExtension WithConnectionString(string connectionString)
    {
        var clone = Clone();
        clone.ConnectionString = connectionString;
        return clone;
    }

    /// <summary>
    /// Clones this extension.
    /// </summary>
    protected virtual SharpCoreDBOptionsExtension Clone() => new(this);

    /// <summary>
    /// Applies services to the service collection.
    /// </summary>
    public void ApplyServices(IServiceCollection services)
    {
        // Register EF Core services for SharpCoreDB
        services.AddEntityFrameworkSharpCoreDB();
        
        // Register SharpCoreDB's own services
        services.AddSharpCoreDB();
    }

    /// <summary>
    /// Validates the options.
    /// </summary>
    public void Validate(IDbContextOptions options)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Connection string must be configured.");
        }
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private string? _logFragment;

        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        private new SharpCoreDBOptionsExtension Extension
            => (SharpCoreDBOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => true;

        public override string LogFragment =>
            _logFragment ??= $"ConnectionString={Extension.ConnectionString}";

        public override int GetServiceProviderHashCode()
            => Extension.ConnectionString?.GetHashCode() ?? 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
               && Extension.ConnectionString == otherInfo.Extension.ConnectionString;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["SharpCoreDB:ConnectionString"] = (Extension.ConnectionString?.GetHashCode() ?? 0).ToString();
        }
    }
}
