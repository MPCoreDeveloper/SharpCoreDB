namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Exception thrown by the SharpCoreDB ADO.NET Data Provider.
/// </summary>
public class SharpCoreDBException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBException"/> class.
    /// </summary>
    public SharpCoreDBException()
        : base("An error occurred in the SharpCoreDB Data Provider.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBException"/> class with a specified error message.
    /// </summary>
    public SharpCoreDBException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBException"/> class with a specified error message and inner exception.
    /// </summary>
    public SharpCoreDBException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
