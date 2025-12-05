using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a SharpCoreDB command for Entity Framework Core.
/// </summary>
public class SharpCoreDBCommand : DbCommand
{
    private readonly SharpCoreDBConnection _connection;
    private string _commandText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBCommand class.
    /// </summary>
    public SharpCoreDBCommand(SharpCoreDBConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <inheritdoc />
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    /// <inheritdoc />
    public override int CommandTimeout { get; set; } = 30;

    /// <inheritdoc />
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <inheritdoc />
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection { get; } = new SharpCoreDBParameterCollection();

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction { get; set; }

    /// <inheritdoc />
    public override void Cancel()
    {
        // Not supported
    }

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open.");

        if (_connection.DbInstance == null)
            throw new InvalidOperationException("Database instance is not initialized.");

        _connection.DbInstance.ExecuteSQL(_commandText);
        return 1; // Simplified: return 1 for success
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        if (reader.Read() && reader.FieldCount > 0)
        {
            return reader.GetValue(0);
        }
        return null;
    }

    /// <inheritdoc />
    public override void Prepare()
    {
        // Not supported for SharpCoreDB
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter()
    {
        return new SharpCoreDBParameter();
    }

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open.");

        if (_connection.DbInstance == null)
            throw new InvalidOperationException("Database instance is not initialized.");

        // For now, execute and return empty reader
        // In a full implementation, this would parse and return query results
        _connection.DbInstance.ExecuteSQL(_commandText);
        return new SharpCoreDBDataReader();
    }
}

/// <summary>
/// Parameter collection for SharpCoreDB commands.
/// </summary>
public class SharpCoreDBParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

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

    public override void AddRange(Array values) => _parameters.AddRange(values.Cast<DbParameter>());
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
/// Parameter for SharpCoreDB commands.
/// </summary>
public class SharpCoreDBParameter : DbParameter
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
