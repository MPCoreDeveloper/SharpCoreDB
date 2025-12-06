using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

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
    [AllowNull]
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
    protected override DbConnection DbConnection
    {
        get => _connection!;
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
    private readonly List<DbParameter> _parameters = [];

    /// <summary>Gets the number of parameters in the collection.</summary>
    public override int Count => _parameters.Count;
    /// <summary>Gets the synchronization root.</summary>
    public override object SyncRoot => _parameters;

    /// <summary>Adds a parameter to the collection.</summary>
    /// <param name="value">The parameter to add.</param>
    /// <returns>The index of the added parameter.</returns>
    public override int Add([AllowNull] object value)
    {
        if (value is DbParameter param)
        {
            _parameters.Add(param);
            return _parameters.Count - 1;
        }
        throw new ArgumentException("Value must be a DbParameter");
    }

    /// <summary>Adds a range of parameters to the collection.</summary>
    /// <param name="values">The parameters to add.</param>
    public override void AddRange(Array values) => _parameters.AddRange(values.Cast<DbParameter>());
    /// <summary>Clears the collection.</summary>
    public override void Clear() => _parameters.Clear();
    /// <summary>Determines whether the collection contains the specified parameter.</summary>
    /// <param name="value">The parameter to check.</param>
    /// <returns>True if the parameter is in the collection, otherwise false.</returns>
    public override bool Contains(object value) => value is DbParameter param && _parameters.Contains(param);
    /// <summary>Determines whether the collection contains a parameter with the specified name.</summary>
    /// <param name="value">The parameter name to check.</param>
    /// <returns>True if the parameter is in the collection, otherwise false.</returns>
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    /// <summary>Copies the parameters to an array.</summary>
    /// <param name="array">The array to copy to.</param>
    /// <param name="index">The starting index.</param>
    public override void CopyTo(Array array, int index) => throw new NotImplementedException();
    /// <summary>Gets an enumerator for the collection.</summary>
    /// <returns>An enumerator.</returns>
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    /// <summary>Gets the index of the specified parameter.</summary>
    /// <param name="value">The parameter to find.</param>
    /// <returns>The index of the parameter.</returns>
    public override int IndexOf(object value) => value is DbParameter param ? _parameters.IndexOf(param) : -1;
    /// <summary>Gets the index of the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>The index of the parameter.</returns>
    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
    /// <summary>Inserts a parameter at the specified index.</summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="value">The parameter to insert.</param>
    public override void Insert(int index, [AllowNull] object value)
    {
        if (value is DbParameter param)
            _parameters.Insert(index, param);
    }
    /// <summary>Removes the specified parameter.</summary>
    /// <param name="value">The parameter to remove.</param>
    public override void Remove([AllowNull] object value)
    {
        if (value is DbParameter param)
            _parameters.Remove(param);
    }
    /// <summary>Removes the parameter at the specified index.</summary>
    /// <param name="index">The index to remove at.</param>
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    /// <summary>Removes the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            RemoveAt(index);
    }
    /// <summary>Gets the parameter at the specified index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The parameter.</returns>
    protected override DbParameter GetParameter(int index) => _parameters[index];
    /// <summary>Gets the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>The parameter.</returns>
    protected override DbParameter GetParameter(string parameterName)
    {
        return _parameters.FirstOrDefault(p => p.ParameterName == parameterName)
               ?? throw new ArgumentException($"Parameter {parameterName} not found");
    }
    /// <summary>Sets the parameter at the specified index.</summary>
    /// <param name="index">The index.</param>
    /// <param name="value">The parameter.</param>
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    /// <summary>Sets the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The parameter.</param>
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
    /// <summary>Gets or sets the database type.</summary>
    public override DbType DbType { get; set; }
    /// <summary>Gets or sets the parameter direction.</summary>
    public override ParameterDirection Direction { get; set; }
    /// <summary>Gets or sets whether the parameter is nullable.</summary>
    public override bool IsNullable { get; set; }
    /// <summary>Gets or sets the parameter name.</summary>
    public override string ParameterName { get; set; } = string.Empty;
    /// <summary>Gets or sets the source column.</summary>
    public override string SourceColumn { get; set; } = string.Empty;
    /// <summary>Gets or sets the parameter value.</summary>
    public override object? Value { get; set; }
    /// <summary>Gets or sets whether the source column null mapping is used.</summary>
    public override bool SourceColumnNullMapping { get; set; }
    /// <summary>Gets or sets the parameter size.</summary>
    public override int Size { get; set; }

    /// <summary>Resets the database type to string.</summary>
    public override void ResetDbType()
    {
        DbType = DbType.String;
    }
}
