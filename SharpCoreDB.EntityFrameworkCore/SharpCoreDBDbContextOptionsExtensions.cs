using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharpCoreDB.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring SharpCoreDB in DbContext.
/// </summary>
public static class SharpCoreDBDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to use SharpCoreDB.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string (format: Data Source=path;Password=pass;Pooling=true).</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The options builder.</returns>
    public static DbContextOptionsBuilder UseSharpCoreDB(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<SharpCoreDBDbContextOptionsBuilder>? configure = null)
    {
        var extension = GetOrCreateExtension(optionsBuilder);
        extension = (SharpCoreDBOptionsExtension)extension.WithConnectionString(connectionString);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        configure?.Invoke(new SharpCoreDBDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    private static SharpCoreDBOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<SharpCoreDBOptionsExtension>()
           ?? new SharpCoreDBOptionsExtension();
}

/// <summary>
/// Builder for SharpCoreDB-specific options.
/// </summary>
public class SharpCoreDBDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDbContextOptionsBuilder class.
    /// </summary>
    public SharpCoreDBDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        _optionsBuilder = optionsBuilder;
    }
}
