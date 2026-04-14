namespace SharpCoreDB.Identity.Storage;

using SharpCoreDB.Identity.Options;
using SharpCoreDB.Interfaces;

/// <summary>
/// Creates and validates required SharpCoreDB identity schema objects.
/// </summary>
public sealed class IdentityDatabaseInitializer(SharpCoreIdentityOptions options)
{
    private readonly SharpCoreIdentityOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Creates identity tables and indexes if missing.
    /// </summary>
    /// <param name="database">The SharpCoreDB database instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnsureInitializedAsync(IDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        cancellationToken.ThrowIfCancellationRequested();

        var users = _options.UsersTableName;
        var roles = _options.RolesTableName;
        var userRoles = _options.UserRolesTableName;
        var userClaims = _options.UserClaimsTableName;
        var userLogins = _options.UserLoginsTableName;
        var roleClaims = _options.RoleClaimsTableName;

        var statements = new List<string>
        {
            $"CREATE TABLE IF NOT EXISTS {users} (Id TEXT PRIMARY KEY, UserName TEXT NOT NULL, NormalizedUserName TEXT NOT NULL, Email TEXT, NormalizedEmail TEXT, EmailConfirmed INTEGER NOT NULL, PasswordHash TEXT NOT NULL, SecurityStamp TEXT NOT NULL, ConcurrencyStamp TEXT NOT NULL, PhoneNumber TEXT, PhoneNumberConfirmed INTEGER NOT NULL, TwoFactorEnabled INTEGER NOT NULL, LockoutEnd TEXT, LockoutEnabled INTEGER NOT NULL, AccessFailedCount INTEGER NOT NULL, FullName TEXT, BirthDate TEXT, IsActive INTEGER NOT NULL)",
            $"CREATE TABLE IF NOT EXISTS {roles} (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, NormalizedName TEXT NOT NULL, ConcurrencyStamp TEXT NOT NULL)",
            $"CREATE TABLE IF NOT EXISTS {userRoles} (UserId TEXT NOT NULL, RoleId TEXT NOT NULL, PRIMARY KEY (UserId, RoleId))",
            $"CREATE TABLE IF NOT EXISTS {userClaims} (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, ClaimType TEXT NOT NULL, ClaimValue TEXT)",
            $"CREATE TABLE IF NOT EXISTS {userLogins} (LoginProvider TEXT NOT NULL, ProviderKey TEXT NOT NULL, ProviderDisplayName TEXT, UserId TEXT NOT NULL, PRIMARY KEY (LoginProvider, ProviderKey))",
            $"CREATE TABLE IF NOT EXISTS {roleClaims} (Id TEXT PRIMARY KEY, RoleId TEXT NOT NULL, ClaimType TEXT NOT NULL, ClaimValue TEXT)",
            $"CREATE UNIQUE INDEX IF NOT EXISTS IX_{users}_NormalizedUserName ON {users}(NormalizedUserName)",
            $"CREATE UNIQUE INDEX IF NOT EXISTS IX_{users}_NormalizedEmail ON {users}(NormalizedEmail)",
            $"CREATE UNIQUE INDEX IF NOT EXISTS IX_{roles}_NormalizedName ON {roles}(NormalizedName)",
            $"CREATE INDEX IF NOT EXISTS IX_{userRoles}_RoleId ON {userRoles}(RoleId)",
            $"CREATE INDEX IF NOT EXISTS IX_{userClaims}_UserId ON {userClaims}(UserId)",
            $"CREATE INDEX IF NOT EXISTS IX_{roleClaims}_RoleId ON {roleClaims}(RoleId)"
        };

        await database.ExecuteBatchSQLAsync(statements, cancellationToken).ConfigureAwait(false);
        database.Flush();
        database.ForceSave();
    }
}
