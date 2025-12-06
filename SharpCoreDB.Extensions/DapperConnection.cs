using SharpCoreDB.Interfaces;
using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides an IDbConnection wrapper for SharpCoreDB that works with Dapper.
/// </summary>
public class DapperConnection : DbConnection
{
    private readonly IDatabase _database;
    private readonly string _connectionString;
    private ConnectionState _state;

    /// <summary>
    /// Initializes a new instance of the DapperConnection class.
    /// </summary>
    /// <param name="database">The SharpCoreDB database instance.</param>
    /// <param name="connectionString">The connection string.</param>
    public DapperConnection(IDatabase database, string connectionString)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _connectionString = connectionString ?? string.Empty;
        _state = ConnectionState.Closed;
    }

    /// <inheritdoc />
    public override string ConnectionString
    {
        get => _connectionString;
        set => throw new NotSupportedException("Cannot change connection string after initialization");
    }

    /// <inheritdoc />
    public override string Database => "SharpCoreDB";

    /// <inheritdoc />
    public override string DataSource => _connectionString;

    /// <inheritdoc />
    public override string ServerVersion => "1.0.0";

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("SharpCoreDB does not support changing databases");
    }

    /// <inheritdoc />
    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    /// <inheritdoc />
    public override void Open()
    {
        if (_state == ConnectionState.Open)
            return;

        _state = ConnectionState.Open;
    }

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotSupportedException("Transactions are not yet implemented in SharpCoreDB");
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand()
    {
        return new DapperCommand(_database) { Connection = this };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _state = ConnectionState.Closed;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Provides a DbCommand implementation for SharpCoreDB.
/// </summary>
internal class DapperCommand : DbCommand
{
    private readonly IDatabase _database;
    private string _commandText = string.Empty;

    public DapperCommand(IDatabase database)
    {
        _database = database;
    }

    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value;
    }

    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new DapperParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
        // Not supported
    }

    public override int ExecuteNonQuery()
    {
        if (string.IsNullOrWhiteSpace(_commandText))
            throw new InvalidOperationException("CommandText is not set");

        _database.ExecuteSQL(_commandText);
        return 1; // Return affected rows (simplified)
    }

    public override object? ExecuteScalar()
    {
        if (string.IsNullOrWhiteSpace(_commandText))
            throw new InvalidOperationException("CommandText is not set");

        _database.ExecuteSQL(_commandText);
        return null; // Simplified implementation
    }

    public override void Prepare()
    {
        // Not supported in this implementation
    }

    protected override DbParameter CreateDbParameter()
    {
        return new DapperParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        throw new NotSupportedException("DataReader not yet implemented for SharpCoreDB");
    }
}

/// <summary>
/// Provides a DbParameterCollection implementation.
/// </summary>
internal class DapperParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;
    public override object SyncRoot => _parameters;

    public override int Add(object value)
    {
        if (value is DbParameter param)
        {
            _parameters.Add(param);
            return _parameters.Count - 1;
        }
        throw new ArgumentException("Value must be a DbParameter");
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value);
        }
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => value is DbParameter param && _parameters.Contains(param);

    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);

    public override void CopyTo(Array array, int index) => throw new NotImplementedException();

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => value is DbParameter param ? _parameters.IndexOf(param) : -1;

    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value)
    {
        if (value is DbParameter param)
            _parameters.Insert(index, param);
    }

    public override void Remove(object value)
    {
        if (value is DbParameter param)
            _parameters.Remove(param);
    }

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            RemoveAt(index);
    }

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName)
    {
        return _parameters.FirstOrDefault(p => p.ParameterName == parameterName)
               ?? throw new ArgumentException($"Parameter {parameterName} not found");
    }

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            _parameters[index] = value;
    }
}

/// <summary>
/// Provides a DbParameter implementation.
/// </summary>
internal class DapperParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; } = string.Empty;
    public override string SourceColumn { get; set; } = string.Empty;
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }

    public override void ResetDbType()
    {
        DbType = DbType.String;
    }
}

/// <summary>
/// Extension methods for Dapper integration.
/// </summary>
public static class DapperExtensions
{
    /// <summary>
    /// Gets an IDbConnection for use with Dapper.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>An IDbConnection instance.</returns>
    public static IDbConnection GetDapperConnection(this IDatabase database, string connectionString = "")
    {
        return new DapperConnection(database, connectionString);
    }
}
