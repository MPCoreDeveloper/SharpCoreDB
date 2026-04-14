namespace SharpCoreDB.Identity.Entities;

/// <summary>
/// Represents the many-to-many relation between users and roles.
/// </summary>
public sealed record class SharpCoreUserRole(Guid UserId, Guid RoleId);
