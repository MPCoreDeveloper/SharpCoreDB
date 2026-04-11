// <copyright file="DatabaseRegistryRuntimeTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Validates runtime attach/detach semantics for <see cref="DatabaseRegistry"/>.
/// </summary>
public sealed class DatabaseRegistryRuntimeTests : IAsyncDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-registry-runtime", Guid.NewGuid().ToString("N"));

    public DatabaseRegistryRuntimeTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task RegisterDatabaseAsync_WhenCalled_RegistersDatabase()
    {
        await using var registry = CreateRegistry();

        await registry.RegisterDatabaseAsync("tenant_a", Path.Combine(_tempRoot, "tenant_a.db"));

        Assert.True(registry.DatabaseExists("tenant_a"));
    }

    [Fact]
    public async Task RegisterDatabaseAsync_WhenDuplicate_ThrowsInvalidOperationException()
    {
        await using var registry = CreateRegistry();

        await registry.RegisterDatabaseAsync("tenant_dup", Path.Combine(_tempRoot, "tenant_dup.db"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RegisterDatabaseAsync("tenant_dup", Path.Combine(_tempRoot, "tenant_dup_2.db")));
    }

    [Fact]
    public async Task UnregisterDatabaseAsync_WhenCalled_RemovesOnlyTargetDatabase()
    {
        await using var registry = CreateRegistry();

        await registry.RegisterDatabaseAsync("tenant_left", Path.Combine(_tempRoot, "tenant_left.db"));
        await registry.RegisterDatabaseAsync("tenant_keep", Path.Combine(_tempRoot, "tenant_keep.db"));

        await registry.UnregisterDatabaseAsync("tenant_left");

        Assert.False(registry.DatabaseExists("tenant_left"));
        Assert.True(registry.DatabaseExists("tenant_keep"));
    }

    [Fact]
    public async Task RegisterDatabaseAsync_WhenConcurrentDuplicateName_OnlyOneRegistrationSucceeds()
    {
        await using var registry = CreateRegistry();

        var outcomes = await Task.WhenAll([
            TryRegisterAsync(registry, "tenant_race", Path.Combine(_tempRoot, "tenant_race_1.db")),
            TryRegisterAsync(registry, "tenant_race", Path.Combine(_tempRoot, "tenant_race_2.db"))
        ]);

        Assert.Equal(1, outcomes.Count(static outcome => outcome));
        Assert.True(registry.DatabaseExists("tenant_race"));
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }

        return ValueTask.CompletedTask;
    }

    private DatabaseRegistry CreateRegistry()
    {
        var config = new ServerConfiguration
        {
            ServerName = "RegistryRuntimeTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "master",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(_tempRoot, "master.db"),
                },
            ],
            Security = new SecurityConfiguration
            {
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };

        return new DatabaseRegistry(Options.Create(config), NullLogger<DatabaseRegistry>.Instance);
    }

    private static async Task<bool> TryRegisterAsync(DatabaseRegistry registry, string databaseName, string databasePath)
    {
        try
        {
            await registry.RegisterDatabaseAsync(databaseName, databasePath);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
