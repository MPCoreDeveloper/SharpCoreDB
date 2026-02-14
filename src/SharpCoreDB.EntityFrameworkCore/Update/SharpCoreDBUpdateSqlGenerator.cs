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

        AppendInsertCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            writeOperations,
            readOperations);

        return ResultSetMapping.NoResults;
    }
}
