// <copyright file="ServerConfiguration.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Configuration options for SharpCoreDB network server.
/// Supports multi-database hosting and secure TLS-only endpoints.
/// </summary>
public sealed class ServerConfiguration
{
    /// <summary>Server name (displayed in monitoring).</summary>
    public required string ServerName { get; init; }

    /// <summary>Bind address (0.0.0.0 for all interfaces).</summary>
    public required string BindAddress { get; init; } = "0.0.0.0";

    /// <summary>gRPC HTTPS port (default: 5001).</summary>
    public required int GrpcPort { get; init; } = 5001;

    /// <summary>Enable gRPC protocol (HTTPS only).</summary>
    public bool EnableGrpc { get; init; } = true;

    /// <summary>Enable HTTP/3 (QUIC) for gRPC endpoint in addition to HTTP/2.</summary>
    public bool EnableGrpcHttp3 { get; init; } = true;

    /// <summary>Enable PostgreSQL-compatible binary protocol listener.</summary>
    public bool EnableBinaryProtocol { get; init; } = true;

    /// <summary>Binary protocol TCP port (PostgreSQL compatible default: 5433).</summary>
    public int BinaryProtocolPort { get; init; } = 5433;

    /// <summary>Enable HTTPS API endpoint for diagnostics and management.</summary>
    public bool EnableHttpsApi { get; init; } = true;

    /// <summary>HTTPS API port (default: 8443).</summary>
    public int HttpsApiPort { get; init; } = 8443;

    /// <summary>Enable WebSocket streaming endpoint on the HTTPS API port.</summary>
    public bool EnableWebSocket { get; init; } = true;

    /// <summary>WebSocket endpoint path (default: /ws).</summary>
    public string WebSocketPath { get; init; } = "/ws";

    /// <summary>Maximum WebSocket message size in bytes (default: 4 MB).</summary>
    public int WebSocketMaxMessageSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>WebSocket keep-alive interval (seconds).</summary>
    public int WebSocketKeepAliveSeconds { get; init; } = 30;

    /// <summary>Enable optional web admin Razor Pages UI (admin role required, HTTPS only, disabled by default).</summary>
    public bool EnableWebAdmin { get; init; } = false;

    /// <summary>Web admin UI base path (default: /admin).</summary>
    public string WebAdminPath { get; init; } = "/admin";

    /// <summary>Maximum concurrent connections.</summary>
    public int MaxConnections { get; init; } = 10000;

    /// <summary>Connection timeout (seconds).</summary>
    public int ConnectionTimeoutSeconds { get; init; } = 300;

    /// <summary>Default database name for clients that do not specify one.</summary>
    public required string DefaultDatabase { get; init; }

    /// <summary>Configured databases hosted by this server.</summary>
    public required List<DatabaseInstanceConfiguration> Databases { get; init; } = [];

    /// <summary>System database settings (master/model/msdb/tempdb style).</summary>
    public SystemDatabasesConfiguration SystemDatabases { get; init; } = new();

    /// <summary>Security configuration.</summary>
    public required SecurityConfiguration Security { get; init; }

    /// <summary>Logging configuration.</summary>
    public LoggingConfiguration Logging { get; init; } = new();

    /// <summary>Performance tuning.</summary>
    public PerformanceConfiguration Performance { get; init; } = new();

    /// <summary>Connection pool configuration.</summary>
    public ConnectionPoolConfiguration ConnectionPool { get; init; } = new();

    /// <summary>Tenant quota policy configuration.</summary>
    public TenantQuotaConfiguration TenantQuotas { get; init; } = new();

    /// <summary>Optional projection runtime configuration for server-hosted projection execution.</summary>
    public ProjectionRuntimeConfiguration Projections { get; init; } = new();
}

/// <summary>
/// Per-database hosted instance configuration.
/// </summary>
public sealed class DatabaseInstanceConfiguration
{
    /// <summary>Logical database name used by clients.</summary>
    public required string Name { get; init; }

    /// <summary>Physical database file path.</summary>
    public required string DatabasePath { get; init; }

    /// <summary>Storage mode (SingleFile, Directory, Columnar).</summary>
    public string StorageMode { get; init; } = "SingleFile";

    /// <summary>Enable encryption for this database.</summary>
    public bool EncryptionEnabled { get; init; }

    /// <summary>Encryption key file path for this database.</summary>
    public string? EncryptionKeyFile { get; init; }

    /// <summary>Resolved encryption master password for this database.</summary>
    public string? EncryptionMasterPassword { get; init; }

    /// <summary>Connection pool size for this database.</summary>
    public int ConnectionPoolSize { get; init; } = 1000;

    /// <summary>Marks this as a system database.</summary>
    public bool IsSystemDatabase { get; init; }

    /// <summary>Marks database as read-only.</summary>
    public bool IsReadOnly { get; init; }
}

/// <summary>
/// Configuration for optional system databases.
/// </summary>
public sealed class SystemDatabasesConfiguration
{
    /// <summary>Enable system databases.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Name of master system database.</summary>
    public string MasterDatabaseName { get; init; } = "master";

    /// <summary>Name of model system database.</summary>
    public string ModelDatabaseName { get; init; } = "model";

    /// <summary>Name of job/agent metadata system database.</summary>
    public string MsdbDatabaseName { get; init; } = "msdb";

    /// <summary>Name of temporary objects system database.</summary>
    public string TempDbDatabaseName { get; init; } = "tempdb";
}

/// <summary>
/// Security configuration for authentication and encryption.
/// </summary>
public sealed class SecurityConfiguration
{
    /// <summary>Enable TLS encryption.</summary>
    public bool TlsEnabled { get; init; } = true;

    /// <summary>Minimum TLS version.</summary>
    public string MinimumTlsVersion { get; init; } = "Tls12";

    /// <summary>TLS certificate file path.</summary>
    public required string TlsCertificatePath { get; init; }

    /// <summary>TLS private key file path.</summary>
    public required string TlsPrivateKeyPath { get; init; }

    /// <summary>JWT secret key for token signing.</summary>
    public required string JwtSecretKey { get; init; }

    /// <summary>JWT token expiration (hours).</summary>
    public int JwtExpirationHours { get; init; } = 24;

    /// <summary>Enable API key authentication.</summary>
    public bool EnableApiKeys { get; init; } = true;

    /// <summary>Configured server users with roles. Loaded from appsettings.json.</summary>
    public List<UserConfiguration> Users { get; init; } = [];

    /// <summary>Enable mutual TLS (client certificate authentication).</summary>
    public bool EnableMutualTls { get; init; }

    /// <summary>
    /// Path to the CA certificate (PEM or PFX) used to validate client certificates.
    /// When set, only clients presenting certificates signed by this CA are accepted.
    /// </summary>
    public string? ClientCaCertificatePath { get; init; }

    /// <summary>
    /// Maps client certificate thumbprints to server roles.
    /// Provides fine-grained, per-certificate access control.
    /// </summary>
    public List<CertificateRoleMapping> CertificateRoleMappings { get; init; } = [];

    /// <summary>Tenant encryption key provider configuration.</summary>
    public TenantEncryptionConfiguration TenantEncryption { get; init; } = new();

    /// <summary>Connection pool configuration.</summary>
    public ConnectionPoolConfiguration ConnectionPool { get; init; } = new();
}

/// <summary>
/// Tenant encryption key management configuration.
/// </summary>
public sealed class TenantEncryptionConfiguration
{
    /// <summary>Enables tenant-dedicated encryption key resolution.</summary>
    public bool Enabled { get; init; }

    /// <summary>When true, tenant databases must resolve to a dedicated key reference.</summary>
    public bool RequireDedicatedTenantKey { get; init; }

    /// <summary>Logical key provider name (used for diagnostics/KMS routing).</summary>
    public string DefaultProvider { get; init; } = "configuration";

    /// <summary>Fallback default key reference when tenant-specific mapping is missing.</summary>
    public string? DefaultKeyReference { get; init; }

    /// <summary>Tenant to default encryption key reference mapping.</summary>
    public Dictionary<string, string> DefaultKeyReferenceByTenant { get; init; } = [];

    /// <summary>Key reference to key material mapping (for local/dev usage).</summary>
    public Dictionary<string, string> KeyMaterialByReference { get; init; } = [];
}

/// <summary>
/// Maps a client certificate thumbprint to a <see cref="DatabaseRole"/>.
/// </summary>
public sealed class CertificateRoleMapping
{
    /// <summary>SHA-256 thumbprint of the client certificate (hex, case-insensitive).</summary>
    public required string Thumbprint { get; init; }

    /// <summary>Assigned role: admin, writer, or reader.</summary>
    public string Role { get; init; } = "reader";

    /// <summary>Optional display name for this certificate.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// A configured server user with credentials and role assignment.
/// Passwords are stored as SHA-256 hex hashes in configuration.
/// </summary>
public sealed class UserConfiguration
{
    /// <summary>Login username (case-insensitive).</summary>
    public required string Username { get; init; }

    /// <summary>SHA-256 hex hash of the password.</summary>
    public required string PasswordHash { get; init; }

    /// <summary>Assigned role: admin, writer, or reader.</summary>
    public string Role { get; init; } = "reader";

    /// <summary>Tenant ID to embed in JWT scope claims (default: "default").</summary>
    public string TenantId { get; init; } = "default";

    /// <summary>Allowed databases for JWT scope claims (default: wildcard access).</summary>
    public List<string> AllowedDatabases { get; init; } = ["*"];

    /// <summary>JWT scope claim version emitted for this user (default: "1").</summary>
    public string ScopeVersion { get; init; } = "1";
}

/// <summary>
/// Logging configuration.
/// </summary>
public sealed class LoggingConfiguration
{
    /// <summary>Log level (Trace, Debug, Information, Warning, Error, Critical).</summary>
    public string Level { get; init; } = "Information";

    /// <summary>Log file path.</summary>
    public string FilePath { get; init; } = "/var/log/sharpcoredb/server.log";

    /// <summary>Maximum log file size (MB).</summary>
    public int MaxFileSizeMb { get; init; } = 100;

    /// <summary>Maximum number of log files to retain.</summary>
    public int MaxFiles { get; init; } = 30;

    /// <summary>Enable structured logging.</summary>
    public bool StructuredLogging { get; init; } = true;
}

/// <summary>
/// Performance tuning configuration.
/// </summary>
public sealed class PerformanceConfiguration
{
    /// <summary>Query plan cache size (MB).</summary>
    public int QueryCacheSizeMb { get; init; } = 256;

    /// <summary>Connection pool idle timeout (seconds).</summary>
    public int ConnectionPoolMaxIdleTimeSeconds { get; init; } = 300;

    /// <summary>Maximum concurrent queries.</summary>
    public int MaxConcurrentQueries { get; init; } = 500;

    /// <summary>Memory limit (MB).</summary>
    public long MemoryLimitMb { get; init; } = 4096;

    /// <summary>CPU limit (cores).</summary>
    public int CpuLimitCores { get; init; } = 4;
}

/// <summary>
/// Configuration for connection pool settings.
/// </summary>
public sealed class ConnectionPoolConfiguration
{
    /// <summary>Minimum pool size per database.</summary>
    public int MinPoolSize { get; init; } = 2;

    /// <summary>Maximum pool size per database.</summary>
    public int MaxPoolSize { get; init; } = 1000;

    /// <summary>Idle timeout in seconds before eviction.</summary>
    public int IdleTimeoutSeconds { get; init; } = 300;

    /// <summary>Acquire timeout in seconds.</summary>
    public int AcquireTimeoutSeconds { get; init; } = 30;

    /// <summary>Enable health checks.</summary>
    public bool EnableHealthChecks { get; init; } = true;

    /// <summary>Health check interval in seconds.</summary>
    public int HealthCheckIntervalSeconds { get; init; } = 60;
}

/// <summary>
/// Tenant quota configuration with global defaults and plan-tier overrides.
/// </summary>
public sealed class TenantQuotaConfiguration
{
    /// <summary>Enable tenant quota enforcement.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Default maximum active sessions per tenant.</summary>
    public int DefaultMaxActiveSessions { get; init; } = 100;

    /// <summary>Default maximum requests per second per tenant.</summary>
    public int DefaultMaxRequestsPerSecond { get; init; } = 500;

    /// <summary>Default maximum storage in MB per tenant database.</summary>
    public long DefaultMaxStorageMb { get; init; } = 4096;

    /// <summary>Default maximum batch size per tenant request.</summary>
    public int DefaultMaxBatchSize { get; init; } = 1000;

    /// <summary>Optional plan-tier quota overrides keyed by plan tier.</summary>
    public Dictionary<string, TenantPlanQuotaConfiguration> PlanDefaults { get; init; } = [];
}

/// <summary>
/// Quota values for a specific plan tier.
/// </summary>
public sealed class TenantPlanQuotaConfiguration
{
    /// <summary>Maximum active sessions per tenant.</summary>
    public int MaxActiveSessions { get; init; } = 100;

    /// <summary>Maximum requests per second.</summary>
    public int MaxRequestsPerSecond { get; init; } = 500;

    /// <summary>Maximum storage in MB.</summary>
    public long MaxStorageMb { get; init; } = 4096;

    /// <summary>Maximum batch size.</summary>
    public int MaxBatchSize { get; init; } = 1000;
}

/// <summary>
/// Optional projection runtime configuration for SharpCoreDB.Server.
/// All features remain disabled by default to preserve optional package behavior.
/// </summary>
public sealed class ProjectionRuntimeConfiguration
{
    /// <summary>Enable projection runtime wiring in server startup.</summary>
    public bool Enabled { get; init; }

    /// <summary>Enable hosted background projection worker execution.</summary>
    public bool EnableHostedWorker { get; init; }

    /// <summary>Enable persistent checkpoint storage in SharpCoreDB tables.</summary>
    public bool UsePersistentCheckpoints { get; init; }

    /// <summary>Enable OpenTelemetry-backed projection metrics adapter.</summary>
    public bool UseOpenTelemetryMetrics { get; init; }

    /// <summary>Database name used by projection runtime. Defaults to server default database when empty.</summary>
    public string? DatabaseName { get; init; }

    /// <summary>Checkpoint table name used when persistent checkpoints are enabled.</summary>
    public string CheckpointTableName { get; init; } = "scdb_projection_checkpoints";

    /// <summary>Logical projection runtime database scope id.</summary>
    public string RuntimeDatabaseId { get; init; } = "main";

    /// <summary>Logical projection runtime tenant scope id.</summary>
    public string RuntimeTenantId { get; init; } = "default";

    /// <summary>Initial global sequence when no checkpoint exists.</summary>
    public long FromGlobalSequence { get; init; } = 1;

    /// <summary>Projection batch size for hosted worker execution.</summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>Projection hosted worker poll interval in milliseconds.</summary>
    public int PollIntervalMilliseconds { get; init; } = 250;

    /// <summary>Run a projection cycle immediately on hosted worker start.</summary>
    public bool RunOnStart { get; init; } = true;

    /// <summary>Maximum hosted worker iterations. Null means continuous execution until cancellation.</summary>
    public int? MaxIterations { get; init; }
}
