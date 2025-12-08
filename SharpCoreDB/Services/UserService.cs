// <copyright file="UserService.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Text.Json;

/// <summary>
/// Implementation of IUserService.
/// </summary>
public class UserService : IUserService
{
    private readonly ICryptoService crypto;
    private readonly IStorage storage;
    private readonly string dbPath;
    private Dictionary<string, string> users = new();

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
                this.users = JsonSerializer.Deserialize<Dictionary<string, string>>(data) ?? new();
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
        var hash = Convert.ToBase64String(this.crypto.DeriveKey(password, username));
        this.users[username] = hash;
        this.SaveUsers();
    }

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        if (!this.users.ContainsKey(username))
        {
            return false;
        }

        var hash = Convert.ToBase64String(this.crypto.DeriveKey(password, username));
        if (this.users[username] == hash)
        {
            this.CurrentUser = username;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public string? CurrentUser { get; private set; }
}
