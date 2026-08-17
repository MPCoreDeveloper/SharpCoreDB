namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Builds CREATE TABLE + INSERT SQL statements from pasted CSV content.
/// Column names come from the header row; types are inferred per column.
/// Values are escaped as SQL literals (safe against injection).
/// </summary>
public static class CsvImportBuilder
{
    /// <summary>
    /// Builds the CREATE TABLE and INSERT statements for the given CSV.
    /// </summary>
    /// <param name="tableName">Target table name.</param>
    /// <param name="csvContent">CSV content including a header row.</param>
    /// <returns>CREATE SQL and a list of INSERT statements.</returns>
    public static (string CreateSql, IReadOnlyList<string> InsertSqls) BuildSql(string tableName, string csvContent)
    {
        var lines = SplitLines(csvContent);
        if (lines.Count < 2)
        {
            throw new InvalidOperationException("CSV must contain a header row and at least one data row.");
        }

        var headers = ParseCsvLine(lines[0]);
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("CSV header row is empty.");
        }

        var uniqueHeaders = headers
            .Select((header, index) => (header: string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : header.Trim(), index))
            .ToList();

        var dataRows = new List<List<string>>(lines.Count - 1);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            dataRows.Add(ParseCsvLine(line));
        }

        var columnTypes = InferColumnTypes(uniqueHeaders.Select(h => h.header).ToArray(), dataRows);

        var escapedTable = EscapeIdentifier(tableName);
        var columns = string.Join(", ", uniqueHeaders.Select(h => $"\"{EscapeIdentifier(h.header)}\" {columnTypes[h.index]}"));
        var createSql = $"CREATE TABLE IF NOT EXISTS \"{escapedTable}\" ({columns});";

        var inserts = new List<string>(dataRows.Count);
        foreach (var row in dataRows)
        {
            var values = new List<string>(uniqueHeaders.Count);
            for (var i = 0; i < uniqueHeaders.Count; i++)
            {
                var raw = i < row.Count ? row[i] : string.Empty;
                values.Add(ToSqlLiteral(raw, columnTypes[i]));
            }

            inserts.Add($"INSERT INTO \"{escapedTable}\" ({string.Join(", ", uniqueHeaders.Select(h => $"\"{EscapeIdentifier(h.header)}\""))}) VALUES ({string.Join(", ", values)});");
        }

        return (createSql, inserts);
    }

    /// <summary>
    /// Splits CSV content into lines, preserving quoted multiline values.
    /// </summary>
    private static List<string> SplitLines(string csvContent)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in csvContent)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
            }
            else if (ch == '\n' && !inQuotes)
            {
                var line = current.ToString().TrimEnd('\r');
                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            var last = current.ToString().TrimEnd('\r');
            if (last.Length > 0)
            {
                lines.Add(last);
            }
        }

        return lines;
    }

    /// <summary>
    /// Parses a single CSV line into fields, handling quoted values with commas.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>
    /// Infers column types (INTEGER, REAL, TEXT) by sampling data rows.
    /// </summary>
    private static string[] InferColumnTypes(string[] headers, List<List<string>> rows)
    {
        var types = new string[headers.Length];

        for (var i = 0; i < headers.Length; i++)
        {
            types[i] = "TEXT";

            var allIntegers = true;
            var allReals = true;

            foreach (var row in rows)
            {
                var raw = i < row.Count ? row[i].Trim() : string.Empty;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                if (!long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    allIntegers = false;
                }

                if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    allReals = false;
                }
            }

            if (allIntegers)
            {
                types[i] = "INTEGER";
            }
            else if (allReals)
            {
                types[i] = "REAL";
            }
        }

        return types;
    }

    /// <summary>
    /// Formats a CSV value as a safe SQL literal matching its inferred type.
    /// </summary>
    private static string ToSqlLiteral(string raw, string columnType)
    {
        var trimmed = raw?.Trim() ?? string.Empty;

        if (columnType is "INTEGER" or "REAL")
        {
            if (string.IsNullOrEmpty(trimmed))
            {
                return "NULL";
            }

            return trimmed;
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return "NULL";
        }

        return $"'{trimmed.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}