// <copyright file="MvccQueryable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Linq;

using SharpCoreDB.MVCC;
using System.Collections;
using System.Linq.Expressions;

/// <summary>
/// Generic queryable implementation that integrates with MVCC.
/// Provides LINQ support with type-safe queries and snapshot isolation.
/// </summary>
/// <typeparam name="TKey">The type of the row key.</typeparam>
/// <typeparam name="TData">The type of the row data.</typeparam>
public sealed class MvccQueryable<TKey, TData> : IOrderedQueryable<TData>
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    where TData : class
{
    private readonly MvccManager<TKey, TData> _mvccManager;
    private readonly MvccTransaction _transaction;
    private readonly Expression _expression;
    private readonly MvccQueryProvider<TKey, TData> _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MvccQueryable{TKey, TData}"/> class.
    /// </summary>
    public MvccQueryable(
        MvccManager<TKey, TData> mvccManager,
        MvccTransaction transaction)
    {
        _mvccManager = mvccManager;
        _transaction = transaction;
        _provider = new MvccQueryProvider<TKey, TData>(mvccManager, transaction);
        _expression = Expression.Constant(this);
    }

    /// <summary>
    /// Initializes a new instance with a custom expression.
    /// </summary>
    internal MvccQueryable(
        MvccManager<TKey, TData> mvccManager,
        MvccTransaction transaction,
        Expression expression)
    {
        _mvccManager = mvccManager;
        _transaction = transaction;
        _provider = new MvccQueryProvider<TKey, TData>(mvccManager, transaction);
        _expression = expression;
    }

    /// <inheritdoc/>
    public Type ElementType => typeof(TData);

    /// <inheritdoc/>
    public Expression Expression => _expression;

    /// <inheritdoc/>
    public IQueryProvider Provider => _provider;

    /// <inheritdoc/>
    public IEnumerator<TData> GetEnumerator()
    {
        // Execute the query and return results
        return _provider.Execute<IEnumerable<TData>>(_expression).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Query provider that translates LINQ to SQL and executes via MVCC.
/// </summary>
internal sealed class MvccQueryProvider<TKey, TData> : IQueryProvider
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    where TData : class
{
    private readonly MvccManager<TKey, TData> _mvccManager;
    private readonly MvccTransaction _transaction;
    private readonly GenericLinqToSqlTranslator<TData> _translator;

    public MvccQueryProvider(
        MvccManager<TKey, TData> mvccManager,
        MvccTransaction transaction)
    {
        _mvccManager = mvccManager;
        _transaction = transaction;
        _translator = new GenericLinqToSqlTranslator<TData>();
    }

    /// <inheritdoc/>
    public IQueryable CreateQuery(Expression expression)
    {
        return new MvccQueryable<TKey, TData>(_mvccManager, _transaction, expression);
    }

    /// <inheritdoc/>
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (typeof(TElement) != typeof(TData))
        {
            throw new NotSupportedException($"Cannot create query for type {typeof(TElement)}");
        }

        return (IQueryable<TElement>)(object)new MvccQueryable<TKey, TData>(
            _mvccManager, _transaction, expression);
    }

    /// <inheritdoc/>
    public object? Execute(Expression expression)
    {
        return Execute<object>(expression);
    }

    /// <inheritdoc/>
    public TResult Execute<TResult>(Expression expression)
    {
        // Translate LINQ expression to SQL
        var (sql, parameters) = _translator.Translate(expression);

        Console.WriteLine($"[LINQ-to-SQL] {sql}");
        Console.WriteLine($"[Parameters] {string.Join(", ", parameters.Select(p => p?.ToString() ?? "NULL"))}");

        // For now, execute via MVCC Scan and apply filters in memory
        // TODO: Integrate with SQL execution engine when available
        var results = _mvccManager.Scan(_transaction);

        // Apply any in-memory filters that couldn't be translated to SQL
        // This is a fallback - ideally everything should be in SQL

        if (typeof(TResult).IsAssignableTo(typeof(IEnumerable)))
        {
            return (TResult)(object)results;
        }

        // For single-value results (Count, Any, etc.)
        if (typeof(TResult) == typeof(int))
        {
            return (TResult)(object)results.Count();
        }

        if (typeof(TResult) == typeof(bool))
        {
            return (TResult)(object)results.Any();
        }

        // For First/Single
        if (typeof(TResult) == typeof(TData))
        {
            return (TResult)(object)results.First()!;
        }

        throw new NotSupportedException($"Result type {typeof(TResult)} not supported");
    }
}
