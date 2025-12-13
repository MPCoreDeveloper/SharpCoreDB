// <copyright file="IUserService.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for user management including creation and authentication.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Creates a new user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    void CreateUser(string username, string password);

    /// <summary>
    /// Authenticates a user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>True if authenticated, false otherwise.</returns>
    bool Login(string username, string password);

    /// <summary>
    /// Gets the current logged-in user.
    /// </summary>
    string? CurrentUser { get; }
}
