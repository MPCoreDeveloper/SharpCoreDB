using Microsoft.EntityFrameworkCore.Update;

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
}
