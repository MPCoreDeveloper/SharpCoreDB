using System.Collections;
using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Collection of parameters for a <see cref="SharpCoreDBCommand"/>.
/// Modern C# 14 implementation with collection expressions and pattern matching.
/// </summary>
public sealed class SharpCoreDBParameterCollection : DbParameterCollection
{
    private readonly List<SharpCoreDBParameter> _parameters = [];

    /// <summary>
    /// Gets the number of parameters in the collection.
    /// </summary>
    public override int Count => _parameters.Count;

    /// <summary>
    /// Gets the synchronized root object.
    /// </summary>
    public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

    /// <summary>
    /// Gets a value indicating whether the collection is fixed size.
    /// </summary>
    public override bool IsFixedSize => false;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public override bool IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether the collection is synchronized.
    /// </summary>
    public override bool IsSynchronized => false;

    /// <summary>
    /// Gets or sets the parameter at the specified index.
    /// </summary>
    public new SharpCoreDBParameter this[int index]
    {
        get => _parameters[index];
        set => _parameters[index] = value;
    }

    /// <summary>
    /// Gets or sets the parameter with the specified name.
    /// </summary>
    public new SharpCoreDBParameter this[string parameterName]
    {
        get
        {
            var index = IndexOf(parameterName);
            if (index < 0)
                throw new ArgumentException($"Parameter '{parameterName}' not found.", nameof(parameterName));
            return _parameters[index];
        }
        set
        {
            var index = IndexOf(parameterName);
            if (index < 0)
                throw new ArgumentException($"Parameter '{parameterName}' not found.", nameof(parameterName));
            _parameters[index] = value;
        }
    }

    /// <summary>
    /// Adds a parameter to the collection.
    /// </summary>
    public SharpCoreDBParameter Add(SharpCoreDBParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Adds a parameter with the specified name and value.
    /// </summary>
    public SharpCoreDBParameter Add(string parameterName, object? value)
    {
        var parameter = new SharpCoreDBParameter(parameterName, value);
        return Add(parameter);
    }

    /// <summary>
    /// Adds a parameter with the specified value.
    /// </summary>
    public override int Add(object value)
    {
        if (value is not SharpCoreDBParameter parameter)
            throw new ArgumentException("Value must be a SharpCoreDBParameter.", nameof(value));

        Add(parameter);
        return _parameters.Count - 1;
    }

    /// <summary>
    /// Adds a range of parameters to the collection.
    /// </summary>
    public void AddRange(params SharpCoreDBParameter[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var param in values)
        {
            Add(param);
        }
    }

    /// <summary>
    /// Adds a range of values to the collection.
    /// </summary>
    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        
        foreach (var value in values)
        {
            Add(value);
        }
    }

    /// <summary>
    /// Clears all parameters from the collection.
    /// </summary>
    public override void Clear()
    {
        _parameters.Clear();
    }

    /// <summary>
    /// Determines whether the collection contains the specified parameter name.
    /// </summary>
    public override bool Contains(string value)
    {
        return IndexOf(value) >= 0;
    }

    /// <summary>
    /// Determines whether the collection contains the specified value.
    /// </summary>
    public override bool Contains(object value)
    {
        return value is SharpCoreDBParameter parameter && _parameters.Contains(parameter);
    }

    /// <summary>
    /// Copies the collection to an array.
    /// </summary>
    public override void CopyTo(Array array, int index)
    {
        ((ICollection)_parameters).CopyTo(array, index);
    }

    /// <summary>
    /// Returns an enumerator for the collection.
    /// </summary>
    public override IEnumerator GetEnumerator()
    {
        return _parameters.GetEnumerator();
    }

    /// <summary>
    /// Gets the parameter at the specified index.
    /// </summary>
    protected override DbParameter GetParameter(int index)
    {
        return _parameters[index];
    }

    /// <summary>
    /// Gets the parameter with the specified name.
    /// </summary>
    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
            throw new ArgumentException($"Parameter '{parameterName}' not found.", nameof(parameterName));
        return _parameters[index];
    }

    /// <summary>
    /// Gets the index of the specified parameter name.
    /// </summary>
    public override int IndexOf(string parameterName)
    {
        ArgumentNullException.ThrowIfNull(parameterName);
        
        var normalizedName = parameterName.TrimStart('@');
        
        for (int i = 0; i < _parameters.Count; i++)
        {
            var paramName = _parameters[i].ParameterName?.TrimStart('@');
            if (string.Equals(paramName, normalizedName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        
        return -1;
    }

    /// <summary>
    /// Gets the index of the specified parameter.
    /// </summary>
    public override int IndexOf(object value)
    {
        if (value is not SharpCoreDBParameter parameter)
            return -1;
        
        return _parameters.IndexOf(parameter);
    }

    /// <summary>
    /// Inserts a parameter at the specified index.
    /// </summary>
    public override void Insert(int index, object value)
    {
        if (value is not SharpCoreDBParameter parameter)
            throw new ArgumentException("Value must be a SharpCoreDBParameter.", nameof(value));

        _parameters.Insert(index, parameter);
    }

    /// <summary>
    /// Removes the specified parameter.
    /// </summary>
    public override void Remove(object value)
    {
        if (value is SharpCoreDBParameter parameter)
        {
            _parameters.Remove(parameter);
        }
    }

    /// <summary>
    /// Removes the parameter at the specified index.
    /// </summary>
    public override void RemoveAt(int index)
    {
        _parameters.RemoveAt(index);
    }

    /// <summary>
    /// Removes the parameter with the specified name.
    /// </summary>
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    /// <summary>
    /// Sets the parameter at the specified index.
    /// </summary>
    protected override void SetParameter(int index, DbParameter value)
    {
        if (value is not SharpCoreDBParameter parameter)
            throw new ArgumentException("Value must be a SharpCoreDBParameter.", nameof(value));

        _parameters[index] = parameter;
    }

    /// <summary>
    /// Sets the parameter with the specified name.
    /// </summary>
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
            throw new ArgumentException($"Parameter '{parameterName}' not found.", nameof(parameterName));

        SetParameter(index, value);
    }
}
