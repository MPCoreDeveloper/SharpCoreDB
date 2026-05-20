using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using System.Linq;
using System.Text;

namespace SharpCoreDB.EntityFrameworkCore.Update;

/// <summary>
/// SQL generator for update commands in SharpCoreDB.
/// </summary>
public class SharpCoreDBUpdateSqlGenerator : UpdateSqlGenerator
{
    /// <summary>
    /// Initializes a new instance of the SharpCoreDBUpdateSqlGenerator class.
    /// </summary>
    public SharpCoreDBUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        requiresTransaction = false;
        var writeOperations = command.ColumnModifications.Where(c => c.IsWrite).ToList();
        var readOperations = command.ColumnModifications.Where(c => c.IsRead).ToList();

        // ✅ FIX: SharpCoreDB does not support RETURNING clauses.
        // Pass an empty read-operations list so the base class emits a plain INSERT
        // without a RETURNING clause, then append SELECT last_insert_rowid() to
        // retrieve the generated primary key via the data reader path.
        AppendInsertCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            writeOperations,
            readOperations: []);

        // If no explicit read operations were provided by EF Core, look for
        // any integer/long column that belongs to the primary key. This is the
        // most common pattern used by custom providers (including how SQLite works)
        // to guarantee last_insert_rowid() is emitted.
        if (readOperations.Count > 0)
        {
            commandStringBuilder.AppendLine(";");
            // Retrieve the generated key. SharpCoreDB supports last_insert_rowid()
            // (SQLite-compatible) and returns it as a scalar result that the
            // EF Core reader pipeline maps back to the entity key property.
            var keyColumn = readOperations[0].ColumnName;
            commandStringBuilder.Append("SELECT last_insert_rowid() AS ");
            commandStringBuilder.Append(SqlGenerationHelper.DelimitIdentifier(keyColumn));
            commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
            return ResultSetMapping.LastInResultSet;
        }

        return ResultSetMapping.NoResults;
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendUpdateOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        // ✅ FIX: EF Core AppendUpdateCommand signature is (builder, name, schema, writeOps, readOps [→RETURNING], conditionOps [→WHERE], appendReturningOne).
        // Pass [] for readOps so no RETURNING clause is emitted. conditionOps drives the WHERE clause.
        requiresTransaction = false;
        var writeOperations = command.ColumnModifications.Where(c => c.IsWrite).ToList();
        var conditionOperations = command.ColumnModifications.Where(c => c.IsCondition || c.IsKey).ToList();
        AppendUpdateCommand(commandStringBuilder, command.TableName, command.Schema, writeOperations, [], conditionOperations, false);
        return ResultSetMapping.NoResults;
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendDeleteOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        // ✅ FIX: AppendDeleteCommand signature: (builder, name, schema, readOps [→RETURNING], conditionOps [→WHERE], appendReturningOne).
        // Pass [] for readOps so no RETURNING is emitted. keyOperations drive the WHERE clause.
        requiresTransaction = false;
        var conditionOperations = command.ColumnModifications.Where(c => c.IsCondition || c.IsKey).ToList();
        AppendDeleteCommand(commandStringBuilder, command.TableName, command.Schema, [], conditionOperations, false);
        return ResultSetMapping.NoResults;
    }
}
