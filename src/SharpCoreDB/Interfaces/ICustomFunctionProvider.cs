// <copyright file="ICustomFunctionProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Provides custom SQL function evaluation for extension modules (e.g., vector search).
/// Registered via DI — zero overhead when not registered.
/// </summary>
public interface ICustomFunctionProvider
{
    /// <summary>
    /// Returns true if this provider handles the given function name.
    /// </summary>
    /// <param name="functionName">The uppercase function name to check.</param>
    /// <returns>True if this provider can evaluate the function.</returns>
    bool CanHandle(string functionName);

    /// <summary>
    /// Evaluates a custom function with the given arguments.
    /// </summary>
    /// <param name="functionName">The uppercase function name.</param>
    /// <param name="arguments">The function arguments.</param>
    /// <returns>The function result.</returns>
    object? Evaluate(string functionName, List<object?> arguments);

    /// <summary>
    /// Gets all function names this provider handles (for EXPLAIN/metadata).
    /// </summary>
    /// <returns>Read-only list of supported function names.</returns>
    IReadOnlyList<string> GetFunctionNames();
}
