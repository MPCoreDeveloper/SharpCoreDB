using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Data adapter for SharpCoreDB, enabling DataSet/DataTable operations.
/// Required for SQL Server Management Studio (SSMS) integration.
/// Modern C# 14 implementation.
/// </summary>
public sealed class SharpCoreDBDataAdapter : DbDataAdapter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataAdapter"/> class.
    /// </summary>
    public SharpCoreDBDataAdapter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataAdapter"/> class with a select command.
    /// </summary>
    public SharpCoreDBDataAdapter(SharpCoreDBCommand selectCommand)
    {
        SelectCommand = selectCommand;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataAdapter"/> class with a select statement and connection.
    /// </summary>
    public SharpCoreDBDataAdapter(string selectCommandText, SharpCoreDBConnection connection)
    {
        SelectCommand = new SharpCoreDBCommand(selectCommandText, connection);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataAdapter"/> class with a select statement and connection string.
    /// </summary>
    public SharpCoreDBDataAdapter(string selectCommandText, string connectionString)
    {
        var connection = new SharpCoreDBConnection(connectionString);
        SelectCommand = new SharpCoreDBCommand(selectCommandText, connection);
    }

    /// <summary>
    /// Gets or sets the SELECT command (typed).
    /// </summary>
    public new SharpCoreDBCommand? SelectCommand
    {
        get => (SharpCoreDBCommand?)base.SelectCommand;
        set => base.SelectCommand = value;
    }

    /// <summary>
    /// Gets or sets the INSERT command (typed).
    /// </summary>
    public new SharpCoreDBCommand? InsertCommand
    {
        get => (SharpCoreDBCommand?)base.InsertCommand;
        set => base.InsertCommand = value;
    }

    /// <summary>
    /// Gets or sets the UPDATE command (typed).
    /// </summary>
    public new SharpCoreDBCommand? UpdateCommand
    {
        get => (SharpCoreDBCommand?)base.UpdateCommand;
        set => base.UpdateCommand = value;
    }

    /// <summary>
    /// Gets or sets the DELETE command (typed).
    /// </summary>
    public new SharpCoreDBCommand? DeleteCommand
    {
        get => (SharpCoreDBCommand?)base.DeleteCommand;
        set => base.DeleteCommand = value;
    }
}
