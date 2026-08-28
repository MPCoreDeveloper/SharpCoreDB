using Dapper;
using SharpCoreDB.Interfaces;
using System.Reflection;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides query result mapping extensions for Dapper with SharpCoreDB.
/// </summary>
public static class DapperMappingExtensions
{
    /// <summary>
    /// Maps query results to a specific type with custom mapping rules.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="mapper">Custom mapping function.</param>
    /// <param name="param">Query parameters.</param>
    /// <returns>Mapped results.</returns>
    public static IEnumerable<T> QueryWithMapping<T>(
        this IDatabase database,
        string sql,
        Func<Dictionary<string, object>, T> mapper,
        object? param = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(mapper);

        var parameters = param != null
            ? ConvertObjectToDictionary(param)
            : null;

        var results = database.ExecuteQuery(sql, parameters);
        return results.Select(mapper);
    }

    /// <summary>
    /// Maps query results to a specific type with automatic property mapping.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">Query parameters.</param>
    /// <param name="ignoreCase">Whether to ignore case when matching properties.</param>
    /// <returns>Mapped results.</returns>
    public static IEnumerable<T> QueryMapped<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        bool ignoreCase = true) where T : new()
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var parameters = param != null
            ? ConvertObjectToDictionary(param)
            : null;

        var results = database.ExecuteQuery(sql, parameters);
        return results.Select(row => MapToType<T>(row, ignoreCase));
    }

    /// <summary>
    /// Maps a single result or returns default.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">Query parameters.</param>
    /// <param name="ignoreCase">Whether to ignore case when matching properties.</param>
    /// <returns>Mapped result or default.</returns>
    public static T? QuerySingleMapped<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        bool ignoreCase = true) where T : new()
    {
        var results = database.QueryMapped<T>(sql, param, ignoreCase);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Maps query results with multi-table joins.
    /// </summary>
    /// <typeparam name="T1">First entity type.</typeparam>
    /// <typeparam name="T2">Second entity type.</typeparam>
    /// <typeparam name="TResult">Result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="mapper">Function to map the two entities to the result.</param>
    /// <param name="splitOn">Column name to split on (default: "Id").</param>
    /// <param name="param">Query parameters.</param>
    /// <returns>Mapped results.</returns>
    public static IEnumerable<TResult> QueryMultiMapped<T1, T2, TResult>(
        this IDatabase database,
        string sql,
        Func<T1, T2, TResult> mapper,
        string splitOn = "Id",
        object? param = null)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return connection.Query(sql, mapper, parameters, splitOn: splitOn);
    }

    /// <summary>
    /// Maps query results with three-table joins.
    /// </summary>
    public static IEnumerable<TResult> QueryMultiMapped<T1, T2, T3, TResult>(
        this IDatabase database,
        string sql,
        Func<T1, T2, T3, TResult> mapper,
        string splitOn = "Id",
        object? param = null)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return connection.Query(sql, mapper, parameters, splitOn: splitOn);
    }

    /// <summary>
    /// Creates a custom type map for Dapper.
    /// </summary>
    /// <typeparam name="T">The type to map.</typeparam>
    /// <param name="columnMappings">Dictionary of property name to column name mappings.</param>
    public static void CreateTypeMap<T>(Dictionary<string, string> columnMappings)
    {
        ArgumentNullException.ThrowIfNull(columnMappings);

        var map = new CustomPropertyTypeMap(
            typeof(T),
            (type, columnName) =>
            {
                if (columnMappings.TryGetValue(columnName, out var propertyName))
                {
                    return type.GetProperty(propertyName);
                }
                return type.GetProperty(columnName);
            });

        SqlMapper.SetTypeMap(typeof(T), map);
    }

    /// <summary>
    /// Maps a dictionary to a strongly-typed object.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="row">The dictionary row.</param>
    /// <param name="ignoreCase">Whether to ignore case when matching properties.</param>
    /// <returns>Mapped object.</returns>
    public static T MapToType<T>(Dictionary<string, object> row, bool ignoreCase = true) where T : new()
    {
        var obj = new T();
        var type = typeof(T);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var kvp in row)
        {
            var property = type.GetProperties()
                .FirstOrDefault(p => p.Name.Equals(kvp.Key, comparison));

            if (property != null && property.CanWrite)
            {
                try
                {
                    var value = ConvertValue(kvp.Value, property.PropertyType);
                    property.SetValue(obj, value);
                }
                catch (InvalidCastException)
                {
                    // Skip properties that can't be converted — type mismatch between DB and CLR type
                }
            }
        }

        return obj;
    }

    /// <summary>
    /// Maps an object to a dictionary.
    /// </summary>
    /// <param name="obj">The object to map.</param>
    /// <returns>Dictionary representation.</returns>
    public static Dictionary<string, object?> MapToDictionary(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var result = new Dictionary<string, object?>();
        var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.CanRead)
            {
                result[property.Name] = property.GetValue(obj);
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a dynamic object from query results.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">Query parameters.</param>
    /// <returns>Dynamic results.</returns>
    public static IEnumerable<dynamic> QueryDynamic(
        this IDatabase database,
        string sql,
        object? param = null)
    {
        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return connection.Query(sql, parameters);
    }

    /// <summary>
    /// Projects query results into a different shape.
    /// </summary>
    /// <typeparam name="TSource">Source type.</typeparam>
    /// <typeparam name="TResult">Result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="projection">Projection function.</param>
    /// <param name="param">Query parameters.</param>
    /// <returns>Projected results.</returns>
    public static IEnumerable<TResult> QueryProjected<TSource, TResult>(
        this IDatabase database,
        string sql,
        Func<TSource, TResult> projection,
        object? param = null) where TSource : new()
    {
        var results = database.QueryMapped<TSource>(sql, param);
        return results.Select(projection);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null || value == DBNull.Value)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsAssignableFrom(value.GetType()))
            return value;

        if (underlyingType.IsEnum)
        {
            return value switch
            {
                string s => Enum.Parse(underlyingType, s),
                int i => Enum.ToObject(underlyingType, i),
                _ => Enum.ToObject(underlyingType, Convert.ToInt32(value))
            };
        }

        return DapperTypeMapper.ConvertValue(value, targetType);
    }

    private static Dictionary<string, object?>? ConvertObjectToDictionary(object? obj)
    {
        if (obj == null)
            return null;

        if (obj is Dictionary<string, object?> dict)
            return dict;

        var result = new Dictionary<string, object?>();
        var properties = obj.GetType().GetProperties();

        foreach (var prop in properties)
        {
            result[prop.Name] = prop.GetValue(obj);
        }

        return result;
    }

    private static DynamicParameters ConvertToDynamicParameters(object param)
    {
        if (param is DynamicParameters dp)
            return dp;

        var dynamicParams = new DynamicParameters();
        var properties = param.GetType().GetProperties();
        
        foreach (var prop in properties)
        {
            var value = prop.GetValue(param);
            dynamicParams.Add(prop.Name, value);
        }

        return dynamicParams;
    }
}
