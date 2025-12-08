// <copyright file="UserService.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Text.Json;
using System.Security.Cryptography;

/// <summary>
/// Implementation of IUserService.
/// </summary>
/// <param name="crypto">The crypto service.</param>
/// <param name="storage">The storage service.</param>
/// <param name="dbPath">The database path.</param>
public sealed class UserService(ICryptoService crypto, IStorage storage, string dbPath) : IUserService
{
    private Dictionary<string, UserCredentials> users = LoadUsersInternal(storage, dbPath);

    private static Dictionary<string, UserCredentials> LoadUsersInternal(IStorage storage, string dbPath)
    {
        var path = Path.Combine(dbPath, "users.json");
        var data = storage.Read(path);
        if (data != null)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, UserCredentials>>(data) ?? [];
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    private void SaveUsers()
    {
        var path = Path.Combine(dbPath, "users.json");
        storage.Write(path, JsonSerializer.Serialize(users));
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password)
    {
        // SECURITY FIX: Generate cryptographically random salt per user
        var salt = new byte[16]; // 128-bit random salt
        RandomNumberGenerator.Fill(salt);
        var saltBase64 = Convert.ToBase64String(salt);
        
        var hash = Convert.ToBase64String(crypto.DeriveKey(password, saltBase64));
        users[username] = new UserCredentials { Hash = hash, Salt = saltBase64 };
        SaveUsers();
    }

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        if (!users.TryGetValue(username, out var userCreds))
            return false;

        var hash = Convert.ToBase64String(crypto.DeriveKey(password, userCreds.Salt));
        if (userCreds.Hash == hash)
        {
            CurrentUser = username;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public string? CurrentUser { get; private set; }
}

/// <summary>
/// User credentials with proper salt storage.
/// SECURITY: Each user now has a unique random salt to prevent rainbow table attacks.
/// </summary>
internal sealed class UserCredentials
{
    public required string Hash { get; init; }
    public required string Salt { get; init; }
}
