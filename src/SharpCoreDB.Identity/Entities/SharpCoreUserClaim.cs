namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents a user claim.
/// </summary>
public sealed record class SharpCoreUserClaim
{
    /// <summary>Gets or sets the claim identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the user identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the claim type.</summary>
    public required string ClaimType { get; set; }

    /// <summary>Gets or sets the claim value.</summary>
    public string? ClaimValue { get; set; }
}
