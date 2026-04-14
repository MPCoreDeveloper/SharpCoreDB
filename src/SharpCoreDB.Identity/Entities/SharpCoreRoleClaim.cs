namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents a role claim.
/// </summary>
public sealed record class SharpCoreRoleClaim
{
    /// <summary>Gets or sets the claim identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the role identifier.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Gets or sets the claim type.</summary>
    public required string ClaimType { get; set; }

    /// <summary>Gets or sets the claim value.</summary>
    public string? ClaimValue { get; set; }
}
