// <copyright file="TenantQuotaEnforcementTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Tests for tenant quota enforcement threshold and burst scenarios.
/// </summary>
public sealed class TenantQuotaEnforcementTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TenantQuotaEnforcementService _service;

    public TenantQuotaEnforcementTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-tenant-quotas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var config = new ServerConfiguration
        {
            ServerName = "QuotaTest",
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
            TenantQuotas = new TenantQuotaConfiguration
            {
                Enabled = true,
                DefaultMaxActiveSessions = 1,
                DefaultMaxRequestsPerSecond = 2,
                DefaultMaxStorageMb = 1,
                DefaultMaxBatchSize = 3,
            },
        };

        _service = new TenantQuotaEnforcementService(
            Options.Create(config),
            new MetricsCollector("quota-tests"),
            NullLogger<TenantQuotaEnforcementService>.Instance);
    }

    [Fact]
    public async Task EnsureSessionQuotaAsync_WhenThresholdReached_Throws()
    {
        await Assert.ThrowsAsync<TenantQuotaExceededException>(() =>
            _service.EnsureSessionQuotaAsync("tenant-a", currentActiveSessions: 1, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRequestQuotaAsync_WhenBurstExceedsQps_Throws()
    {
        await _service.EnsureRequestQuotaAsync("tenant-a", "REST/query", cancellationToken: CancellationToken.None);
        await _service.EnsureRequestQuotaAsync("tenant-a", "REST/query", cancellationToken: CancellationToken.None);

        await Assert.ThrowsAsync<TenantQuotaExceededException>(() =>
            _service.EnsureRequestQuotaAsync("tenant-a", "REST/query", cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRequestQuotaAsync_WhenBatchSizeExceeded_Throws()
    {
        await Assert.ThrowsAsync<TenantQuotaExceededException>(() =>
            _service.EnsureRequestQuotaAsync("tenant-a", "REST/batch", batchSize: 4, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureStorageQuotaAsync_WhenStorageExceeded_Throws()
    {
        var dbPath = Path.Combine(_tempRoot, "tenant-storage.db");

        using (var stream = File.Create(dbPath))
        {
            stream.SetLength(2 * 1024 * 1024); // 2MB > 1MB limit
        }

        await Assert.ThrowsAsync<TenantQuotaExceededException>(() =>
            _service.EnsureStorageQuotaAsync("tenant-a", dbPath, "REST/execute", CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
