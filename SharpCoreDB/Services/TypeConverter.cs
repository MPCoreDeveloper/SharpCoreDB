using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCoreDB.Services;

/// <summary>
/// Provides safe type conversion utilities for database operations.
/// </summary>
public static class TypeConverter
{
    /// <summary>
    /// Safely converts a value to the specified type.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="InvalidCastException">Thrown if conversion is not possible.</exception>
    public static T Convert<T>(object? value)
    {
        if (value is T t)
            return t;

        if (value == null || value == DBNull.Value)
            return default!;

        try
        {
            return (T)System.Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            throw new InvalidCastException($"Cannot convert {value.GetType()} to {typeof(T)}");
        }
    }

    /// <summary>
    /// Tries to convert a value to the specified type.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">The converted value if successful.</param>
    /// <returns>True if conversion succeeded, otherwise false.</returns>
    public static bool TryConvert<T>(object? value, out T result)
    {
        result = default!;

        if (value is T t)
        {
            result = t;
            return true;
        }

        if (value == null || value == DBNull.Value)
        {
            result = default!;
            return true;
        }

        try
        {
            result = (T)System.Convert.ChangeType(value, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates a DEFAULT expression and returns the resulting value.
    /// </summary>
    /// <param name="expression">The expression to evaluate (e.g., "CURRENT_TIMESTAMP", "NEWID()").</param>
    /// <param name="targetType">The target data type for the result.</param>
    /// <returns>The evaluated value.</returns>
    public static object? EvaluateDefaultExpression(string? expression, DataType targetType)
    {
        if (string.IsNullOrEmpty(expression))
            return null;

        return expression.ToUpperInvariant() switch
        {
            "CURRENT_TIMESTAMP" or "GETDATE" or "GETUTCDATE" => DateTime.UtcNow,
            "NEWID" or "NEWSEQUENTIALID" => Guid.NewGuid(),
            _ => throw new InvalidOperationException($"Unsupported DEFAULT expression: {expression}")
        };
    }

    /// <summary>
    /// Evaluates a CHECK constraint expression against a row of data.
    /// </summary>
    /// <param name="expression">The CHECK expression to evaluate.</param>
    /// <param name="row">The row data to evaluate against.</param>
    /// <param name="columnTypes">The column types for type checking.</param>
    /// <returns>True if the constraint passes, false otherwise.</returns>
    public static bool EvaluateCheckConstraint(string? expression, Dictionary<string, object> row, List<DataType> columnTypes)
    {
        if (string.IsNullOrEmpty(expression))
            return true;

        // For now, implement basic CHECK constraint evaluation
        // TODO: Implement full expression parsing and evaluation
        try
        {
            // Simple comparison patterns: column > value, column < value, etc.
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                var columnName = parts[0];
                var op = parts[1];
                var valueStr = parts[2];

                if (row.TryGetValue(columnName, out var columnValue))
                {
                    var expectedValue = ParseValue(valueStr, columnTypes[row.Keys.ToList().IndexOf(columnName)]);
                    return EvaluateOperator(columnValue, op, expectedValue);
                }
            }

            // For complex expressions, return true for now
            // TODO: Implement full expression evaluation
            return true;
        }
        catch
        {
            // If evaluation fails, consider the constraint violated
            return false;
        }
    }

    /// <summary>
    /// Parses a string value to the specified data type.
    /// </summary>
    private static object? ParseValue(string value, DataType type)
    {
        if (value == "NULL") return null;

        return type switch
        {
            DataType.Integer => int.Parse(value),
            DataType.Long => long.Parse(value),
            DataType.Real => double.Parse(value),
            DataType.Decimal => decimal.Parse(value),
            DataType.String => value.Trim('\'', '"'),
            DataType.Boolean => bool.Parse(value),
            DataType.DateTime => DateTime.Parse(value),
            _ => value
        };
    }

    /// <summary>
    /// Evaluates a comparison operator.
    /// </summary>
    private static bool EvaluateOperator(object? left, string op, object? right)
    {
        if (left == null || right == null)
            return false;

        int comparison = Comparer<object>.Default.Compare(left, right);

        return op switch
        {
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            "=" => comparison == 0,
            "!=" or "<>" => comparison != 0,
            _ => false
        };
    }
}
