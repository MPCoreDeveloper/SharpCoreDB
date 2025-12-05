using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpCoreDB.EntityFrameworkCore.Diagnostics;
using SharpCoreDB.EntityFrameworkCore.Infrastructure;
using SharpCoreDB.EntityFrameworkCore.Migrations;
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
        // Add logging if not already added
        if (!serviceCollection.Any(sd => sd.ServiceType == typeof(ILoggerFactory)))
        {
            serviceCollection.AddLogging();
        }

        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, SharpCoreDBLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, SharpCoreDBDatabaseProviderService>()
            .TryAdd<IDatabase, SharpCoreDBDatabaseProvider>()
            .TryAdd<IDatabaseCreator, SharpCoreDBDatabaseCreator>()
            .TryAdd<IRelationalConnection, SharpCoreDBRelationalConnection>()
            .TryAdd<IRelationalTypeMappingSource, SharpCoreDBTypeMappingSource>()
            .TryAdd<IModificationCommandBatchFactory, SharpCoreDBModificationCommandBatchFactory>()
            .TryAdd<IQuerySqlGeneratorFactory, SharpCoreDBQuerySqlGeneratorFactory>()
            .TryAdd<ISqlGenerationHelper, SharpCoreDBSqlGenerationHelper>()
            .TryAdd<IMigrationsSqlGenerator, SharpCoreDBMigrationsSqlGenerator>();

        builder.TryAddCoreServices();

        return serviceCollection;
    }
}
