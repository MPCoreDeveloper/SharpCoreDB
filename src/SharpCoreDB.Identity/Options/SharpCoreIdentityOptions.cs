namespace SharpCoreDB.Identity.Options;

using System.Security.Cryptography;

/// <summary>
/// Configures core identity behavior for SharpCoreDB identity services.
/// </summary>
public sealed class SharpCoreIdentityOptions
{
    /// <summary>Gets or sets password policy options.</summary>
    public SharpCorePasswordOptions Password { get; set; } = new();

    /// <summary>Gets or sets lockout policy options.</summary>
    public SharpCoreLockoutOptions Lockout { get; set; } = new();

    /// <summary>Gets or sets token options.</summary>
    public SharpCoreTokenOptions Tokens { get; set; } = new();

    /// <summary>Gets or sets the users table name.</summary>
    public string UsersTableName { get; set; } = "sc_identity_users";

    /// <summary>Gets or sets the roles table name.</summary>
    public string RolesTableName { get; set; } = "sc_identity_roles";

    /// <summary>Gets or sets the user roles table name.</summary>
    public string UserRolesTableName { get; set; } = "sc_identity_user_roles";

    /// <summary>Gets or sets the user claims table name.</summary>
    public string UserClaimsTableName { get; set; } = "sc_identity_user_claims";

    /// <summary>Gets or sets the user logins table name.</summary>
    public string UserLoginsTableName { get; set; } = "sc_identity_user_logins";

    /// <summary>Gets or sets the role claims table name.</summary>
    public string RoleClaimsTableName { get; set; } = "sc_identity_role_claims";

    /// <summary>
    /// Normalizes names and emails for unique lookup consistency.
    /// </summary>
    public string Normalize(string value) => value.Trim().ToUpperInvariant();
}

/// <summary>
/// Configures password hashing and password policy.
/// </summary>
public sealed class SharpCorePasswordOptions
{
    /// <summary>Gets or sets the required password length.</summary>
    public int RequiredLength { get; set; } = 8;

    /// <summary>Gets or sets whether non-alphanumeric chars are required.</summary>
    public bool RequireNonAlphanumeric { get; set; } = true;

    /// <summary>Gets or sets whether uppercase chars are required.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Gets or sets whether lowercase chars are required.</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Gets or sets whether digits are required.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Gets or sets the PBKDF2 iteration count.</summary>
    public int IterationCount { get; set; } = 210_000;

    /// <summary>Gets or sets the salt size in bytes.</summary>
    public int SaltSize { get; set; } = 16;

    /// <summary>Gets or sets the hash size in bytes.</summary>
    public int HashSize { get; set; } = 32;

    /// <summary>Gets or sets the PBKDF2 algorithm.</summary>
    public SharpCorePbkdf2Algorithm Algorithm { get; set; } = SharpCorePbkdf2Algorithm.Sha256;
}

/// <summary>
/// Configures lockout behavior.
/// </summary>
public sealed class SharpCoreLockoutOptions
{
    /// <summary>Gets or sets whether lockout is enabled for new users.</summary>
    public bool AllowedForNewUsers { get; set; } = true;

    /// <summary>Gets or sets the maximum failed access attempts before lockout.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Gets or sets the lockout duration.</summary>
    public TimeSpan DefaultLockoutTimeSpan { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Configures token generation behavior.
/// </summary>
public sealed class SharpCoreTokenOptions
{
    /// <summary>Gets or sets the email confirmation token lifetime.</summary>
    public TimeSpan EmailConfirmationTokenLifespan { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets the password reset token lifetime.</summary>
    public TimeSpan PasswordResetTokenLifespan { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets or sets the HMAC signing key used for token signatures.
    /// Keep this value persistent and at least 32 bytes.
    /// </summary>
    public byte[] TokenSigningKey { get; set; } = RandomNumberGenerator.GetBytes(32);
}

/// <summary>
/// Supported PBKDF2 algorithms.
/// </summary>
public enum SharpCorePbkdf2Algorithm
{
    /// <summary>PBKDF2 with SHA256.</summary>
    Sha256,

    /// <summary>PBKDF2 with SHA512.</summary>
    Sha512
}
