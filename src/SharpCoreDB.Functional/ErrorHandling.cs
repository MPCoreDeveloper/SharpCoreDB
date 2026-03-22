namespace SharpCoreDB.Functional;

using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

/// <summary>
/// Helpers for converting runtime errors into <see cref="Fin{A}"/> values.
/// </summary>
public static class ErrorHandling
{
    /// <summary>
    /// Converts an exception into a failed <see cref="Fin{A}"/>.
    /// </summary>
    /// <typeparam name="T">The successful value type.</typeparam>
    /// <param name="exception">The exception to wrap.</param>
    /// <returns>A failed <see cref="Fin{A}"/> instance.</returns>
    public static Fin<T> Fail<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Fin<T>.Fail(Error.New(exception));
    }

    /// <summary>
    /// Produces a successful unit result.
    /// </summary>
    /// <returns>A successful <see cref="Fin{A}"/> carrying <see cref="Unit"/>.</returns>
    public static Fin<Unit> SuccessUnit() => Fin<Unit>.Succ(unit);
}
