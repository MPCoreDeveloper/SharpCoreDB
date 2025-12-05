namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for the database engine.
/// </summary>
public interface IDatabase
{
    /// <summary>
    /// Initializes the database with a master password.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <returns>The initialized database instance.</returns>
    IDatabase Initialize(string dbPath, string masterPassword);

    /// <summary>
    /// Executes a SQL command.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    void ExecuteSQL(string sql);

    /// <summary>
    /// Executes a SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    void CreateUser(string username, string password);

    /// <summary>
    /// Logs in a user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>True if login successful.</returns>
    bool Login(string username, string password);

    /// <summary>
    /// Gets query cache statistics.
    /// </summary>
    /// <returns>A tuple containing cache hits, misses, hit rate, and total cached queries.</returns>
    (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics();
}
