using System.Data;
using System.Globalization;
using System.Text;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using FluentMigrator.Runner.Processors;

namespace SharpCoreDB.Extensions.Processor;

/// <summary>
/// FluentMigrator processor for SharpCoreDB.
/// </summary>
public sealed class SharpCoreDbProcessor(
    string connectionString,
    ProcessorOptions options,
    SharpCoreDbMigrationExecutor executor) : IMigrationProcessor
{
    private readonly SharpCoreDbMigrationExecutor _executor = executor;

    /// <summary>
    /// Gets strongly typed processor options.
    /// </summary>
    public ProcessorOptions ProcessorOptions { get; } = options;

#pragma warning disable CS0612
    /// <inheritdoc />
    public IMigrationProcessorOptions Options => ProcessorOptions;
#pragma warning restore CS0612

    /// <inheritdoc />
    public string ConnectionString { get; } = connectionString;

    /// <inheritdoc />
    public string DatabaseType => "sharpcoredb";

    /// <inheritdoc />
    public IList<string> DatabaseTypeAliases { get; } = ["SharpCoreDB", "sharpcoredb"];

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public void Execute(string sql) => _executor.ExecuteSql(sql);

    /// <inheritdoc />
    public void Execute(string template, params object[] args) => Execute(string.Format(CultureInfo.InvariantCulture, template, args));

    /// <inheritdoc />
    public DataSet ReadTableData(string schemaName, string tableName) => _executor.Read($"SELECT * FROM {QuoteIdentifier(tableName)}");

    /// <inheritdoc />
    public DataSet Read(string template, params object[] args)
    {
        var sql = string.Format(CultureInfo.InvariantCulture, template, args);
        return _executor.Read(sql);
    }

    /// <inheritdoc />
    public bool Exists(string template, params object[] args)
    {
        var sql = string.Format(CultureInfo.InvariantCulture, template, args);
        return _executor.ExecuteScalar(sql) is not null;
    }

    /// <inheritdoc />
    public bool SchemaExists(string schemaName) => string.IsNullOrWhiteSpace(schemaName);

    /// <inheritdoc />
    public bool TableExists(string schemaName, string tableName)
    {
        try
        {
            _ = _executor.ExecuteScalar($"SELECT 1 FROM {QuoteIdentifier(tableName)} LIMIT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool ColumnExists(string schemaName, string tableName, string columnName)
    {
        try
        {
            var result = _executor.Read($"PRAGMA table_info({QuoteIdentifier(tableName)})");
            if (result.Tables.Count == 0)
            {
                return false;
            }

            return result.Tables[0].Rows
                .Cast<DataRow>()
                .Any(r => string.Equals(r["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool ConstraintExists(string schemaName, string tableName, string constraintName) => false;

    /// <inheritdoc />
    public bool IndexExists(string schemaName, string tableName, string indexName)
    {
        try
        {
            var scalar = _executor.ExecuteScalar($"SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = '{EscapeSql(indexName)}' LIMIT 1");
            return scalar is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool SequenceExists(string schemaName, string sequenceName) => false;

    /// <inheritdoc />
    public bool DefaultValueExists(string schemaName, string tableName, string columnName, object defaultValue)
    {
        try
        {
            var result = _executor.Read($"PRAGMA table_info({QuoteIdentifier(tableName)})");
            if (result.Tables.Count == 0)
            {
                return false;
            }

            var expected = FormatValue(defaultValue).Trim('"', '\'');
            var row = result.Tables[0].Rows.Cast<DataRow>()
                .FirstOrDefault(r => string.Equals(r["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase));

            var actual = row?["dflt_value"]?.ToString()?.Trim('"', '\'');
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void BeginTransaction()
    {
    }

    /// <inheritdoc />
    public void CommitTransaction()
    {
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
    }

    /// <inheritdoc />
    public void Process(CreateSchemaExpression expression)
    {
        if (!string.IsNullOrWhiteSpace(expression.SchemaName))
        {
            Execute($"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(expression.SchemaName)}");
        }
    }

    /// <inheritdoc />
    public void Process(DeleteSchemaExpression expression)
    {
        if (!string.IsNullOrWhiteSpace(expression.SchemaName))
        {
            Execute($"DROP SCHEMA IF EXISTS {QuoteIdentifier(expression.SchemaName)}");
        }
    }

    /// <inheritdoc />
    public void Process(AlterTableExpression expression)
    {
        if (!string.IsNullOrWhiteSpace(expression.TableDescription))
        {
            Execute($"-- ALTER TABLE {QuoteIdentifier(expression.TableName)}: {EscapeComment(expression.TableDescription)}");
        }
    }

    /// <inheritdoc />
    public void Process(AlterColumnExpression expression)
    {
        var columnSql = BuildColumnDefinition(expression.Column);
        Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} ALTER COLUMN {columnSql}");
    }

    /// <inheritdoc />
    public void Process(CreateTableExpression expression)
    {
        var columns = expression.Columns.Select(BuildColumnDefinition).ToList();
        var primaryKeyColumns = expression.Columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();

        if (primaryKeyColumns.Count > 0)
        {
            columns.Add($"PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})");
        }

        Execute($"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(expression.TableName)} ({string.Join(", ", columns)})");

        foreach (var indexedColumn in expression.Columns.Where(c => c.IsIndexed))
        {
            var indexName = $"IX_{expression.TableName}_{indexedColumn.Name}";
            Execute($"CREATE INDEX IF NOT EXISTS {QuoteIdentifier(indexName)} ON {QuoteIdentifier(expression.TableName)} ({QuoteIdentifier(indexedColumn.Name)})");
        }
    }

    /// <inheritdoc />
    public void Process(CreateColumnExpression expression)
    {
        Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} ADD COLUMN {BuildColumnDefinition(expression.Column)}");
    }

    /// <inheritdoc />
    public void Process(DeleteTableExpression expression)
    {
        var ifExists = expression.IfExists ? "IF EXISTS " : string.Empty;
        Execute($"DROP TABLE {ifExists}{QuoteIdentifier(expression.TableName)}");
    }

    /// <inheritdoc />
    public void Process(DeleteColumnExpression expression)
    {
        foreach (var columnName in expression.ColumnNames)
        {
            Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} DROP COLUMN {QuoteIdentifier(columnName)}");
        }
    }

    /// <inheritdoc />
    public void Process(CreateForeignKeyExpression expression)
    {
        var foreignKey = expression.ForeignKey;
        var fkColumns = string.Join(", ", foreignKey.ForeignColumns.Select(QuoteIdentifier));
        var pkColumns = string.Join(", ", foreignKey.PrimaryColumns.Select(QuoteIdentifier));

        Execute($"ALTER TABLE {QuoteIdentifier(foreignKey.ForeignTable)} ADD CONSTRAINT {QuoteIdentifier(foreignKey.Name)} FOREIGN KEY ({fkColumns}) REFERENCES {QuoteIdentifier(foreignKey.PrimaryTable)} ({pkColumns})");
    }

    /// <inheritdoc />
    public void Process(DeleteForeignKeyExpression expression)
    {
        var foreignKey = expression.ForeignKey;
        Execute($"ALTER TABLE {QuoteIdentifier(foreignKey.ForeignTable)} DROP CONSTRAINT {QuoteIdentifier(foreignKey.Name)}");
    }

    /// <inheritdoc />
    public void Process(CreateIndexExpression expression)
    {
        var index = expression.Index;
        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var columns = string.Join(", ", index.Columns.Select(c => QuoteIdentifier(c.Name)));
        Execute($"CREATE {unique}INDEX IF NOT EXISTS {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(index.TableName)} ({columns})");
    }

    /// <inheritdoc />
    public void Process(DeleteIndexExpression expression)
    {
        Execute($"DROP INDEX IF EXISTS {QuoteIdentifier(expression.Index.Name)}");
    }

    /// <inheritdoc />
    public void Process(RenameTableExpression expression)
    {
        Execute($"ALTER TABLE {QuoteIdentifier(expression.OldName)} RENAME TO {QuoteIdentifier(expression.NewName)}");
    }

    /// <inheritdoc />
    public void Process(RenameColumnExpression expression)
    {
        Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} RENAME COLUMN {QuoteIdentifier(expression.OldName)} TO {QuoteIdentifier(expression.NewName)}");
    }

    /// <inheritdoc />
    public void Process(InsertDataExpression expression)
    {
        foreach (var row in expression.Rows)
        {
            var values = ToDictionary(row);
            var columns = string.Join(", ", values.Keys.Select(QuoteIdentifier));
            var literals = string.Join(", ", values.Values.Select(FormatValue));
            Execute($"INSERT INTO {QuoteIdentifier(expression.TableName)} ({columns}) VALUES ({literals})");
        }
    }

    /// <inheritdoc />
    public void Process(AlterDefaultConstraintExpression expression)
    {
        Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} ALTER COLUMN {QuoteIdentifier(expression.ColumnName)} SET DEFAULT {FormatValue(expression.DefaultValue)}");
    }

    /// <inheritdoc />
    public void Process(PerformDBOperationExpression expression)
    {
        using var connection = _executor.GetOperationConnection();
        expression.Operation?.Invoke(connection, null);
    }

    /// <inheritdoc />
    public void Process(DeleteDataExpression expression)
    {
        if (expression.IsAllRows || expression.Rows.Count == 0)
        {
            Execute($"DELETE FROM {QuoteIdentifier(expression.TableName)}");
            return;
        }

        foreach (var row in expression.Rows)
        {
            var values = ToDictionary(row);
            var predicate = BuildWhereClause(values);
            Execute($"DELETE FROM {QuoteIdentifier(expression.TableName)} WHERE {predicate}");
        }
    }

    /// <inheritdoc />
    public void Process(UpdateDataExpression expression)
    {
        var setValues = expression.Set.Count > 0 ? ToDictionary(expression.Set[0]) : new Dictionary<string, object?>();
        var setClause = string.Join(", ", setValues.Select(v => $"{QuoteIdentifier(v.Key)} = {FormatValue(v.Value)}"));

        if (expression.IsAllRows || expression.Where.Count == 0)
        {
            Execute($"UPDATE {QuoteIdentifier(expression.TableName)} SET {setClause}");
            return;
        }

        foreach (var where in expression.Where)
        {
            var whereClause = BuildWhereClause(ToDictionary(where));
            Execute($"UPDATE {QuoteIdentifier(expression.TableName)} SET {setClause} WHERE {whereClause}");
        }
    }

    /// <inheritdoc />
    public void Process(AlterSchemaExpression expression)
    {
        if (!string.IsNullOrWhiteSpace(expression.DestinationSchemaName))
        {
            Execute($"-- ALTER SCHEMA {EscapeComment(expression.SourceSchemaName)} TO {EscapeComment(expression.DestinationSchemaName)} FOR TABLE {EscapeComment(expression.TableName)}");
        }
    }

    /// <inheritdoc />
    public void Process(CreateSequenceExpression expression)
    {
        var sequence = expression.Sequence;
        var start = sequence.StartWith ?? 1;
        var increment = sequence.Increment ?? 1;
        Execute($"CREATE SEQUENCE {QuoteIdentifier(sequence.Name)} START WITH {start} INCREMENT BY {increment}");
    }

    /// <inheritdoc />
    public void Process(DeleteSequenceExpression expression)
    {
        Execute($"DROP SEQUENCE IF EXISTS {QuoteIdentifier(expression.SequenceName)}");
    }

    /// <inheritdoc />
    public void Process(CreateConstraintExpression expression)
    {
        var constraint = expression.Constraint;
        var columns = string.Join(", ", constraint.Columns.Select(QuoteIdentifier));

        if (constraint.IsPrimaryKeyConstraint)
        {
            Execute($"ALTER TABLE {QuoteIdentifier(constraint.TableName)} ADD CONSTRAINT {QuoteIdentifier(constraint.ConstraintName)} PRIMARY KEY ({columns})");
            return;
        }

        if (constraint.IsUniqueConstraint)
        {
            Execute($"ALTER TABLE {QuoteIdentifier(constraint.TableName)} ADD CONSTRAINT {QuoteIdentifier(constraint.ConstraintName)} UNIQUE ({columns})");
            return;
        }

        throw new NotSupportedException($"Constraint type is not supported for '{constraint.ConstraintName}'.");
    }

    /// <inheritdoc />
    public void Process(DeleteConstraintExpression expression)
    {
        var constraint = expression.Constraint;
        Execute($"ALTER TABLE {QuoteIdentifier(constraint.TableName)} DROP CONSTRAINT {QuoteIdentifier(constraint.ConstraintName)}");
    }

    /// <inheritdoc />
    public void Process(DeleteDefaultConstraintExpression expression)
    {
        Execute($"ALTER TABLE {QuoteIdentifier(expression.TableName)} ALTER COLUMN {QuoteIdentifier(expression.ColumnName)} DROP DEFAULT");
    }

    private static Dictionary<string, object?> ToDictionary(object row)
    {
        if (row is IDictionary<string, object?> dict)
        {
            return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary<string, object> objectDict)
        {
            return objectDict.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
        }

        var valuesProperty = row.GetType().GetProperty("Values");
        if (valuesProperty?.GetValue(row) is IDictionary<string, object?> rowValues)
        {
            return new Dictionary<string, object?>(rowValues, StringComparer.OrdinalIgnoreCase);
        }

        throw new NotSupportedException($"Unsupported data row type '{row.GetType().FullName}'.");
    }

    private static string BuildWhereClause(Dictionary<string, object?> values)
    {
        if (values.Count == 0)
        {
            return "1 = 1";
        }

        return string.Join(" AND ", values.Select(v => $"{QuoteIdentifier(v.Key)} = {FormatValue(v.Value)}"));
    }

    private static string BuildColumnDefinition(ColumnDefinition column)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteIdentifier(column.Name));
        builder.Append(' ');

        if (!string.IsNullOrWhiteSpace(column.CustomType))
        {
            builder.Append(column.CustomType);
        }
        else
        {
            builder.Append(MapDbType(column));
        }

        if (column.IsIdentity)
        {
            builder.Append(" PRIMARY KEY AUTOINCREMENT");
        }
        else if (column.IsPrimaryKey)
        {
            builder.Append(" PRIMARY KEY");
        }

        if (column.IsUnique)
        {
            builder.Append(" UNIQUE");
        }

        if (column.IsNullable is false)
        {
            builder.Append(" NOT NULL");
        }

        if (column.DefaultValue is not null)
        {
            builder.Append(" DEFAULT ");
            builder.Append(FormatValue(column.DefaultValue));
        }

        return builder.ToString();
    }

    private static string MapDbType(ColumnDefinition column)
    {
        return column.Type switch
        {
            DbType.Int16 or DbType.Int32 or DbType.Int64 or DbType.UInt16 or DbType.UInt32 or DbType.UInt64 => "INTEGER",
            DbType.Boolean => "BOOLEAN",
            DbType.Date or DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset => "DATETIME",
            DbType.Decimal or DbType.Currency or DbType.VarNumeric => "DECIMAL",
            DbType.Double => "DOUBLE",
            DbType.Single => "REAL",
            DbType.Binary => "BLOB",
            DbType.Guid => "TEXT",
            _ => column.Size.HasValue && column.Size.Value > 0 ? $"VARCHAR({column.Size.Value})" : "TEXT"
        };
    }

    private static string FormatValue(object? value)
    {
        if (value is null or DBNull)
        {
            return "NULL";
        }

        return value switch
        {
            bool boolValue => boolValue ? "1" : "0",
            string stringValue => $"'{EscapeSql(stringValue)}'",
            DateTime dateTime => $"'{dateTime:O}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:O}'",
            Guid guid => $"'{guid:D}'",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => $"'{EscapeSql(value.ToString() ?? string.Empty)}'"
        };
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string EscapeComment(string value) => value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
