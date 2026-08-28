using System.Diagnostics.CodeAnalysis;

namespace SharpCoreDB.Functional;

/// <summary>
/// Represents an optional value — either <c>Some(value)</c> or <c>None</c>.
/// </summary>
/// <typeparam name="T">The type of the contained value.</typeparam>
public readonly struct Option<T> : IEquatable<Option<T>>
{
    private readonly T? _value;
    private readonly bool _isSome;

    private Option(T value)
    {
        _value = value;
        _isSome = true;
    }

    /// <summary>
    /// Gets the <c>None</c> instance (no value).
    /// </summary>
    public static Option<T> None => default;

    /// <summary>
    /// Creates a <c>Some</c> instance wrapping the given value.
    /// </summary>
    /// <param name="value">The value to wrap. Must not be null.</param>
    /// <returns>An <see cref="Option{T}"/> containing the value.</returns>
    public static Option<T> Some(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<T>(value);
    }

    /// <summary>
    /// Gets whether this option contains a value.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsSome => _isSome;

    /// <summary>
    /// Gets whether this option is empty.
    /// </summary>
    public bool IsNone => !_isSome;

    /// <summary>
    /// Pattern-matches on this option, returning a result from the appropriate branch.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="Some">Function invoked when a value is present.</param>
    /// <param name="None">Function invoked when no value is present.</param>
    /// <returns>The result of the matched branch.</returns>
    public TResult Match<TResult>(Func<T, TResult> Some, Func<TResult> None) =>
        _isSome ? Some(_value) : None();

    /// <summary>
    /// Async pattern-match on this option.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="Some">Async function invoked when a value is present.</param>
    /// <param name="None">Async function invoked when no value is present.</param>
    /// <returns>The result of the matched branch.</returns>
    public Task<TResult> Match<TResult>(Func<T, Task<TResult>> Some, Func<Task<TResult>> None) =>
        _isSome ? Some(_value) : None();

    /// <summary>
    /// Executes <paramref name="action"/> if this option contains a value.
    /// </summary>
    /// <param name="action">The action to execute on the contained value.</param>
    public void IfSome(Action<T> action)
    {
        if (_isSome)
            action(_value);
    }

    /// <summary>
    /// Executes <paramref name="action"/> if this option is empty.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public void IfNone(Action action)
    {
        if (!_isSome)
            action();
    }

    /// <summary>
    /// Transforms the contained value using <paramref name="map"/>.
    /// Returns <c>None</c> if this option is empty.
    /// </summary>
    /// <typeparam name="TResult">The mapped type.</typeparam>
    /// <param name="map">The mapping function.</param>
    /// <returns>A new option containing the mapped value, or <c>None</c>.</returns>
    public Option<TResult> Map<TResult>(Func<T, TResult> map) =>
        _isSome ? Option<TResult>.Some(map(_value)) : Option<TResult>.None;

    /// <summary>
    /// Flat-maps the contained value using <paramref name="bind"/>.
    /// Returns <c>None</c> if this option is empty.
    /// </summary>
    /// <typeparam name="TResult">The bound type.</typeparam>
    /// <param name="bind">The binding function.</param>
    /// <returns>The result of the bind, or <c>None</c>.</returns>
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bind) =>
        _isSome ? bind(_value) : Option<TResult>.None;

    /// <summary>
    /// Returns the contained value or <paramref name="defaultValue"/> if empty.
    /// </summary>
    /// <param name="defaultValue">The fallback value.</param>
    /// <returns>The contained value or the default.</returns>
    public T IfNone(T defaultValue) =>
        _isSome ? _value : defaultValue;

    /// <summary>
    /// Returns the contained value or invokes <paramref name="defaultFactory"/> if empty.
    /// </summary>
    /// <param name="defaultFactory">Factory for the fallback value.</param>
    /// <returns>The contained value or the factory result.</returns>
    public T IfNone(Func<T> defaultFactory) =>
        _isSome ? _value : defaultFactory();

    /// <summary>
    /// Implicit conversion from a value to <c>Some(value)</c>.
    /// Null values produce <c>None</c>.
    /// </summary>
    public static implicit operator Option<T>(T? value) =>
        value is null ? None : new Option<T>(value);

    /// <inheritdoc />
    public bool Equals(Option<T> other) =>
        _isSome == other._isSome &&
        (!_isSome || EqualityComparer<T>.Default.Equals(_value, other._value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _isSome ? EqualityComparer<T>.Default.GetHashCode(_value) : 0;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        _isSome ? $"Some({_value})" : "None";
}
