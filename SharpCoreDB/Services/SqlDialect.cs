// <copyright file="SqlDialect.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// Defines SQL dialect-specific behavior.
/// </summary>
public interface ISqlDialect
{
    /// <summary>
    /// Gets the dialect name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the dialect supports RIGHT JOIN.
    /// </summary>
    bool SupportsRightJoin { get; }

    /// <summary>
    /// Gets whether the dialect supports FULL OUTER JOIN.
    /// </summary>
    bool SupportsFullOuterJoin { get; }

    /// <summary>
    /// Gets whether the dialect supports subqueries in FROM clause.
    /// </summary>
    bool SupportsSubqueriesInFrom { get; }

    /// <summary>
    /// Gets whether the dialect supports subqueries in WHERE clause.
    /// </summary>
    bool SupportsSubqueriesInWhere { get; }

    /// <summary>
    /// Gets whether the dialect supports LIMIT/OFFSET.
    /// </summary>
    bool SupportsLimitOffset { get; }

    /// <summary>
    /// Gets whether the dialect supports WITH (CTE - Common Table Expressions).
    /// </summary>
    bool SupportsCTE { get; }

    /// <summary>
    /// Gets whether the dialect supports RETURNING clause.
    /// </summary>
    bool SupportsReturning { get; }

    /// <summary>
    /// Gets whether the dialect supports window functions.
    /// </summary>
    bool SupportsWindowFunctions { get; }

    /// <summary>
    /// Translates a standard SQL function to dialect-specific version.
    /// </summary>
    /// <param name="functionName">The standard function name.</param>
    /// <returns>The dialect-specific function name.</returns>
    string TranslateFunction(string functionName);

    /// <summary>
    /// Formats a LIMIT clause according to dialect syntax.
    /// </summary>
    /// <param name="limit">The limit value.</param>
    /// <param name="offset">The offset value.</param>
    /// <returns>The formatted LIMIT clause.</returns>
    string FormatLimitClause(int? limit, int? offset);

    /// <summary>
    /// Formats an identifier (table/column name) with proper quoting.
    /// </summary>
    /// <param name="identifier">The identifier to format.</param>
    /// <returns>The formatted identifier.</returns>
    string QuoteIdentifier(string identifier);
}

/// <summary>
/// Standard SQL dialect (ANSI SQL).
/// </summary>
public class StandardSqlDialect : ISqlDialect
{
    /// <inheritdoc/>
    public virtual string Name => "Standard SQL";

    /// <inheritdoc/>
    public virtual bool SupportsRightJoin => true;

    /// <inheritdoc/>
    public virtual bool SupportsFullOuterJoin => true;

    /// <inheritdoc/>
    public virtual bool SupportsSubqueriesInFrom => true;

    /// <inheritdoc/>
    public virtual bool SupportsSubqueriesInWhere => true;

    /// <inheritdoc/>
    public virtual bool SupportsLimitOffset => true;

    /// <inheritdoc/>
    public virtual bool SupportsCTE => true;

    /// <inheritdoc/>
    public virtual bool SupportsReturning => false;

    /// <inheritdoc/>
    public virtual bool SupportsWindowFunctions => true;

    /// <inheritdoc/>
    public virtual string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant();
    }

    /// <inheritdoc/>
    public virtual string FormatLimitClause(int? limit, int? offset)
    {
        if (limit == null && offset == null)
            return string.Empty;

        if (offset != null)
            return $"LIMIT {limit ?? int.MaxValue} OFFSET {offset}";

        return $"LIMIT {limit}";
    }

    /// <inheritdoc/>
    public virtual string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier}\"";
    }
}

/// <summary>
/// SQLite dialect.
/// </summary>
public class SqliteDialect : StandardSqlDialect
{
    /// <inheritdoc/>
    public override string Name => "SQLite";

    /// <inheritdoc/>
    public override bool SupportsRightJoin => false;

    /// <inheritdoc/>
    public override bool SupportsFullOuterJoin => false;

    /// <inheritdoc/>
    public override bool SupportsReturning => true;

    /// <inheritdoc/>
    public override string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "LEN" => "LENGTH",
            "GETDATE" => "DATE('now')",
            "GETUTCDATE" => "DATETIME('now')",
            _ => base.TranslateFunction(functionName)
        };
    }

    /// <inheritdoc/>
    public override string QuoteIdentifier(string identifier)
    {
        return $"[{identifier}]";
    }
}

/// <summary>
/// PostgreSQL dialect.
/// </summary>
public class PostgreSqlDialect : StandardSqlDialect
{
    /// <inheritdoc/>
    public override string Name => "PostgreSQL";

    /// <inheritdoc/>
    public override bool SupportsReturning => true;

    /// <inheritdoc/>
    public override string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "LEN" => "LENGTH",
            "GETDATE" => "NOW()",
            "GETUTCDATE" => "NOW() AT TIME ZONE 'UTC'",
            _ => base.TranslateFunction(functionName)
        };
    }
}

/// <summary>
/// MySQL dialect.
/// </summary>
public class MySqlDialect : StandardSqlDialect
{
    /// <inheritdoc/>
    public override string Name => "MySQL";

    /// <inheritdoc/>
    public override bool SupportsCTE => true; // MySQL 8.0+

    /// <inheritdoc/>
    public override string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "LEN" => "CHAR_LENGTH",
            "GETDATE" => "NOW()",
            "GETUTCDATE" => "UTC_TIMESTAMP()",
            _ => base.TranslateFunction(functionName)
        };
    }

    /// <inheritdoc/>
    public override string QuoteIdentifier(string identifier)
    {
        return $"`{identifier}`";
    }

    /// <inheritdoc/>
    public override string FormatLimitClause(int? limit, int? offset)
    {
        if (limit == null && offset == null)
            return string.Empty;

        if (offset != null)
            return $"LIMIT {offset}, {limit ?? int.MaxValue}";

        return $"LIMIT {limit}";
    }
}

/// <summary>
/// SQL Server (T-SQL) dialect.
/// </summary>
public class SqlServerDialect : StandardSqlDialect
{
    /// <inheritdoc/>
    public override string Name => "SQL Server";

    /// <inheritdoc/>
    public override bool SupportsLimitOffset => false; // Uses TOP and OFFSET/FETCH instead

    /// <inheritdoc/>
    public override string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "SUBSTR" => "SUBSTRING",
            "LENGTH" => "LEN",
            _ => base.TranslateFunction(functionName)
        };
    }

    /// <inheritdoc/>
    public override string QuoteIdentifier(string identifier)
    {
        return $"[{identifier}]";
    }

    /// <inheritdoc/>
    public override string FormatLimitClause(int? limit, int? offset)
    {
        // SQL Server uses OFFSET/FETCH NEXT syntax
        if (limit == null && offset == null)
            return string.Empty;

        if (offset != null)
            return $"OFFSET {offset} ROWS FETCH NEXT {limit ?? int.MaxValue} ROWS ONLY";

        return $"TOP {limit}";
    }
}

/// <summary>
/// SharpCoreDB native dialect (extends SQLite with custom features).
/// </summary>
public class SharpCoreDbDialect : SqliteDialect
{
    /// <inheritdoc/>
    public override string Name => "SharpCoreDB";

    /// <inheritdoc/>
    public override bool SupportsRightJoin => true;

    /// <inheritdoc/>
    public override bool SupportsFullOuterJoin => true;

    /// <inheritdoc/>
    public override bool SupportsCTE => true;

    /// <inheritdoc/>
    public override bool SupportsWindowFunctions => true;

    /// <inheritdoc/>
    public override string TranslateFunction(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "ULID" => "ULID",
            "NEWULID" => "ULID_NEW",
            _ => base.TranslateFunction(functionName)
        };
    }
}

/// <summary>
/// Factory for creating SQL dialects.
/// </summary>
public static class SqlDialectFactory
{
    /// <summary>
    /// Creates a dialect by name.
    /// </summary>
    /// <param name="dialectName">The dialect name.</param>
    /// <returns>The dialect instance.</returns>
    public static ISqlDialect Create(string dialectName)
    {
        return dialectName.ToLowerInvariant() switch
        {
            "sharpcoredb" or "sharpcore" => new SharpCoreDbDialect(),
            "sqlite" => new SqliteDialect(),
            "postgresql" or "postgres" or "pgsql" => new PostgreSqlDialect(),
            "mysql" or "mariadb" => new MySqlDialect(),
            "sqlserver" or "mssql" or "tsql" => new SqlServerDialect(),
            "standard" or "ansi" => new StandardSqlDialect(),
            _ => new SharpCoreDbDialect() // Default to SharpCoreDB
        };
    }

    /// <summary>
    /// Gets the default dialect for SharpCoreDB.
    /// </summary>
    public static ISqlDialect Default => new SharpCoreDbDialect();
}
