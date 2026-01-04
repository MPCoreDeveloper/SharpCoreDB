// <copyright file="SecurityConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

/// <summary>
/// Configuration options for database security.
/// </summary>
public class SecurityConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether to block DROP statements.
    /// </summary>
    public bool BlockDropStatements { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to enable query validation.
    /// </summary>
    public bool EnableQueryValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to allow subqueries in WHERE clauses.
    /// </summary>
    public bool AllowSubqueries { get; set; } = true;

    /// <summary>
    /// Gets default security configuration.
    /// </summary>
    public static SecurityConfig Default => new();
}
