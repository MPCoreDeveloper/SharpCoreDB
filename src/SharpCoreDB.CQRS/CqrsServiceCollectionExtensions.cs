// <copyright file="CqrsServiceCollectionExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpCoreDB.EventSourcing;
using SharpCoreDB.Interfaces;
using SharpDispatch;

/// <summary>
/// Dependency injection extensions for SharpCoreDB.CQRS primitives.
/// </summary>
public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers default CQRS services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSharpCoreDBCqrs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICommandDispatcher, ServiceProviderCommandDispatcher>();
        services.TryAddSingleton(new OutboxRetryPolicyOptions());
        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var retryPolicy = sp.GetRequiredService<OutboxRetryPolicyOptions>();
            return new InMemoryOutboxStore(retryPolicy);
        });
        services.TryAddSingleton<OutboxDispatchService>();
        return services;
    }

    /// <summary>
    /// Configures outbox retry and dead-letter policy options.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Retry policy configuration callback.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddOutboxRetryPolicy(
        this IServiceCollection services,
        Action<OutboxRetryPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OutboxRetryPolicyOptions();
        configure(options);
        services.Replace(ServiceDescriptor.Singleton(options));
        return services;
    }

    /// <summary>
    /// Replaces the default in-memory outbox with a persistent SharpCoreDB-backed store.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="tableName">Outbox table name.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddPersistentOutbox(
        this IServiceCollection services,
        string tableName = "scdb_outbox")
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IOutboxStore>(sp =>
        {
            var database = sp.GetRequiredService<IDatabase>();
            var retryPolicy = sp.GetService<OutboxRetryPolicyOptions>();
            return new SharpCoreDbOutboxStore(database, tableName, retryPolicy);
        }));
        return services;
    }

    /// <summary>
    /// Registers the hosted outbox dispatch background worker.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Optional options configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddOutboxWorker(
        this IServiceCollection services,
        Action<OutboxHostedServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxHostedServiceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.AddHostedService<OutboxDispatchBackgroundService>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="IOutboxEventPublisher"/> bridge that converts domain events
    /// from an <see cref="AggregateRoot"/> into outbox messages for reliable delivery.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddOutboxEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOutboxEventPublisher, OutboxEventPublisher>();
        return services;
    }

    /// <summary>
    /// Registers an in-memory <see cref="ISnapshotStore"/> for testing and lightweight scenarios.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
        return services;
    }

    /// <summary>
    /// Registers a persistent SharpCoreDB-backed <see cref="ISnapshotStore"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="tableName">Snapshot table name.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddPersistentSnapshotStore(
        this IServiceCollection services,
        string tableName = "scdb_snapshots")
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<ISnapshotStore>(sp =>
        {
            var database = sp.GetRequiredService<IDatabase>();
            return new SharpCoreDbSnapshotStore(database, tableName);
        }));
        return services;
    }
}
