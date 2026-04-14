namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents an application role.
/// </summary>
public sealed record class SharpCoreRole
{
    /// <summary>Gets or sets the role identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the role name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the normalized role name.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>Gets or sets the concurrency stamp.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
