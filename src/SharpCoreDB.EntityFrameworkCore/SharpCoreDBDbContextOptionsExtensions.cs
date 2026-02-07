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

    /// <summary>
    /// Configures the context to use SharpCoreDB with the generic <see cref="DbContextOptionsBuilder{TContext}"/>.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string (format: Data Source=path;Password=pass;Pooling=true).</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The options builder.</returns>
    public static DbContextOptionsBuilder<TContext> UseSharpCoreDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<SharpCoreDBDbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseSharpCoreDB(connectionString, configure);
        return optionsBuilder;
    }

    private static SharpCoreDBOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<SharpCoreDBOptionsExtension>()
           ?? new SharpCoreDBOptionsExtension();
}

/// <summary>
/// Builder for SharpCoreDB-specific options.
/// Provides a fluent API for configuring provider behavior such as command timeout and query splitting.
/// Inherits from <see cref="RelationalDbContextOptionsBuilder{TBuilder, TExtension}"/>
/// to support standard relational options like <c>CommandTimeout</c> and <c>MaxBatchSize</c>.
/// </summary>
public class SharpCoreDBDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<SharpCoreDBDbContextOptionsBuilder, SharpCoreDBOptionsExtension>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDbContextOptionsBuilder"/> class.
    /// </summary>
    /// <param name="optionsBuilder">The core options builder.</param>
    public SharpCoreDBDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }
}
