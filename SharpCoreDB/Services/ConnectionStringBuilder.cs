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
    /// Gets or sets whether the database should be opened in read-only mode.
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets the cache mode (Shared or Private).
    /// </summary>
    public string Cache { get; set; } = "Private";

    /// <summary>
    /// Initializes a new instance of the ConnectionStringBuilder class.
    /// </summary>
    public ConnectionStringBuilder()
    {
    }

    /// <summary>
    /// Initializes a new instance of the ConnectionStringBuilder class with a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public ConnectionStringBuilder(string connectionString)
    {
        ParseConnectionString(connectionString);
    }

    /// <summary>
    /// Parses a connection string and sets the properties.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public void ParseConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length != 2)
                continue;

            var key = keyValue[0].Trim();
            var value = keyValue[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "data source":
                case "datasource":
                    DataSource = value;
                    break;
                case "password":
                case "pwd":
                    Password = value;
                    break;
                case "readonly":
                case "read only":
                    ReadOnly = bool.TryParse(value, out var ro) && ro;
                    break;
                case "cache":
                    Cache = value;
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

        if (!string.IsNullOrEmpty(DataSource))
            parts.Add($"Data Source={DataSource}");

        if (!string.IsNullOrEmpty(Password))
            parts.Add($"Password={Password}");

        if (ReadOnly)
            parts.Add("ReadOnly=True");

        if (!string.IsNullOrEmpty(Cache))
            parts.Add($"Cache={Cache}");

        return string.Join(";", parts);
    }

    /// <summary>
    /// Returns the connection string representation.
    /// </summary>
    /// <returns>The connection string.</returns>
    public override string ToString() => BuildConnectionString();
}
