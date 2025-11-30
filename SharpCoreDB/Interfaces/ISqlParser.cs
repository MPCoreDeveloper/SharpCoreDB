namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for parsing and executing SQL commands.
/// </summary>
public interface ISqlParser
{
    /// <summary>
    /// Executes a SQL command.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="wal">The WAL for logging.</param>
    void Execute(string sql, IWAL? wal = null);
}
