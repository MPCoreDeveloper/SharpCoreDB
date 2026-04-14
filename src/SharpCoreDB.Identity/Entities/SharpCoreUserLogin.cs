namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents an external login mapped to a user.
/// </summary>
public sealed record class SharpCoreUserLogin
{
    /// <summary>Gets or sets the login provider.</summary>
    public required string LoginProvider { get; set; }

    /// <summary>Gets or sets the provider key.</summary>
    public required string ProviderKey { get; set; }

    /// <summary>Gets or sets the provider display name.</summary>
    public string? ProviderDisplayName { get; set; }

    /// <summary>Gets or sets the user identifier.</summary>
    public Guid UserId { get; set; }
}
