// <copyright file="MasterDatabaseLocatorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Pins how the master database for the tenant catalog and database grants repositories is resolved
/// and, more importantly, what an operator is told when it cannot be resolved. A missing or
/// misconfigured master database used to surface as an unhandled HTTP 500 on every request that
/// constructed a controller (the anonymous health endpoint included) with the message
/// "Unable to resolve master database for tenant catalog repository." — a wording that names neither
/// the candidates nor the state of the registry.
/// </summary>
public sealed class MasterDatabaseLocatorTests : IAsyncDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "sharpcoredb-master-database-locator",
        Guid.NewGuid().ToString("N")[..8]);

    public MasterDatabaseLocatorTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task TryResolve_WhenSystemDatabaseRegistered_ReturnsSystemDatabase()
    {
        await using var registry = CreateRegistry();
        await registry.InitializeAsync();
        await registry.RegisterDatabaseAsync("master", Path.Combine(_tempRoot, "master.db"));

        var resolved = MasterDatabaseLocator.TryResolve(registry, "master", "defaultdb");

        Assert.NotNull(resolved);
        Assert.Equal("master", resolved!.Configuration.Name);
    }

    [Fact]
    public async Task TryResolve_WhenOnlyDefaultDatabaseRegistered_FallsBackToDefaultDatabase()
    {
        await using var registry = CreateRegistry();
        await registry.InitializeAsync();
        await registry.RegisterDatabaseAsync("defaultdb", Path.Combine(_tempRoot, "defaultdb.db"));

        var resolved = MasterDatabaseLocator.TryResolve(registry, "master", "defaultdb");

        Assert.NotNull(resolved);
        Assert.Equal("defaultdb", resolved!.Configuration.Name);
    }

    [Fact]
    public async Task TryResolve_WhenNeitherCandidateRegistered_ReturnsNull()
    {
        await using var registry = CreateRegistry();
        await registry.InitializeAsync();
        await registry.RegisterDatabaseAsync("otherdb", Path.Combine(_tempRoot, "otherdb.db"));

        var resolved = MasterDatabaseLocator.TryResolve(registry, "master", "defaultdb");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task TryResolve_WhenSystemDatabaseNameIsBlank_FallsBackToDefaultDatabase()
    {
        await using var registry = CreateRegistry();
        await registry.InitializeAsync();
        await registry.RegisterDatabaseAsync("defaultdb", Path.Combine(_tempRoot, "defaultdb_blank.db"));

        var resolved = MasterDatabaseLocator.TryResolve(registry, "   ", "defaultdb");

        Assert.NotNull(resolved);
        Assert.Equal("defaultdb", resolved!.Configuration.Name);
    }

    [Fact]
    public async Task DescribeUnresolved_WhenNeitherCandidateRegistered_NamesCandidatesRegisteredSetAndState()
    {
        await using var registry = CreateRegistry();
        await registry.InitializeAsync();
        await registry.RegisterDatabaseAsync("otherdb", Path.Combine(_tempRoot, "otherdb_message.db"));

        var message = MasterDatabaseLocator.DescribeUnresolved(registry, "master", "defaultdb");

        Assert.Contains("master", message, StringComparison.Ordinal);
        Assert.Contains("defaultdb", message, StringComparison.Ordinal);
        Assert.Contains("otherdb", message, StringComparison.Ordinal);
        Assert.Contains("is initialized and holds", message, StringComparison.Ordinal);
        Assert.Contains("Server:Databases", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeUnresolved_WhenRegistryNotInitialized_SaysSoExplicitly()
    {
        await using var registry = CreateRegistry();

        var message = MasterDatabaseLocator.DescribeUnresolved(registry, "master", "defaultdb");

        Assert.Contains("is not initialized", message, StringComparison.Ordinal);
        Assert.Contains("<none>", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeUnresolved_WhenNamesAreNotConfigured_MarksThemAsNotConfigured()
    {
        await using var registry = CreateRegistry();

        var message = MasterDatabaseLocator.DescribeUnresolved(registry, null, "  ");

        Assert.Contains("<not configured>", message, StringComparison.Ordinal);
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
            ServerName = "MasterDatabaseLocatorTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "defaultdb",
            Databases = [],
            SystemDatabases = new SystemDatabasesConfiguration { Enabled = false },
            Security = new SecurityConfiguration
            {
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };

        return new DatabaseRegistry(Options.Create(config), NullLogger<DatabaseRegistry>.Instance);
    }
}
