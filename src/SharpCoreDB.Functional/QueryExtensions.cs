namespace SharpCoreDB.Functional;

using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

/// <summary>
/// Fluent helpers for Option/Fin query flows.
/// </summary>
public static class QueryExtensions
{
    /// <summary>
    /// Converts an <see cref="Option{A}"/> into a <see cref="Fin{A}"/> with a custom none error.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="option">Option to convert.</param>
    /// <param name="errorFactory">Factory for none-case error.</param>
    /// <returns>A successful or failed <see cref="Fin{A}"/>.</returns>
    public static Fin<T> ToFin<T>(this Option<T> option, Func<Error> errorFactory) =>
        option.Match(
            Some: Fin<T>.Succ,
            None: () => Fin<T>.Fail(errorFactory()));

    /// <summary>
    /// Converts an <see cref="Option{A}"/> into a <see cref="Fin{A}"/> with a fixed message.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="option">Option to convert.</param>
    /// <param name="message">Failure message used when option is none.</param>
    /// <returns>A successful or failed <see cref="Fin{A}"/>.</returns>
    public static Fin<T> ToFin<T>(this Option<T> option, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return option.ToFin(() => Error.New(message));
    }

    /// <summary>
    /// Transforms only successful values while preserving failures.
    /// </summary>
    /// <typeparam name="TIn">Input type.</typeparam>
    /// <typeparam name="TOut">Output type.</typeparam>
    /// <param name="fin">Input result.</param>
    /// <param name="map">Mapping function.</param>
    /// <returns>Mapped result.</returns>
    public static Fin<TOut> MapSuccess<TIn, TOut>(this Fin<TIn> fin, Func<TIn, TOut> map) =>
        fin.Match(
            Succ: value => Fin<TOut>.Succ(map(value)),
            Fail: error => Fin<TOut>.Fail(error));

    /// <summary>
    /// Executes asynchronous work only when an option has a value.
    /// </summary>
    /// <typeparam name="T">Option value type.</typeparam>
    /// <param name="option">Option source.</param>
    /// <param name="onSome">Action for some case.</param>
    /// <param name="onNone">Action for none case.</param>
    /// <returns>A completion task.</returns>
    public static Task IfSomeOrNoneAsync<T>(
        this Option<T> option,
        Func<T, Task> onSome,
        Func<Task> onNone) =>
        option.Match(
            Some: onSome,
            None: onNone);
}
