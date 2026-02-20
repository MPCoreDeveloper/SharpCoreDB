namespace SharpCoreDB.Analytics;

/// <summary>
/// Base interface for window functions.
/// Used for ranking, row numbering, and lag/lead operations.
/// </summary>
public interface IWindowFunction
{
    /// <summary>Gets the name of the window function.</summary>
    string FunctionName { get; }
    
    /// <summary>Processes the next value in the window.</summary>
    void ProcessValue(object? value);
    
    /// <summary>Gets the result for the current row.</summary>
    object? GetResult();
}

/// <summary>
/// Specification for a window frame (ROWS BETWEEN X AND Y).
/// </summary>
public class WindowFrameSpec
{
    /// <summary>
    /// Gets or sets the frame start type (UNBOUNDED PRECEDING, CURRENT ROW, etc.).
    /// </summary>
    public WindowFrameStart FrameStart { get; set; } = WindowFrameStart.UnboundedPreceding;
    
    /// <summary>
    /// Gets or sets the frame end type.
    /// </summary>
    public WindowFrameEnd FrameEnd { get; set; } = WindowFrameEnd.CurrentRow;
    
    /// <summary>
    /// Gets or sets the number of rows for relative frame specifications.
    /// </summary>
    public int? RowOffset { get; set; }
}

/// <summary>
/// Window frame start specification.
/// </summary>
public enum WindowFrameStart
{
    /// <summary>Start from the first row of the partition.</summary>
    UnboundedPreceding,
    
    /// <summary>Start N rows before the current row.</summary>
    PrecedingRows,
    
    /// <summary>Start from the current row.</summary>
    CurrentRow
}

/// <summary>
/// Window frame end specification.
/// </summary>
public enum WindowFrameEnd
{
    /// <summary>End at the current row.</summary>
    CurrentRow,
    
    /// <summary>End N rows after the current row.</summary>
    FollowingRows,
    
    /// <summary>End at the last row of the partition.</summary>
    UnboundedFollowing
}

/// <summary>
/// Represents a partition in a window function specification.
/// </summary>
public class WindowPartition
{
    /// <summary>Gets the partition key value.</summary>
    public object? PartitionKey { get; set; }
    
    /// <summary>Gets the list of values in this partition.</summary>
    public List<object?> Values { get; } = [];
    
    /// <summary>Gets the current row index within the partition.</summary>
    public int CurrentRowIndex { get; set; }
}
