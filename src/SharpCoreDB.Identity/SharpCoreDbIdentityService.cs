namespace SharpCoreDB.Identity;

using Microsoft.Extensions.Logging;
using SharpCoreDB.Identity.Entities;
using SharpCoreDB.Identity.Options;
using SharpCoreDB.Identity.Security;
using SharpCoreDB.Identity.Storage;
using SharpCoreDB.Interfaces;

/// <summary>
/// Provides core identity operations for SharpCoreDB-backed applications.
/// </summary>
public sealed class SharpCoreDbIdentityService(
    IDatabase database,
    SharpCoreDbPasswordHasher passwordHasher,
    IdentityDatabaseInitializer databaseInitializer,
    SharpCoreIdentityOptions options,
    ILogger<SharpCoreDbIdentityService>? logger = null)
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly SharpCoreDbPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    private readonly IdentityDatabaseInitializer _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
    private readonly SharpCoreIdentityOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SharpCoreDbTokenProvider _tokenProvider = new(options);
    private readonly ILogger<SharpCoreDbIdentityService> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SharpCoreDbIdentityService>.Instance;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private int _isInitialized;

    /// <summary>
    /// Creates a new user and persists it.
    /// </summary>
    public async Task<SharpCoreUser> CreateUserAsync(SharpCoreUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        ValidatePassword(password);

        var normalizedUserName = _options.Normalize(user.UserName);
        var normalizedEmail = string.IsNullOrWhiteSpace(user.Email) ? null : _options.Normalize(user.Email);

        if (await FindByNameAsync(user.UserName, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"User '{user.UserName}' already exists.");
        }

        if (normalizedEmail is not null && await FindByEmailAsync(user.Email!, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException($"Email '{user.Email}' is already in use.");
        }

        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;
        user.NormalizedUserName = normalizedUserName;
        user.NormalizedEmail = normalizedEmail;
        user.PasswordHash = _passwordHasher.HashPassword(password);
        user.SecurityStamp = string.IsNullOrWhiteSpace(user.SecurityStamp) ? Guid.NewGuid().ToString("N") : user.SecurityStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        user.LockoutEnabled = user.LockoutEnabled || _options.Lockout.AllowedForNewUsers;

        var sql = $"INSERT INTO {_options.UsersTableName} (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FullName, BirthDate, IsActive) VALUES ({SqlString(user.Id.ToString("D"))}, {SqlString(user.UserName)}, {SqlString(user.NormalizedUserName)}, {SqlString(user.Email)}, {SqlString(user.NormalizedEmail)}, {SqlBool(user.EmailConfirmed)}, {SqlString(user.PasswordHash)}, {SqlString(user.SecurityStamp)}, {SqlString(user.ConcurrencyStamp)}, {SqlString(user.PhoneNumber)}, {SqlBool(user.PhoneNumberConfirmed)}, {SqlBool(user.TwoFactorEnabled)}, {SqlString(user.LockoutEnd?.ToString("O"))}, {SqlBool(user.LockoutEnabled)}, {user.AccessFailedCount}, {SqlString(user.FullName)}, {SqlString(user.BirthDate?.ToString("O"))}, {SqlBool(user.IsActive)})";

        await _database.ExecuteSQLAsync(sql, cancellationToken).ConfigureAwait(false);
        _database.Flush();

        return user;
    }

    /// <summary>
    /// Finds a user by identifier.
    /// </summary>
    public async Task<SharpCoreUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var rows = await QueryAsync($"SELECT * FROM {_options.UsersTableName} WHERE Id = {SqlString(userId.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : MapUser(rows[0]);
    }

    /// <summary>
    /// Finds a user by user name.
    /// </summary>
    public async Task<SharpCoreUser?> FindByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var normalizedUserName = _options.Normalize(userName);
        var rows = await QueryAsync($"SELECT * FROM {_options.UsersTableName} WHERE NormalizedUserName = {SqlString(normalizedUserName)}", cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : MapUser(rows[0]);
    }

    /// <summary>
    /// Finds a user by email.
    /// </summary>
    public async Task<SharpCoreUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var normalizedEmail = _options.Normalize(email);
        var rows = await QueryAsync($"SELECT * FROM {_options.UsersTableName} WHERE NormalizedEmail = {SqlString(normalizedEmail)}", cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : MapUser(rows[0]);
    }

    /// <summary>
    /// Verifies whether the supplied password matches the user's stored hash.
    /// </summary>
    public Task<bool> CheckPasswordAsync(SharpCoreUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_passwordHasher.VerifyHashedPassword(user.PasswordHash, password));
    }

    /// <summary>
    /// Changes the password when the current password is valid.
    /// </summary>
    public async Task<bool> ChangePasswordAsync(SharpCoreUser user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!await CheckPasswordAsync(user, currentPassword, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        ValidatePassword(newPassword);
        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        await _database.ExecuteSQLAsync($"UPDATE {_options.UsersTableName} SET PasswordHash = {SqlString(user.PasswordHash)}, SecurityStamp = {SqlString(user.SecurityStamp)}, ConcurrencyStamp = {SqlString(user.ConcurrencyStamp)} WHERE Id = {SqlString(user.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return true;
    }

    /// <summary>
    /// Adds a user to a role.
    /// </summary>
    public async Task AddToRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var normalizedRoleName = _options.Normalize(roleName);
        var roleRows = await QueryAsync($"SELECT * FROM {_options.RolesTableName} WHERE NormalizedName = {SqlString(normalizedRoleName)}", cancellationToken).ConfigureAwait(false);

        var role = roleRows.Count == 0
            ? await CreateRoleAsync(roleName, normalizedRoleName, cancellationToken).ConfigureAwait(false)
            : MapRole(roleRows[0]);

        var assignment = await QueryAsync($"SELECT * FROM {_options.UserRolesTableName} WHERE UserId = {SqlString(userId.ToString("D"))} AND RoleId = {SqlString(role.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        if (assignment.Count > 0)
        {
            return;
        }

        await _database.ExecuteSQLAsync($"INSERT INTO {_options.UserRolesTableName} (UserId, RoleId) VALUES ({SqlString(userId.ToString("D"))}, {SqlString(role.Id.ToString("D"))})", cancellationToken).ConfigureAwait(false);
        _database.Flush();
    }

    /// <summary>
    /// Removes a user from a role.
    /// </summary>
    public async Task RemoveFromRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var normalizedRoleName = _options.Normalize(roleName);
        var roleRows = await QueryAsync($"SELECT * FROM {_options.RolesTableName} WHERE NormalizedName = {SqlString(normalizedRoleName)}", cancellationToken).ConfigureAwait(false);
        if (roleRows.Count == 0)
        {
            return;
        }

        var role = MapRole(roleRows[0]);
        await _database.ExecuteSQLAsync($"DELETE FROM {_options.UserRolesTableName} WHERE UserId = {SqlString(userId.ToString("D"))} AND RoleId = {SqlString(role.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        _database.Flush();
    }

    /// <summary>
    /// Gets role names assigned to the user.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var userRoles = await QueryAsync($"SELECT RoleId FROM {_options.UserRolesTableName} WHERE UserId = {SqlString(userId.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        if (userRoles.Count == 0)
        {
            return [];
        }

        List<string> roles = [];
        foreach (var userRole in userRoles)
        {
            var roleId = AsString(userRole, "RoleId");
            if (string.IsNullOrWhiteSpace(roleId))
            {
                continue;
            }

            var rows = await QueryAsync($"SELECT Name FROM {_options.RolesTableName} WHERE Id = {SqlString(roleId)}", cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                continue;
            }

            var roleName = AsString(rows[0], "Name");
            if (!string.IsNullOrWhiteSpace(roleName))
            {
                roles.Add(roleName);
            }
        }

        return roles;
    }

    /// <summary>
    /// Generates an email confirmation token for the user.
    /// </summary>
    public Task<string> GenerateEmailConfirmationTokenAsync(SharpCoreUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_tokenProvider.GenerateEmailConfirmationToken(user, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Confirms a user's email using a valid token.
    /// </summary>
    public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var user = await FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!_tokenProvider.ValidateToken(token, "email-confirmation", user, DateTimeOffset.UtcNow))
        {
            return false;
        }

        user.EmailConfirmed = true;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        await _database.ExecuteSQLAsync($"UPDATE {_options.UsersTableName} SET EmailConfirmed = 1, ConcurrencyStamp = {SqlString(user.ConcurrencyStamp)} WHERE Id = {SqlString(user.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return true;
    }

    /// <summary>
    /// Generates a password reset token for the user.
    /// </summary>
    public Task<string> GeneratePasswordResetTokenAsync(SharpCoreUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_tokenProvider.GeneratePasswordResetToken(user, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Resets password for a user when token validation succeeds.
    /// </summary>
    public async Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        var user = await FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return false;
        }

        if (!_tokenProvider.ValidateToken(token, "password-reset", user, DateTimeOffset.UtcNow))
        {
            return false;
        }

        ValidatePassword(newPassword);
        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        await _database.ExecuteSQLAsync($"UPDATE {_options.UsersTableName} SET PasswordHash = {SqlString(user.PasswordHash)}, SecurityStamp = {SqlString(user.SecurityStamp)}, ConcurrencyStamp = {SqlString(user.ConcurrencyStamp)} WHERE Id = {SqlString(user.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return true;
    }

    /// <summary>
    /// Attempts to sign in with a user name and password.
    /// </summary>
    public async Task<SharpCoreSignInResult> PasswordSignInAsync(string userName, string password, bool lockoutOnFailure = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var user = await FindByNameAsync(userName, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return SharpCoreSignInResult.Failed;
        }

        if (!user.IsActive)
        {
            return SharpCoreSignInResult.NotAllowed;
        }

        var now = DateTimeOffset.UtcNow;
        if (user.LockoutEnabled && user.LockoutEnd is not null && user.LockoutEnd > now)
        {
            return SharpCoreSignInResult.LockedOut;
        }

        if (_passwordHasher.VerifyHashedPassword(user.PasswordHash, password))
        {
            await _database.ExecuteSQLAsync($"UPDATE {_options.UsersTableName} SET AccessFailedCount = 0, LockoutEnd = NULL WHERE Id = {SqlString(user.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
            _database.Flush();
            return user.EmailConfirmed ? SharpCoreSignInResult.Success : SharpCoreSignInResult.NotAllowed;
        }

        if (!lockoutOnFailure || !user.LockoutEnabled)
        {
            return SharpCoreSignInResult.Failed;
        }

        var failedCount = user.AccessFailedCount + 1;
        DateTimeOffset? lockoutEnd = null;
        if (failedCount >= _options.Lockout.MaxFailedAccessAttempts)
        {
            lockoutEnd = now + _options.Lockout.DefaultLockoutTimeSpan;
            failedCount = 0;
        }

        await _database.ExecuteSQLAsync($"UPDATE {_options.UsersTableName} SET AccessFailedCount = {failedCount}, LockoutEnd = {SqlString(lockoutEnd?.ToString("O"))} WHERE Id = {SqlString(user.Id.ToString("D"))}", cancellationToken).ConfigureAwait(false);
        _database.Flush();

        return lockoutEnd is null ? SharpCoreSignInResult.Failed : SharpCoreSignInResult.LockedOut;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isInitialized) == 1)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized == 1)
            {
                return;
            }

            await _databaseInitializer.EnsureInitializedAsync(_database, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _isInitialized, 1);
            _logger.LogDebug("SharpCoreDB identity schema initialized.");
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task<SharpCoreRole> CreateRoleAsync(string roleName, string normalizedRoleName, CancellationToken cancellationToken)
    {
        var role = new SharpCoreRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = normalizedRoleName,
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

        await _database.ExecuteSQLAsync($"INSERT INTO {_options.RolesTableName} (Id, Name, NormalizedName, ConcurrencyStamp) VALUES ({SqlString(role.Id.ToString("D"))}, {SqlString(role.Name)}, {SqlString(role.NormalizedName)}, {SqlString(role.ConcurrencyStamp)})", cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return role;
    }

    private void ValidatePassword(string password)
    {
        if (password.Length < _options.Password.RequiredLength)
        {
            throw new ArgumentException($"Password must be at least {_options.Password.RequiredLength} characters long.", nameof(password));
        }

        if (_options.Password.RequireDigit && !password.Any(char.IsDigit))
        {
            throw new ArgumentException("Password must contain at least one digit.", nameof(password));
        }

        if (_options.Password.RequireLowercase && !password.Any(char.IsLower))
        {
            throw new ArgumentException("Password must contain at least one lowercase character.", nameof(password));
        }

        if (_options.Password.RequireUppercase && !password.Any(char.IsUpper))
        {
            throw new ArgumentException("Password must contain at least one uppercase character.", nameof(password));
        }

        if (_options.Password.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            throw new ArgumentException("Password must contain at least one non-alphanumeric character.", nameof(password));
        }
    }

    private Task<List<Dictionary<string, object>>> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_database.ExecuteQuery(sql));
    }

    private static SharpCoreUser MapUser(IReadOnlyDictionary<string, object> row)
    {
        return new SharpCoreUser
        {
            Id = Guid.TryParse(AsString(row, "Id"), out var id) ? id : Guid.Empty,
            UserName = AsString(row, "UserName") ?? string.Empty,
            NormalizedUserName = AsString(row, "NormalizedUserName") ?? string.Empty,
            Email = AsString(row, "Email"),
            NormalizedEmail = AsString(row, "NormalizedEmail"),
            EmailConfirmed = AsBoolean(row, "EmailConfirmed"),
            PasswordHash = AsString(row, "PasswordHash") ?? string.Empty,
            SecurityStamp = AsString(row, "SecurityStamp") ?? string.Empty,
            ConcurrencyStamp = AsString(row, "ConcurrencyStamp") ?? string.Empty,
            PhoneNumber = AsString(row, "PhoneNumber"),
            PhoneNumberConfirmed = AsBoolean(row, "PhoneNumberConfirmed"),
            TwoFactorEnabled = AsBoolean(row, "TwoFactorEnabled"),
            LockoutEnd = AsDateTimeOffset(row, "LockoutEnd"),
            LockoutEnabled = AsBoolean(row, "LockoutEnabled"),
            AccessFailedCount = AsInt32(row, "AccessFailedCount"),
            FullName = AsString(row, "FullName"),
            BirthDate = AsDateOnly(row, "BirthDate"),
            IsActive = !row.TryGetValue("IsActive", out var isActiveObj) || AsBooleanValue(isActiveObj)
        };
    }

    private static SharpCoreRole MapRole(IReadOnlyDictionary<string, object> row)
    {
        return new SharpCoreRole
        {
            Id = Guid.TryParse(AsString(row, "Id"), out var id) ? id : Guid.Empty,
            Name = AsString(row, "Name") ?? string.Empty,
            NormalizedName = AsString(row, "NormalizedName") ?? string.Empty,
            ConcurrencyStamp = AsString(row, "ConcurrencyStamp") ?? string.Empty
        };
    }

    private static string SqlString(string? value)
        => value is null ? "NULL" : $"'{value.Replace("'", "''")}'";

    private static string SqlBool(bool value)
        => value ? "1" : "0";

    private static string? AsString(IReadOnlyDictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        return Convert.ToString(value);
    }

    private static bool AsBoolean(IReadOnlyDictionary<string, object> row, string key)
        => row.TryGetValue(key, out var value) && AsBooleanValue(value);

    private static bool AsBooleanValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return false;
        }

        return value switch
        {
            bool boolean => boolean,
            int integer => integer != 0,
            long longValue => longValue != 0,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when int.TryParse(text, out var parsedInt) => parsedInt != 0,
            _ => false
        };
    }

    private static int AsInt32(IReadOnlyDictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return 0;
        }

        return Convert.ToInt32(value);
    }

    private static DateTimeOffset? AsDateTimeOffset(IReadOnlyDictionary<string, object> row, string key)
    {
        var text = AsString(row, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;
    }

    private static DateOnly? AsDateOnly(IReadOnlyDictionary<string, object> row, string key)
    {
        var text = AsString(row, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParse(text, out var dateOnly))
        {
            return dateOnly;
        }

        return DateTimeOffset.TryParse(text, out var dateTimeOffset) ? DateOnly.FromDateTime(dateTimeOffset.DateTime) : null;
    }
}
