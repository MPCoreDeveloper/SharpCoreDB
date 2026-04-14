namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents an application user stored in SharpCoreDB identity tables.
/// </summary>
public sealed record class SharpCoreUser
{
    /// <summary>Gets or sets the unique user identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the user name.</summary>
    public required string UserName { get; set; }

    /// <summary>Gets or sets the normalized user name.</summary>
    public required string NormalizedUserName { get; set; }

    /// <summary>Gets or sets the email address.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the normalized email address.</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>Gets or sets a value indicating whether the email is confirmed.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>Gets or sets the password hash.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Gets or sets the security stamp.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the concurrency stamp.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the phone number.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Gets or sets a value indicating whether the phone number is confirmed.</summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>Gets or sets a value indicating whether two-factor authentication is enabled.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Gets or sets the lockout end instant.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Gets or sets a value indicating whether lockout is enabled.</summary>
    public bool LockoutEnabled { get; set; } = true;

    /// <summary>Gets or sets the number of access failures.</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>Gets or sets the full name.</summary>
    public string? FullName { get; set; }

    /// <summary>Gets or sets the birth date.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Gets or sets a value indicating whether the user is active.</summary>
    public bool IsActive { get; set; } = true;
}
