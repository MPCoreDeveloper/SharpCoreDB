// <copyright file="ConnectionStringBuilder.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// Parses and builds connection strings for SharpCoreDB.
/// </summary>
public class ConnectionStringBuilder
{
    /// <summary>
    /// Gets or sets the data source (database path).
    /// </summary>
    public string DataSource { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password for database encryption.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the database should be opened in read-only mode.
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets the cache mode (Shared or Private).
    /// </summary>
    public string Cache { get; set; } = "Private";

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStringBuilder"/> class.
    /// </summary>
    public ConnectionStringBuilder()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStringBuilder"/> class with a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public ConnectionStringBuilder(string connectionString)
    {
        this.ParseConnectionString(connectionString);
    }

    /// <summary>
    /// Parses a connection string and sets the properties.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public void ParseConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length != 2)
            {
                continue;
            }

            var key = keyValue[0].Trim();
            var value = keyValue[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "data source":
                case "datasource":
                    this.DataSource = value;
                    break;
                case "password":
                case "pwd":
                    this.Password = value;
                    break;
                case "readonly":
                case "read only":
                    this.ReadOnly = bool.TryParse(value, out var ro) && ro;
                    break;
                case "cache":
                    this.Cache = value;
                    break;
            }
        }
    }

    /// <summary>
    /// Builds a connection string from the current properties.
    /// </summary>
    /// <returns>The connection string.</returns>
    public string BuildConnectionString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(this.DataSource))
        {
            parts.Add($"Data Source={this.DataSource}");
        }

        if (!string.IsNullOrEmpty(this.Password))
        {
            parts.Add($"Password={this.Password}");
        }

        if (this.ReadOnly)
        {
            parts.Add("ReadOnly=True");
        }

        if (!string.IsNullOrEmpty(this.Cache))
        {
            parts.Add($"Cache={this.Cache}");
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Returns the connection string representation.
    /// </summary>
    /// <returns>The connection string.</returns>
    public override string ToString() => this.BuildConnectionString();
}
