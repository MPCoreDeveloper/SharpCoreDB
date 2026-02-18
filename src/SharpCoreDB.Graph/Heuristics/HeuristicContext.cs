// <copyright file="HeuristicContext.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Heuristics;

using System.Collections.Generic;

/// <summary>
/// Context data passed to custom heuristic functions.
/// ✅ GraphRAG Phase 6.2: Provides domain-specific data for heuristic calculations.
/// </summary>
/// <remarks>
/// <para>Common use cases:</para>
/// <list type="bullet">
/// <item><description><strong>Spatial graphs:</strong> Store node positions as <c>context["positions"]</c></description></item>
/// <item><description><strong>Weighted graphs:</strong> Store edge weights as <c>context["weights"]</c></description></item>
/// <item><description><strong>Domain data:</strong> Store business logic (priority, cost, etc.)</description></item>
/// </list>
/// <para><strong>Example:</strong></para>
/// <code>
/// var context = new HeuristicContext
/// {
///     ["positions"] = new Dictionary&lt;long, (int X, int Y)&gt;
///     {
///         [1] = (0, 0),
///         [2] = (3, 4),
///         [3] = (6, 8)
///     }
/// };
/// </code>
/// </remarks>
public sealed class HeuristicContext : Dictionary<string, object>
{
    /// <summary>
    /// Initializes an empty heuristic context.
    /// </summary>
    public HeuristicContext()
    {
    }

    /// <summary>
    /// Initializes a heuristic context with initial data.
    /// </summary>
    /// <param name="data">Initial context data.</param>
    public HeuristicContext(IDictionary<string, object> data)
        : base(data)
    {
    }

    /// <summary>
    /// Gets a strongly-typed value from the context.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="key">Context key.</param>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    /// <exception cref="KeyNotFoundException">If key doesn't exist.</exception>
    /// <exception cref="InvalidCastException">If value cannot be cast to <typeparamref name="T"/>.</exception>
    public T Get<T>(string key)
    {
        return (T)this[key];
    }

    /// <summary>
    /// Tries to get a strongly-typed value from the context.
    /// </summary>
    /// <typeparam name="T">Expected value type.</typeparam>
    /// <param name="key">Context key.</param>
    /// <param name="value">The value if found, otherwise default.</param>
    /// <returns>True if value was found and successfully cast.</returns>
    public bool TryGet<T>(string key, out T? value)
    {
        if (TryGetValue(key, out var obj))
        {
            try
            {
                value = (T)obj;
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        value = default;
        return false;
    }
}
