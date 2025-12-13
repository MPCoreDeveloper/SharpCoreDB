// <copyright file="SqlParameter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a SQL parameter for parameterized queries.
/// </summary>
public class SqlParameter
{
    /// <summary>
    /// Gets or sets the parameter name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the parameter value.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlParameter"/> class.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    public SqlParameter(string? name, object? value)
    {
        this.Name = name;
        this.Value = value;
    }
}
