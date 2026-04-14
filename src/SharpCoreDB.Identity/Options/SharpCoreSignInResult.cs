namespace SharpCoreDB.Identity.Options;

/// <summary>
/// Represents a password sign-in outcome.
/// </summary>
public sealed record class SharpCoreSignInResult(bool Succeeded, bool IsLockedOut, bool IsNotAllowed)
{
    /// <summary>Gets a successful sign-in result.</summary>
    public static SharpCoreSignInResult Success { get; } = new(true, false, false);

    /// <summary>Gets a failed sign-in result.</summary>
    public static SharpCoreSignInResult Failed { get; } = new(false, false, false);

    /// <summary>Gets a locked-out sign-in result.</summary>
    public static SharpCoreSignInResult LockedOut { get; } = new(false, true, false);

    /// <summary>Gets a not-allowed sign-in result.</summary>
    public static SharpCoreSignInResult NotAllowed { get; } = new(false, false, true);
}
