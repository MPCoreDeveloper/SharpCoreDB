using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SharpCoreDB.CrudApp.Configuration;
using SharpCoreDB.Identity.Options;
using SharpCoreDB.Identity.Security;
using SharpCoreDB.Identity.Storage;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.CrudApp.Services;

/// <summary>
/// Manages encrypted SharpCoreDB lifecycle operations for startup schema creation and development reset.
/// </summary>
public sealed class SharpCoreCrudDatabaseService(
    DatabaseFactory databaseFactory,
    IOptions<SharpCoreDbAppOptions> databaseOptions,
    IOptions<SharpCoreIdentityOptions> identityOptions,
    IdentityDatabaseInitializer identityInitializer,
    SharpCoreDbPasswordHasher passwordHasher,
    ILogger<SharpCoreCrudDatabaseService> logger)
{
    private readonly DatabaseFactory _databaseFactory = databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory));
    private readonly SharpCoreDbAppOptions _databaseOptions = databaseOptions?.Value ?? throw new ArgumentNullException(nameof(databaseOptions));
    private readonly SharpCoreIdentityOptions _identityOptions = identityOptions?.Value ?? throw new ArgumentNullException(nameof(identityOptions));
    private readonly IdentityDatabaseInitializer _identityInitializer = identityInitializer ?? throw new ArgumentNullException(nameof(identityInitializer));
    private readonly SharpCoreDbPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    private readonly ILogger<SharpCoreCrudDatabaseService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _syncLock = new();

    /// <summary>
    /// Creates a new encrypted single-file database instance.
    /// </summary>
    public IDatabase CreateDatabase()
    {
        ValidateConfiguration();

        var absolutePath = GetAbsolutePath();
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        var encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(_databaseOptions.EncryptionPassword));
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = encryptionKey,
            EnableMemoryMapping = true,
            AutoVacuum = true,
            AutoVacuumMode = VacuumMode.Quick,
            CreateImmediately = true,
            FileShareMode = FileShare.ReadWrite,
            DatabaseConfig = DatabaseConfig.Default
        };

        return _databaseFactory.CreateWithOptions(absolutePath, _databaseOptions.MasterPassword, options);
    }

    /// <summary>
    /// Ensures identity schema and CRUD demo tables exist.
    /// </summary>
    public async Task EnsureInitializedAsync(bool seedAdminUser, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var database = CreateDatabase();

        await _identityInitializer.EnsureInitializedAsync(database, cancellationToken).ConfigureAwait(false);
        await EnsureDemoSchemaAsync(database, cancellationToken).ConfigureAwait(false);
        await SeedCategoriesAsync(database, cancellationToken).ConfigureAwait(false);

        if (seedAdminUser)
        {
            await SeedAdminUserAsync(database, cancellationToken).ConfigureAwait(false);
        }

        database.Flush();
        database.ForceSave();
    }

    /// <summary>
    /// Resets the encrypted `.scdb` file and recreates all application schema.
    /// </summary>
    public async Task ResetDatabaseAsync(bool seedAdminUser, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var absolutePath = GetAbsolutePath();

        lock (_syncLock)
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }

        _logger.LogWarning("Development reset executed. Database file '{DatabaseFile}' was deleted.", absolutePath);

        await EnsureInitializedAsync(seedAdminUser, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDemoSchemaAsync(IDatabase database, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> statements =
        [
            "CREATE TABLE IF NOT EXISTS Categories (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Description TEXT, IsActive INTEGER NOT NULL)",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Categories_Name ON Categories(Name)",
            "CREATE TABLE IF NOT EXISTS Products (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Description TEXT, Price DECIMAL(18,2) NOT NULL, StockQuantity INTEGER NOT NULL, CategoryId TEXT NOT NULL, IsActive INTEGER NOT NULL, CreatedDate TEXT NOT NULL, LastUpdatedDate TEXT NOT NULL)",
            "CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId)",
            "CREATE INDEX IF NOT EXISTS IX_Products_Name ON Products(Name)"
        ];

        await database.ExecuteBatchSQLAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedCategoriesAsync(IDatabase database, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = database.ExecuteQuery("SELECT Id FROM Categories");
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var categories = new[] { "Electronics", "Books", "Home", "Food" };
        List<string> statements = [];

        foreach (var name in categories)
        {
            var categoryId = Guid.NewGuid();
            statements.Add($"INSERT INTO Categories (Id, Name, Description, IsActive) VALUES ('{categoryId:D}', '{EscapeSql(name)}', '{EscapeSql($"{name} category")}', 1)");
            statements.Add($"INSERT INTO Products (Id, Name, Description, Price, StockQuantity, CategoryId, IsActive, CreatedDate, LastUpdatedDate) VALUES ('{Guid.NewGuid():D}', '{EscapeSql($"Sample {name} Item")}', '{EscapeSql("Demo seeded product")}', 19.99, 10, '{categoryId:D}', 1, '{now:O}', '{now:O}')");
        }

        await database.ExecuteBatchSQLAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedAdminUserAsync(IDatabase database, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedAdmin = _identityOptions.Normalize("admin");
        var usersTable = _identityOptions.UsersTableName;
        var existingAdmin = database.ExecuteQuery($"SELECT Id FROM {usersTable} WHERE NormalizedUserName = '{EscapeSql(normalizedAdmin)}'");

        if (existingAdmin.Count > 0)
        {
            return;
        }

        var passwordHash = _passwordHasher.HashPassword("Admin123!");
        var userId = Guid.NewGuid();
        var securityStamp = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var concurrencyStamp = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var statement = $"INSERT INTO {usersTable} (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FullName, BirthDate, IsActive) VALUES ('{userId:D}', 'admin', '{EscapeSql(normalizedAdmin)}', 'admin@localhost', '{EscapeSql(_identityOptions.Normalize("admin@localhost"))}', 1, '{EscapeSql(passwordHash)}', '{securityStamp}', '{concurrencyStamp}', NULL, 0, 0, NULL, 1, 0, 'System Administrator', '2000-01-01', 1)";

        await database.ExecuteBatchSQLAsync([statement], cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Seeded development admin user with username 'admin'.");
    }

    private string GetAbsolutePath()
    {
        return Path.IsPathRooted(_databaseOptions.DatabaseFilePath)
            ? _databaseOptions.DatabaseFilePath
            : Path.Combine(AppContext.BaseDirectory, _databaseOptions.DatabaseFilePath);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_databaseOptions.DatabaseFilePath))
        {
            throw new InvalidOperationException("SharpCoreDb:DatabaseFilePath is required.");
        }

        if (string.IsNullOrWhiteSpace(_databaseOptions.EncryptionPassword))
        {
            throw new InvalidOperationException("SharpCoreDb:EncryptionPassword is required. Use user-secrets in development.");
        }

        if (string.IsNullOrWhiteSpace(_databaseOptions.MasterPassword))
        {
            throw new InvalidOperationException("SharpCoreDb:MasterPassword is required. Use user-secrets in development.");
        }
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
