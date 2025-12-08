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
public class UserService : IUserService
{
    private readonly ICryptoService crypto;
    private readonly IStorage storage;
    private readonly string dbPath;
    private Dictionary<string, UserCredentials> users = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UserService"/> class.
    /// </summary>
    /// <param name="crypto">The crypto service.</param>
    /// <param name="storage">The storage service.</param>
    /// <param name="dbPath">The database path.</param>
    public UserService(ICryptoService crypto, IStorage storage, string dbPath)
    {
        this.crypto = crypto;
        this.storage = storage;
        this.dbPath = dbPath;
        this.LoadUsers();
    }

    private void LoadUsers()
    {
        var path = Path.Combine(this.dbPath, "users.json");
        var data = this.storage.Read(path);
        if (data != null)
        {
            try
            {
                this.users = JsonSerializer.Deserialize<Dictionary<string, UserCredentials>>(data) ?? new();
            }
            catch
            {
                this.users = new();
            }
        }
    }

    private void SaveUsers()
    {
        var path = Path.Combine(this.dbPath, "users.json");
        this.storage.Write(path, JsonSerializer.Serialize(this.users));
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password)
    {
        // SECURITY FIX: Generate cryptographically random salt per user
        var salt = new byte[16]; // 128-bit random salt
        RandomNumberGenerator.Fill(salt);
        var saltBase64 = Convert.ToBase64String(salt);
        
        var hash = Convert.ToBase64String(this.crypto.DeriveKey(password, saltBase64));
        this.users[username] = new UserCredentials { Hash = hash, Salt = saltBase64 };
        this.SaveUsers();
    }

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        if (!this.users.ContainsKey(username))
        {
            return false;
        }

        var userCreds = this.users[username];
        var hash = Convert.ToBase64String(this.crypto.DeriveKey(password, userCreds.Salt));
        if (userCreds.Hash == hash)
        {
            this.CurrentUser = username;
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
internal class UserCredentials
{
    public string Hash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
}
