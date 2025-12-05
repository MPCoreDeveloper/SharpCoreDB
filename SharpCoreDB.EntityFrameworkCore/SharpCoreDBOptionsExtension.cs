using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;

namespace SharpCoreDB.EntityFrameworkCore;

/// <summary>
/// Options extension for configuring SharpCoreDB with Entity Framework Core.
/// </summary>
public class SharpCoreDBOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private string _connectionString = string.Empty;

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
        : base(copyFrom)
    {
        _connectionString = copyFrom._connectionString;
    }

    /// <summary>
    /// Gets the extension info.
    /// </summary>
    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public override string? ConnectionString => _connectionString;

    /// <inheritdoc />
    public override RelationalOptionsExtension WithConnectionString(string? connectionString)
    {
        var clone = (SharpCoreDBOptionsExtension)Clone();
        clone._connectionString = connectionString ?? string.Empty;
        return clone;
    }

    /// <summary>
    /// Clones this extension.
    /// </summary>
    protected override RelationalOptionsExtension Clone() => new SharpCoreDBOptionsExtension(this);

    /// <summary>
    /// Applies services to the service collection.
    /// </summary>
    public override void ApplyServices(IServiceCollection services)
    {
        // Register EF Core services for SharpCoreDB
        services.AddEntityFrameworkSharpCoreDB();
        
        // Register SharpCoreDB's own services
        services.AddSharpCoreDB();
    }

    /// <summary>
    /// Validates the options.
    /// </summary>
    public override void Validate(IDbContextOptions options)
    {
        base.Validate(options);
        
        if (string.IsNullOrWhiteSpace(_connectionString))
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
