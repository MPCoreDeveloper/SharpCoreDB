using System.Diagnostics.CodeAnalysis;

namespace SharpCoreDB.Functional;

/// <summary>
/// Represents the result of an operation — either a success value or an <see cref="Error"/>.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public readonly struct Fin<T> : IEquatable<Fin<T>>
{
    private readonly T? _value;
    private readonly Error? _error;
    private readonly bool _isSucc;

    private Fin(T value)
    {
        _value = value;
        _error = null;
        _isSucc = true;
    }

    private Fin(Error error)
    {
        _value = default;
        _error = error;
        _isSucc = false;
    }

    /// <summary>
    /// Creates a success result wrapping <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="Fin{T}"/>.</returns>
    public static Fin<T> Succ(T value) => new(value);

    /// <summary>
    /// Creates a failure result wrapping <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed <see cref="Fin{T}"/>.</returns>
    public static Fin<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    /// <summary>
    /// Gets whether this result is a success.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsSucc => _isSucc;

    /// <summary>
    /// Gets whether this result is a failure.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_error))]
    public bool IsFail => !_isSucc;

    /// <summary>
    /// Pattern-matches on this result, returning a value from the appropriate branch.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="Succ">Function invoked on success.</param>
    /// <param name="Fail">Function invoked on failure.</param>
    /// <returns>The result of the matched branch.</returns>
    public TResult Match<TResult>(Func<T, TResult> Succ, Func<Error, TResult> Fail) =>
        _isSucc ? Succ(_value) : Fail(_error);

    /// <summary>
    /// Async pattern-match on this result.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="Succ">Async function invoked on success.</param>
    /// <param name="Fail">Async function invoked on failure.</param>
    /// <returns>The result of the matched branch.</returns>
    public Task<TResult> Match<TResult>(Func<T, Task<TResult>> Succ, Func<Error, Task<TResult>> Fail) =>
        _isSucc ? Succ(_value) : Fail(_error);

    /// <summary>
    /// Pattern-matches on this result, executing the appropriate action.
    /// </summary>
    /// <param name="Succ">Action invoked on success.</param>
    /// <param name="Fail">Action invoked on failure.</param>
    public void Match(Action<T> Succ, Action<Error> Fail)
    {
        if (_isSucc)
            Succ(_value);
        else
            Fail(_error);
    }

    /// <summary>
    /// Transforms the success value using <paramref name="map"/>.
    /// Failures pass through unchanged.
    /// </summary>
    /// <typeparam name="TResult">The mapped type.</typeparam>
    /// <param name="map">The mapping function.</param>
    /// <returns>A new result with the mapped success value, or the original failure.</returns>
    public Fin<TResult> Map<TResult>(Func<T, TResult> map) =>
        _isSucc ? Fin<TResult>.Succ(map(_value)) : Fin<TResult>.Fail(_error);

    /// <summary>
    /// Executes <paramref name="action"/> if this result is a success.
    /// </summary>
    /// <param name="action">The action to execute on the success value.</param>
    public void IfSucc(Action<T> action)
    {
        if (_isSucc)
            action(_value);
    }

    /// <summary>
    /// Executes <paramref name="action"/> if this result is a failure.
    /// </summary>
    /// <param name="action">The action to execute on the error.</param>
    public void IfFail(Action<Error> action)
    {
        if (!_isSucc)
            action(_error);
    }

    /// <summary>
    /// Flat-maps the success value using <paramref name="bind"/>.
    /// Failures pass through unchanged.
    /// </summary>
    /// <typeparam name="TResult">The bound type.</typeparam>
    /// <param name="bind">The binding function.</param>
    /// <returns>The result of the bind, or the original failure.</returns>
    public Fin<TResult> Bind<TResult>(Func<T, Fin<TResult>> bind) =>
        _isSucc ? bind(_value) : Fin<TResult>.Fail(_error);

    /// <summary>
    /// Returns the success value or invokes <paramref name="handler"/> on the error.
    /// Prefer <see cref="Match{TResult}(Func{T, TResult}, Func{Error, TResult})"/> over this method.
    /// </summary>
    /// <param name="handler">Handler invoked when this result is a failure.</param>
    /// <returns>The success value or the handler result.</returns>
    public T IfFail(Func<Error, T> handler) =>
        _isSucc ? _value : handler(_error);

    /// <summary>
    /// Implicit conversion from a value to a success result.
    /// </summary>
    public static implicit operator Fin<T>(T value) => Succ(value);

    /// <summary>
    /// Implicit conversion from an <see cref="Error"/> to a failure result.
    /// </summary>
    public static implicit operator Fin<T>(Error error) => Fail(error);

    /// <inheritdoc />
    public bool Equals(Fin<T> other) =>
        _isSucc == other._isSucc &&
        (_isSucc
            ? EqualityComparer<T>.Default.Equals(_value, other._value)
            : EqualityComparer<Error>.Default.Equals(_error, other._error));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fin<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _isSucc
            ? HashCode.Combine(true, _value)
            : HashCode.Combine(false, _error);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Fin<T> left, Fin<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Fin<T> left, Fin<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        _isSucc ? $"Succ({_value})" : $"Fail({_error})";
}
