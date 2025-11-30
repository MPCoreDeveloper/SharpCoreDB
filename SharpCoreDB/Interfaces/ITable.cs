namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for a database table.
/// </summary>
public interface ITable
{
    /// <summary>
    /// The table name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The column names.
    /// </summary>
    List<string> Columns { get; }

    /// <summary>
    /// The column types.
    /// </summary>
    List<DataType> ColumnTypes { get; }

    /// <summary>
    /// The data file path.
    /// </summary>
    string DataFile { get; }

    /// <summary>
    /// The primary key column index.
    /// </summary>
    int PrimaryKeyIndex { get; }

    /// <summary>
    /// Whether columns are auto-generated.
    /// </summary>
    List<bool> IsAuto { get; }

    /// <summary>
    /// Inserts a row into the table.
    /// </summary>
    /// <param name="row">The row data.</param>
    void Insert(Dictionary<string, object> row);

    /// <summary>
    /// Selects rows from the table with optional filtering and ordering.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="orderBy">The column to order by.</param>
    /// <param name="asc">Whether to order ascending.</param>
    /// <returns>The selected rows.</returns>
    List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true);

    /// <summary>
    /// Updates rows in the table.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="updates">The updates to apply.</param>
    void Update(string where, Dictionary<string, object> updates);

    /// <summary>
    /// Deletes rows from the table.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    void Delete(string where);
}
