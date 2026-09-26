// <copyright file="SqlTypeAffinity.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// Single source of truth for mapping a declared SQL column type to an engine <see cref="DataType"/>.
/// The mapping used to be duplicated in five places (CREATE TABLE, both ALTER TABLE paths and two
/// legacy string-DDL paths in <c>DatabaseExtensions</c>), every copy recognising canonical names only
/// and routing the rest to <see cref="DataType.String"/>. That made a declared <c>DOUBLE</c> or
/// <c>FLOAT</c> a TEXT column instead of REAL, <c>INT</c>/<c>SMALLINT</c> TEXT instead of INTEGER, and
/// <c>DECIMAL(10,2)</c> TEXT because the size argument defeated the exact-name match.
/// </summary>
internal static class SqlTypeAffinity
{
    /// <summary>
    /// Keywords that terminate a column's declared type inside a CREATE TABLE column definition.
    /// <c>AUTO</c> and <c>AUTOINCREMENT</c> are SharpCoreDB/SQLite column modifiers, so they must not be
    /// read as part of the type (<c>ulid ULID AUTO</c> declares a ULID column, not a "ULID AUTO" one).
    /// </summary>
    private static readonly string[] ColumnConstraintKeywords =
    [
        "PRIMARY", "NOT", "NULL", "UNIQUE", "CHECK", "DEFAULT", "COLLATE", "REFERENCES",
        "GENERATED", "AUTO", "AUTOINCREMENT", "AUTO_INCREMENT", "AS", "CONSTRAINT",
        "DEFERRABLE", "INITIALLY", "STORAGE",
    ];

    /// <summary>
    /// Extracts the declared type text of a CREATE TABLE column definition: everything after the
    /// column name up to the first constraint keyword, with a size argument removed.
    /// SQLite determines column affinity from this text, so a multi-word type
    /// (<c>DOUBLE PRECISION</c>, <c>UNSIGNED BIG INT</c>) must survive intact, while
    /// <c>VARCHAR(255)</c> and <c>DECIMAL(10,2)</c> must be reduced to their base name.
    /// </summary>
    /// <param name="columnDefinitionParts">Column definition split on whitespace; index 0 is the column name.</param>
    /// <returns>The declared type text in upper case, or an empty string when the column declares no type.</returns>
    public static string ExtractDeclaredType(string[] columnDefinitionParts)
    {
        ArgumentNullException.ThrowIfNull(columnDefinitionParts);

        if (columnDefinitionParts.Length <= 1)
        {
            return string.Empty; // no type token at all
        }

        var declared = new System.Text.StringBuilder();
        var depth = 0;

        for (var i = 1; i < columnDefinitionParts.Length; i++)
        {
            var token = columnDefinitionParts[i];
            if (token.Length == 0)
            {
                continue;
            }

            var upper = token.ToUpperInvariant();

            // At depth 0 a constraint keyword means the type ended. That is also true for a type-less
            // column whose definition starts with a constraint, e.g. "payload CHECK (payload > 0)".
            if (depth == 0 && IsColumnConstraintKeyword(upper))
            {
                break;
            }

            if (declared.Length > 0)
            {
                declared.Append(' ');
            }

            declared.Append(upper);

            var closedSizeArgument = false;
            for (var c = 0; c < token.Length; c++)
            {
                if (token[c] == '(')
                {
                    depth++;
                }
                else if (token[c] == ')')
                {
                    depth--;
                    closedSizeArgument = true;
                }
            }

            if (closedSizeArgument && depth <= 0)
            {
                break; // the size argument closed inside this token
            }
        }

        return Normalize(declared.ToString());
    }

    /// <summary>
    /// Resolves the engine type for a declared SQL type name or declared type text
    /// (e.g. <c>DOUBLE PRECISION</c>, <c>VARCHAR(255)</c>, <c>VECTOR(1536)</c>).
    /// Canonical SharpCoreDB names, their SQL aliases and the names the PostgreSQL catalog layer
    /// reports keep their documented meaning; every other name is classified with SQLite's
    /// documented affinity rules ("Datatypes In SQLite", §3.1 Determination Of Column Affinity),
    /// so a name containing <c>INT</c> is INTEGER, <c>CHAR</c>/<c>CLOB</c>/<c>TEXT</c> is TEXT,
    /// <c>BLOB</c> is BLOB and <c>REAL</c>/<c>FLOA</c>/<c>DOUB</c> is REAL.
    /// </summary>
    /// <param name="declaredType">Declared type text; may carry a size argument, or be empty (no declared type).</param>
    /// <param name="useSqliteIntegerAffinity">
    /// When true the INTEGER affinity bucket maps to <see cref="DataType.Long"/> (SQLite's INTEGER is
    /// 64-bit); when false it keeps the engine's historical <see cref="DataType.Integer"/> (Int32)
    /// mapping so existing databases and consumer code do not change behavior.
    /// </param>
    /// <returns>The engine data type for the declared type text.</returns>
    public static DataType Resolve(string? declaredType, bool useSqliteIntegerAffinity)
    {
        var upper = Normalize(declaredType);
        if (upper.Length == 0)
        {
            return DataType.Blob; // SQLite rule 3: a column with no declared type has BLOB affinity
        }

        var integerAffinity = useSqliteIntegerAffinity ? DataType.Long : DataType.Integer;

        return upper switch
        {
            "INTEGER" or "INT" or "INT2" or "INT4" or "INT16" or "INT32" or "SMALLINT" or "TINYINT" or "MEDIUMINT"
                => integerAffinity,
            "BIGINT" or "INT8" or "INT64" or "LONG" => DataType.Long,
            "TEXT" or "CLOB" or "CHAR" or "NCHAR" or "CHARACTER" or "VARCHAR" or "NVARCHAR" => DataType.String,
            "REAL" or "FLOAT" or "FLOAT4" or "FLOAT8" or "DOUBLE" or "DOUBLE PRECISION" => DataType.Real,
            "BLOB" or "BYTEA" or "BINARY" or "VARBINARY" => DataType.Blob,
            "BOOLEAN" or "BOOL" => DataType.Boolean,
            "DATETIME" or "DATE" or "TIMESTAMP" or "TIMESTAMPTZ" => DataType.DateTime,
            "DECIMAL" or "NUMERIC" or "NUMBER" => DataType.Decimal,
            "GUID" or "UUID" => DataType.Guid,
            "ULID" => DataType.Ulid,
            "ROWREF" => DataType.RowRef,
            "VECTOR" => DataType.Vector,
            _ => ResolveBySqliteAffinity(upper, integerAffinity),
        };
    }

    /// <summary>
    /// SQLite affinity determination for a declared type that is not a known name. The branches and
    /// their order are SQLite's own; only the fallback differs deliberately (see <see cref="Resolve"/>).
    /// </summary>
    /// <param name="upperDeclaredType">Declared type text, upper case, without a size argument.</param>
    /// <param name="integerAffinity">The engine type the INTEGER affinity bucket resolves to.</param>
    /// <returns>The engine data type for the declared type text.</returns>
    private static DataType ResolveBySqliteAffinity(string upperDeclaredType, DataType integerAffinity)
    {
        if (upperDeclaredType.Contains("INT", StringComparison.Ordinal))
        {
            return integerAffinity;
        }

        if (upperDeclaredType.Contains("CHAR", StringComparison.Ordinal)
            || upperDeclaredType.Contains("CLOB", StringComparison.Ordinal)
            || upperDeclaredType.Contains("TEXT", StringComparison.Ordinal))
        {
            return DataType.String;
        }

        if (upperDeclaredType.Contains("BLOB", StringComparison.Ordinal))
        {
            return DataType.Blob;
        }

        if (upperDeclaredType.Contains("REAL", StringComparison.Ordinal)
            || upperDeclaredType.Contains("FLOA", StringComparison.Ordinal)
            || upperDeclaredType.Contains("DOUB", StringComparison.Ordinal))
        {
            return DataType.Real;
        }

        // SQLite gives these names NUMERIC affinity, which stores a number as INTEGER or REAL and
        // anything else as TEXT. A strongly typed engine has no such dual-storage column type, and
        // TEXT is what this engine always mapped an unrecognised name to, so that behavior is kept.
        // Pinned by DeclaredTypeAffinityTests.CreateTable_WithMultiWordAndUnknownTypes_ClassifiesEveryOne.
        return DataType.String;
    }

    /// <summary>Upper-cases a declared type and drops a size argument (<c>VARCHAR(255)</c> → <c>VARCHAR</c>).</summary>
    /// <param name="declaredType">Declared type text as written in the statement.</param>
    /// <returns>The base type name in upper case, or an empty string.</returns>
    private static string Normalize(string? declaredType)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return string.Empty;
        }

        var text = declaredType.Trim();
        var sizeStart = text.IndexOf('(');
        if (sizeStart >= 0)
        {
            text = text[..sizeStart];
        }

        return text.Trim().ToUpperInvariant();
    }

    /// <summary>Returns true when the token is a column-constraint keyword rather than part of a type.</summary>
    /// <param name="upperToken">Upper-cased token from the column definition.</param>
    /// <returns>True when the token ends the declared type.</returns>
    private static bool IsColumnConstraintKeyword(string upperToken)
        => Array.IndexOf(ColumnConstraintKeywords, upperToken) >= 0;
}
