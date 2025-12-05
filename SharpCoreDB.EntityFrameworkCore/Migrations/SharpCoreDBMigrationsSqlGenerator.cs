using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace SharpCoreDB.EntityFrameworkCore.Migrations;

/// <summary>
/// SQL generator for SharpCoreDB migrations.
/// Generates CREATE TABLE, INDEX, UPSERT operations.
/// </summary>
public class SharpCoreDBMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>
    /// Initializes a new instance of the SharpCoreDBMigrationsSqlGenerator class.
    /// </summary>
    public SharpCoreDBMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        ICommandBatchPreparer commandBatchPreparer)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        if (operation is CreateTableOperation createTableOp)
        {
            GenerateCreateTable(createTableOp, model, builder);
        }
        else if (operation is CreateIndexOperation createIndexOp)
        {
            GenerateCreateIndex(createIndexOp, model, builder);
        }
        else if (operation is InsertDataOperation insertDataOp)
        {
            GenerateInsertOrReplace(insertDataOp, model, builder);
        }
        else
        {
            base.Generate(operation, model, builder);
        }
    }

    /// <summary>
    /// Generates CREATE TABLE SQL.
    /// </summary>
    protected virtual void GenerateCreateTable(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("CREATE TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" (");

        using (builder.Indent())
        {
            for (var i = 0; i < operation.Columns.Count; i++)
            {
                var column = operation.Columns[i];
                if (i > 0)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
                ColumnDefinition(column, model, builder);
            }

            if (operation.PrimaryKey != null)
            {
                builder.Append(",").AppendLine();
                PrimaryKeyConstraint(operation.PrimaryKey, model, builder);
            }

            builder.AppendLine();
        }

        builder.Append(")");
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();
    }

    /// <summary>
    /// Generates CREATE INDEX SQL.
    /// </summary>
    protected virtual void GenerateCreateIndex(CreateIndexOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder.Append("CREATE ");

        if (operation.IsUnique)
        {
            builder.Append("UNIQUE ");
        }

        builder
            .Append("INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ON ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" (");

        for (var i = 0; i < operation.Columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[i]));
        }

        builder.Append(")");
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();
    }

    /// <summary>
    /// Generates INSERT OR REPLACE (UPSERT) SQL for SharpCoreDB.
    /// </summary>
    protected virtual void GenerateInsertOrReplace(InsertDataOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("INSERT OR REPLACE INTO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" (");

        for (var i = 0; i < operation.Columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[i]));
        }

        builder.Append(") VALUES ");

        for (var i = 0; i < operation.Values.GetLength(0); i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("(");

            for (var j = 0; j < operation.Values.GetLength(1); j++)
            {
                if (j > 0)
                {
                    builder.Append(", ");
                }

                var value = operation.Values[i, j];
                builder.Append(value == null
                    ? "NULL"
                    : Dependencies.TypeMappingSource.GetMapping(value.GetType()).GenerateSqlLiteral(value));
            }

            builder.Append(")");
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();
    }

    /// <inheritdoc />
    protected override void ColumnDefinition(AddColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ")
            .Append(operation.ColumnType ?? GetColumnType(operation.Schema, operation.Table, operation.Name, operation, model));

        if (!operation.IsNullable)
        {
            builder.Append(" NOT NULL");
        }

        if (operation.DefaultValue != null)
        {
            builder
                .Append(" DEFAULT ")
                .Append(Dependencies.TypeMappingSource.GetMapping(operation.DefaultValue.GetType()).GenerateSqlLiteral(operation.DefaultValue));
        }
        else if (!string.IsNullOrWhiteSpace(operation.DefaultValueSql))
        {
            builder
                .Append(" DEFAULT ")
                .Append(operation.DefaultValueSql);
        }
    }

    /// <summary>
    /// Gets the column type for SharpCoreDB.
    /// </summary>
    protected virtual string GetColumnType(string? schema, string table, string name, AddColumnOperation operation, IModel? model)
    {
        return operation.ClrType switch
        {
            Type t when t == typeof(int) => "INTEGER",
            Type t when t == typeof(long) => "LONG",
            Type t when t == typeof(string) => "TEXT",
            Type t when t == typeof(bool) => "BOOLEAN",
            Type t when t == typeof(double) => "REAL",
            Type t when t == typeof(decimal) => "DECIMAL",
            Type t when t == typeof(DateTime) => "DATETIME",
            Type t when t == typeof(Guid) => "GUID",
            Type t when t == typeof(byte[]) => "BLOB",
            _ => "TEXT"
        };
    }

    /// <summary>
    /// Generates primary key constraint SQL.
    /// </summary>
    protected virtual void PrimaryKeyConstraint(AddPrimaryKeyOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        builder
            .Append("PRIMARY KEY (");

        for (var i = 0; i < operation.Columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Columns[i]));
        }

        builder.Append(")");
    }
}
