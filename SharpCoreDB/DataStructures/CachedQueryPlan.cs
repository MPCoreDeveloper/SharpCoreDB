// <copyright file="CachedQueryPlan.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a cached query execution plan for prepared statements.
/// </summary>
public record CachedQueryPlan(string Sql, string[] Parts);
