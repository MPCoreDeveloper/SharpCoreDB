using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore.Infrastructure;
using SharpCoreDB.EntityFrameworkCore.Query;
using SharpCoreDB.EntityFrameworkCore.Storage;
using SharpCoreDB.EntityFrameworkCore.Update;

namespace SharpCoreDB.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring Entity Framework Core services for SharpCoreDB.
/// </summary>
public static class SharpCoreDBServiceCollectionExtensions
{
    /// <summary>
    /// Adds Entity Framework Core services for SharpCoreDB.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddEntityFrameworkSharpCoreDB(
        this IServiceCollection serviceCollection)
    {
        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IDatabase, SharpCoreDBDatabaseProvider>()
            .TryAdd<IDatabaseCreator, SharpCoreDBDatabaseCreator>()
            .TryAdd<IRelationalConnection, SharpCoreDBRelationalConnection>()
            .TryAdd<IRelationalTypeMappingSource, SharpCoreDBTypeMappingSource>()
            .TryAdd<IModificationCommandBatchFactory, SharpCoreDBModificationCommandBatchFactory>()
            .TryAdd<IQuerySqlGeneratorFactory, SharpCoreDBQuerySqlGeneratorFactory>()
            .TryAdd<ISqlGenerationHelper, SharpCoreDBSqlGenerationHelper>();

        builder.TryAddCoreServices();

        return serviceCollection;
    }
}
