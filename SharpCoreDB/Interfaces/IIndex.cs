namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for indexing data, using B-tree for fast lookups.
/// </summary>
public interface IIndex<TKey, TValue> where TKey : IComparable<TKey>
{
    /// <summary>
    /// Inserts a key-value pair into the index.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    void Insert(TKey key, TValue value);

    /// <summary>
    /// Searches for a value by key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True if found, and the value.</returns>
    (bool Found, TValue? Value) Search(TKey key);
}
