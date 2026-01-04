using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Command builder for generating INSERT, UPDATE, and DELETE commands automatically.
/// Modern C# 14 implementation.
/// </summary>
public sealed class SharpCoreDBCommandBuilder : DbCommandBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommandBuilder"/> class.
    /// </summary>
    public SharpCoreDBCommandBuilder()
    {
        QuotePrefix = "[";
        QuoteSuffix = "]";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommandBuilder"/> class with a data adapter.
    /// </summary>
    public SharpCoreDBCommandBuilder(SharpCoreDBDataAdapter adapter) : this()
    {
        DataAdapter = adapter;
    }

    /// <summary>
    /// Gets or sets the data adapter (typed).
    /// </summary>
    public new SharpCoreDBDataAdapter? DataAdapter
    {
        get => (SharpCoreDBDataAdapter?)base.DataAdapter;
        set => base.DataAdapter = value;
    }

    /// <summary>
    /// Applies parameter information to a parameter.
    /// </summary>
    protected override void ApplyParameterInfo(DbParameter parameter, DataRow row, StatementType statementType, bool whereClause)
    {
        // SharpCoreDB parameters don't require special configuration
    }

    /// <summary>
    /// Returns the name of the specified parameter.
    /// </summary>
    protected override string GetParameterName(int parameterOrdinal)
    {
        return $"@p{parameterOrdinal}";
    }

    /// <summary>
    /// Returns the name of the specified parameter in a format suitable for WHERE clauses.
    /// </summary>
    protected override string GetParameterName(string parameterName)
    {
        return parameterName.StartsWith('@') ? parameterName : $"@{parameterName}";
    }

    /// <summary>
    /// Returns the placeholder for a parameter.
    /// </summary>
    protected override string GetParameterPlaceholder(int parameterOrdinal)
    {
        return $"@p{parameterOrdinal}";
    }

    /// <summary>
    /// Sets the number of records to process in each batch for row updates.
    /// </summary>
    protected override void SetRowUpdatingHandler(DbDataAdapter adapter)
    {
        // Handle row updating events if needed
        if (adapter is SharpCoreDBDataAdapter)
        {
            // Future: implement row updating logic
        }
    }
}
