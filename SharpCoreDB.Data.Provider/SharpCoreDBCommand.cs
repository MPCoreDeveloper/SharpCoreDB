using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Represents a SQL command to execute against a SharpCoreDB database.
/// Modern C# 14 implementation with pattern matching and improved null handling.
/// </summary>
public sealed class SharpCoreDBCommand : DbCommand
{
    private int _commandTimeout = 30;
    private readonly SharpCoreDBParameterCollection _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommand"/> class.
    /// </summary>
    public SharpCoreDBCommand()
    {
        _parameters = [];
        CommandType = CommandType.Text;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommand"/> class with command text.
    /// </summary>
    public SharpCoreDBCommand(string commandText) : this()
    {
        CommandText = commandText;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommand"/> class with command text and connection.
    /// </summary>
    public SharpCoreDBCommand(string commandText, SharpCoreDBConnection connection) : this(commandText)
    {
        Connection = connection;
    }

    /// <summary>
    /// Gets or sets the SQL command text.
    /// </summary>
    [AllowNull]
    public override string CommandText { get; set; }

    /// <summary>
    /// Gets or sets the command timeout in seconds.
    /// </summary>
    public override int CommandTimeout
    {
        get => _commandTimeout;
        set => _commandTimeout = value >= 0 ? value : throw new ArgumentException("Timeout must be non-negative.");
    }

    /// <summary>
    /// Gets or sets the command type. Only CommandType.Text is supported.
    /// </summary>
    public override CommandType CommandType { get; set; }

    /// <summary>
    /// Gets or sets how command results are applied to the DataRow.
    /// </summary>
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <summary>
    /// Gets or sets the connection used by this command.
    /// </summary>
    protected override DbConnection? DbConnection
    {
        get => Connection;
        set => Connection = (SharpCoreDBConnection?)value;
    }

    /// <summary>
    /// Gets or sets the connection used by this command (typed).
    /// </summary>
    public new SharpCoreDBConnection? Connection { get; set; }

    /// <summary>
    /// Gets the parameter collection.
    /// </summary>
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <summary>
    /// Gets the parameter collection (typed).
    /// </summary>
    public new SharpCoreDBParameterCollection Parameters => _parameters;

    /// <summary>
    /// Gets or sets the transaction for this command.
    /// </summary>
    protected override DbTransaction? DbTransaction
    {
        get => Transaction;
        set => Transaction = (SharpCoreDBTransaction?)value;
    }

    /// <summary>
    /// Gets or sets the transaction for this command (typed).
    /// </summary>
    public new SharpCoreDBTransaction? Transaction { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the command is design-time visible.
    /// </summary>
    public override bool DesignTimeVisible { get; set; }

    /// <summary>
    /// Cancels the command execution. Not supported.
    /// </summary>
    public override void Cancel()
    {
        // SharpCoreDB doesn't support cancellation
    }

    /// <summary>
    /// Prepares the command for execution.
    /// </summary>
    public override void Prepare()
    {
        // SharpCoreDB handles optimization internally
    }

    /// <summary>
    /// Executes a SQL statement and returns the number of rows affected.
    /// </summary>
    public override int ExecuteNonQuery()
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            if (parameters.Count > 0)
                db.ExecuteSQL(CommandText!, parameters);
            else
                db.ExecuteSQL(CommandText!);

            // SharpCoreDB doesn't return rows affected, return -1 as per ADO.NET convention
            return -1;
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing non-query command.", ex);
        }
    }

    /// <summary>
    /// Executes a SQL statement and returns a data reader.
    /// </summary>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            List<Dictionary<string, object>> results;
            if (parameters.Count > 0)
                results = db.ExecuteQuery(CommandText!, parameters);
            else
                results = db.ExecuteQuery(CommandText!);

            return new SharpCoreDBDataReader(results, behavior);
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing data reader command.", ex);
        }
    }

    /// <summary>
    /// Executes a SQL statement and returns the first column of the first row.
    /// </summary>
    public override object? ExecuteScalar()
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            List<Dictionary<string, object>> results;
            if (parameters.Count > 0)
                results = db.ExecuteQuery(CommandText!, parameters);
            else
                results = db.ExecuteQuery(CommandText!);

            if (results.Count == 0)
                return null;

            var firstRow = results[0];
            if (firstRow.Count == 0)
                return null;

            return firstRow.Values.First();
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing scalar command.", ex);
        }
    }

    /// <summary>
    /// Asynchronously executes a SQL statement and returns the number of rows affected.
    /// </summary>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            if (parameters.Count > 0)
                await db.ExecuteSQLAsync(CommandText!, parameters, cancellationToken);
            else
                await db.ExecuteSQLAsync(CommandText!, cancellationToken);

            return -1;
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing non-query command asynchronously.", ex);
        }
    }

    /// <summary>
    /// Asynchronously executes a SQL statement and returns the first column of the first row.
    /// </summary>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            // ExecuteQuery doesn't have async version, so use Task.Run
            List<Dictionary<string, object>> results = await Task.Run(() =>
            {
                if (parameters.Count > 0)
                    return db.ExecuteQuery(CommandText!, parameters);
                else
                    return db.ExecuteQuery(CommandText!);
            }, cancellationToken);

            if (results.Count == 0)
                return null;

            var firstRow = results[0];
            if (firstRow.Count == 0)
                return null;

            return firstRow.Values.First();
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing scalar command asynchronously.", ex);
        }
    }

    /// <summary>
    /// Asynchronously executes a SQL statement and returns a data reader.
    /// </summary>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        ValidateCommand();

        try
        {
            var db = Connection!.DbInstance!;
            var parameters = BuildParameterDictionary();

            // ExecuteQuery doesn't have async version, so use Task.Run
            var results = await Task.Run(() =>
            {
                if (parameters.Count > 0)
                    return db.ExecuteQuery(CommandText!, parameters);
                else
                    return db.ExecuteQuery(CommandText!);
            }, cancellationToken);

            return new SharpCoreDBDataReader(results, behavior);
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Error executing data reader command asynchronously.", ex);
        }
    }

    /// <summary>
    /// Creates a new parameter.
    /// </summary>
    protected override DbParameter CreateDbParameter()
    {
        return new SharpCoreDBParameter();
    }

    private void ValidateCommand()
    {
        if (Connection is null)
            throw new InvalidOperationException("Connection property has not been initialized.");

        if (Connection.State != ConnectionState.Open)
            throw new InvalidOperationException($"Connection must be Open. Current state: {Connection.State}");

        if (string.IsNullOrWhiteSpace(CommandText))
            throw new InvalidOperationException("CommandText property has not been initialized.");

        if (CommandType != CommandType.Text)
            throw new NotSupportedException($"CommandType {CommandType} is not supported. Only CommandType.Text is supported.");
    }

    private Dictionary<string, object?> BuildParameterDictionary()
    {
        return new Dictionary<string, object?>(
            _parameters.Cast<SharpCoreDBParameter>()
                .Select(param => new KeyValuePair<string, object?>(
                    param.ParameterName?.TrimStart('@') ?? string.Empty,
                    param.Value
                ))
        );
    }

    /// <summary>
    /// Disposes the command.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _parameters?.Clear();
        }
        base.Dispose(disposing);
    }
}
