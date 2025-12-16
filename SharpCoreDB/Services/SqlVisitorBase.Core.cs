// <copyright file="SqlVisitorBase.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Base visitor with error recovery capabilities - Core partial class.
/// Contains fields, properties, and error handling infrastructure.
/// Implements try/catch handling for each visit method to prevent crashes.
/// </summary>
/// <typeparam name="TResult">The return type of visit operations.</typeparam>
public abstract partial class SqlVisitorBase<TResult> : ISqlVisitor<TResult>
{
    private readonly List<string> _errors = []; // ✅ C# 14: Collection expression
    private readonly bool _throwOnError;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlVisitorBase{TResult}"/> class.
    /// </summary>
    /// <param name="throwOnError">Whether to throw exceptions on errors (default: false).</param>
    protected SqlVisitorBase(bool throwOnError = false)
    {
        _throwOnError = throwOnError;
    }

    /// <summary>
    /// Gets the list of errors encountered during visitation.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets whether any errors were encountered.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Clears all recorded errors.
    /// </summary>
    public void ClearErrors() => _errors.Clear();

    /// <summary>
    /// Records an error message.
    /// </summary>
    protected void RecordError(string message, SqlNode? node = null)
    {
        var errorMsg = node is not null // ✅ C# 14: is not null pattern
            ? $"[Position {node.Position}] {message}"
            : message;
        _errors.Add(errorMsg);
    }

    /// <summary>
    /// Safely executes a visit operation with error recovery.
    /// </summary>
    protected TResult SafeVisit(Func<TResult> visitFunc, string context, SqlNode? node = null)
    {
        try
        {
            return visitFunc();
        }
        catch (Exception ex)
        {
            RecordError($"{context}: {ex.Message}", node);
            if (_throwOnError)
                throw;
            return default!;
        }
    }
}
