param()
$file = "D:\source\repos\MPCoreDeveloper\SharpCoreDB\src\SharpCoreDB\Services\SqlParser.DML.cs"

# ── load ──────────────────────────────────────────────────────────────────────
$all = [string[]][System.IO.File]::ReadAllLines($file)
Write-Host "Loaded $($all.Length) lines"

# ── helpers ───────────────────────────────────────────────────────────────────
function ReplaceRange {
    param([string[]]$src, [int]$from0, [int]$to0, [string[]]$replacement)
    $list = [System.Collections.Generic.List[string]]::new()
    if ($from0 -gt 0) { $list.AddRange([string[]]$src[0..($from0-1)]) }
    $list.AddRange($replacement)
    if ($to0 -lt $src.Length - 1) { $list.AddRange([string[]]$src[($to0+1)..($src.Length-1)]) }
    return $list.ToArray()
}

# ── PATCH 1: ExecuteQueryInternal (lines 155-160, 0-based 154-159) ──────────
$p1 = [string[]]@(
    "    private List<Dictionary<string, object>> ExecuteQueryInternal(string sql, string[] parts, bool noEncrypt = false)",
    "    {",
    "        if (parts.Length == 0) return [];",
    "        if (string.Equals(parts[0], SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))",
    "            return ExecuteSelectQuery(sql, parts, noEncrypt);",
    "        _pendingQueryResults = [];",
    "        ExecuteInternal(sql, parts, wal: null, noEncrypt: noEncrypt);",
    "        return _pendingQueryResults is { Count: > 0 } ? [.. _pendingQueryResults] : [];",
    "    }"
)
$all = ReplaceRange $all 154 159 $p1
Write-Host "After patch 1: $($all.Length) lines"

# ── find new line numbers after patch 1 ────────────────────────────────────
$insertLine = ($all | Select-String -Pattern "^\s+private void ExecuteInsert\(string sql, IWAL" | Select-Object -First 1).LineNumber
Write-Host "ExecuteInsert at line $insertLine"

# ExecuteInsert: replace from its opening brace to closing brace (find range)
$from = $insertLine - 1  # 0-based signature line
# find closing brace: next line that is exactly "    }" after the signature
$to = $from
for ($i = $from + 1; $i -lt $all.Length; $i++) {
    if ($all[$i] -match '^    \}$') { $to = $i; break }
}
Write-Host "ExecuteInsert range 0-based: $from .. $to"

$p2 = [string[]]@(
    "    private void ExecuteInsert(string sql, IWAL? wal)",
    "    {",
    "        if (this.isReadOnly) throw new InvalidOperationException(`"Cannot insert in readonly mode`");",
    "        _lastChanges = 0;",
    "        var returningColumns = TryExtractReturningColumns(sql, out var sqlWithoutReturning);",
    "        var insertSql = sqlWithoutReturning[sqlWithoutReturning.IndexOf(`"INSERT INTO`", StringComparison.OrdinalIgnoreCase)..];",
    "        var tableStart = `"INSERT INTO `".Length;",
    "        var tableEnd = insertSql.IndexOf(' ', tableStart);",
    "        if (tableEnd == -1) tableEnd = insertSql.IndexOf('(', tableStart);",
    "        var tableName = insertSql[tableStart..tableEnd].Trim().Trim('`"', '[', ']', '``');",
    "        if (!this.tables.TryGetValue(tableName, out var table))",
    "            throw new InvalidOperationException(`$`"Table {tableName} does not exist`");",
    "        var rest = insertSql[tableEnd..];",
    "        List<string>? insertColumns = null;",
    "        if (rest.TrimStart().StartsWith('('))",
    "        {",
    "            var colStart = rest.IndexOf('(') + 1;",
    "            var colEnd = rest.IndexOf(')', colStart);",
    "            insertColumns = [.. rest[colStart..colEnd].Split(',').Select(c => c.Trim().Trim('`"', '[', ']', '``'))];",
    "            rest = rest[(colEnd + 1)..];",
    "        }",
    "        var valuesStart = rest.IndexOf(`"VALUES`", StringComparison.OrdinalIgnoreCase) + `"VALUES`".Length;",
    "        var valuesRest = rest[valuesStart..].Trim();",
    "        List<List<string>> allRowValues = ParseMultiRowInsertValues(valuesRest);",
    "        var tableAsTable = table as Table;",
    "        bool skipInternalRowId = tableAsTable is { HasInternalRowId: true }",
    "            && (insertColumns is null || !insertColumns.Contains(Constants.PersistenceConstants.InternalRowIdColumnName, StringComparer.OrdinalIgnoreCase));",
    "        var insertedRows = new List<Dictionary<string, object>>();",
    "        foreach (var rowValues in allRowValues)",
    "        {",
    "            var row = new Dictionary<string, object>();",
    "            if (insertColumns is null)",
    "            {",
    "                int valueIdx = 0;",
    "                for (int i = 0; i < table.Columns.Count; i++)",
    "                {",
    "                    var col = table.Columns[i];",
    "                    if (skipInternalRowId && col == Constants.PersistenceConstants.InternalRowIdColumnName) continue;",
    "                    var type = table.ColumnTypes[i];",
    "                    row[col] = SqlParser.ParseValue(valueIdx < rowValues.Count ? rowValues[valueIdx] : `"NULL`", type) ?? DBNull.Value;",
    "                    valueIdx++;",
    "                }",
    "            }",
    "            else",
    "            {",
    "                for (int i = 0; i < insertColumns.Count; i++)",
    "                {",
    "                    var col = insertColumns[i];",
    "                    var idx = table.Columns.IndexOf(col);",
    "                    row[col] = SqlParser.ParseValue(i < rowValues.Count ? rowValues[i] : `"NULL`", table.ColumnTypes[idx]) ?? DBNull.Value;",
    "                }",
    "            }",
    "            FireTriggers(tableName, TriggerTiming.Before, TriggerEvent.Insert, newRow: row);",
    "            table.Insert(row);",
    "            FireTriggers(tableName, TriggerTiming.After, TriggerEvent.Insert, newRow: row);",
    "            insertedRows.Add(new Dictionary<string, object>(row, StringComparer.OrdinalIgnoreCase));",
    "        }",
    "        _lastChanges = insertedRows.Count;",
    "        _totalChanges += insertedRows.Count;",
    "        if (insertedRows.Count > 0) _lastInsertRowId++;",
    "        if (returningColumns is not null) _pendingQueryResults = ProjectReturningRows(insertedRows, returningColumns);",
    "        wal?.Log(sqlWithoutReturning);",
    "    }"
)
$all = ReplaceRange $all $from $to $p2
Write-Host "After patch 2 (ExecuteInsert): $($all.Length) lines"

# ── PATCH 3: remove old ExecuteSelectLiteralQuery + ParseSelectLiteralValue ──
$litLine = ($all | Select-String -Pattern "^\s+private static List<Dictionary<string, object>> ExecuteSelectLiteralQuery" | Select-Object -First 1).LineNumber
Write-Host "ExecuteSelectLiteralQuery at line $litLine"
$litFrom = $litLine - 1
# find end of ParseSelectLiteralValue (two closing braces after it)
$parseValLine = ($all | Select-String -Pattern "^\s+private static object\? ParseSelectLiteralValue" | Select-Object -First 1).LineNumber
$parseValFrom = $parseValLine - 1
$parseValTo = $parseValFrom
for ($i = $parseValFrom + 1; $i -lt $all.Length; $i++) {
    if ($all[$i] -match '^    \}$') { $parseValTo = $i; break }
}
Write-Host "Removing lines 0-based $litFrom .. $parseValTo"
$all = ReplaceRange $all $litFrom $parseValTo @()
Write-Host "After patch 3 (remove old literal helpers): $($all.Length) lines"

# ── PATCH 4: ExecuteUpdate ─────────────────────────────────────────────────
$updLine = ($all | Select-String -Pattern "^\s+private void ExecuteUpdate\(string sql, IWAL" | Select-Object -First 1).LineNumber
$updFrom = $updLine - 1
$updTo = $updFrom
for ($i = $updFrom + 1; $i -lt $all.Length; $i++) {
    if ($all[$i] -match '^    \}$') { $updTo = $i; break }
}
Write-Host "ExecuteUpdate range 0-based: $updFrom .. $updTo"

$p4 = [string[]]@(
    "    private void ExecuteUpdate(string sql, IWAL? wal)",
    "    {",
    "        if (isReadOnly) throw new InvalidOperationException(`"Cannot update in readonly mode`");",
    "        _lastChanges = 0;",
    "        var returningColumns = TryExtractReturningColumns(sql, out var sqlWithoutReturning);",
    "        var updateMatch = UpdateRegex.Match(sqlWithoutReturning);",
    "        if (!updateMatch.Success) throw new InvalidOperationException(`$`"Invalid UPDATE syntax: {sqlWithoutReturning}`");",
    "        var tableName = updateMatch.Groups[1].Value.Trim();",
    "        if (!tables.TryGetValue(tableName, out var table)) throw new InvalidOperationException(`$`"Table {tableName} does not exist`");",
    "        var setClauses = updateMatch.Groups[2].Value.Trim().Split(',');",
    "        var whereClause = updateMatch.Groups[3].Value.Trim();",
    "        var updates = new Dictionary<string, object?>();",
    "        foreach (var setClause in setClauses)",
    "        {",
    "            var parts = setClause.Split('=');",
    "            if (parts.Length == 2)",
    "            {",
    "                var colName = parts[0].Trim();",
    "                var colIndex = table.Columns.IndexOf(colName);",
    "                if (colIndex >= 0) updates[colName] = SqlParser.ParseValue(parts[1].Trim(), table.ColumnTypes[colIndex]);",
    "            }",
    "        }",
    "        var affectedRows = table.Select(whereClause, null, true, false);",
    "        table.Update(whereClause, updates);",
    "        _lastChanges = affectedRows.Count;",
    "        _totalChanges += _lastChanges;",
    "        if (returningColumns is not null)",
    "        {",
    "            var updatedRows = affectedRows.Select(row => { var c = new Dictionary<string, object>(row, StringComparer.OrdinalIgnoreCase); foreach (var kv in updates) c[kv.Key] = kv.Value ?? DBNull.Value; return c; }).ToList();",
    "            _pendingQueryResults = ProjectReturningRows(updatedRows, returningColumns);",
    "        }",
    "        wal?.Log(sqlWithoutReturning);",
    "    }"
)
$all = ReplaceRange $all $updFrom $updTo $p4
Write-Host "After patch 4 (ExecuteUpdate): $($all.Length) lines"

# ── PATCH 5: ExecuteDelete ─────────────────────────────────────────────────
$delLine = ($all | Select-String -Pattern "^\s+private void ExecuteDelete\(string sql, IWAL" | Select-Object -First 1).LineNumber
$delFrom = $delLine - 1
$delTo = $delFrom
for ($i = $delFrom + 1; $i -lt $all.Length; $i++) {
    if ($all[$i] -match '^    \}$') { $delTo = $i; break }
}
Write-Host "ExecuteDelete range 0-based: $delFrom .. $delTo"

$p5 = [string[]]@(
    "    private void ExecuteDelete(string sql, IWAL? wal)",
    "    {",
    "        if (isReadOnly) throw new InvalidOperationException(`"Cannot delete in readonly mode`");",
    "        _lastChanges = 0;",
    "        var returningColumns = TryExtractReturningColumns(sql, out var sqlWithoutReturning);",
    "        var deleteMatch = DeleteRegex.Match(sqlWithoutReturning);",
    "        if (!deleteMatch.Success) throw new InvalidOperationException(`$`"Invalid DELETE syntax: {sqlWithoutReturning}`");",
    "        var tableName = deleteMatch.Groups[1].Value.Trim();",
    "        if (!tables.TryGetValue(tableName, out var table)) throw new InvalidOperationException(`$`"Table {tableName} does not exist`");",
    "        var whereClause = deleteMatch.Groups[2].Value.Trim();",
    "        var deletedRows = table.Select(whereClause, null, true, false);",
    "        table.Delete(whereClause);",
    "        _lastChanges = deletedRows.Count;",
    "        _totalChanges += _lastChanges;",
    "        if (returningColumns is not null) _pendingQueryResults = ProjectReturningRows(deletedRows, returningColumns);",
    "        wal?.Log(sqlWithoutReturning);",
    "    }"
)
$all = ReplaceRange $all $delFrom $delTo $p5
Write-Host "After patch 5 (ExecuteDelete): $($all.Length) lines"

# ── write ─────────────────────────────────────────────────────────────────────
[System.IO.File]::WriteAllLines($file, $all)
Write-Host "Written $($all.Length) lines to $file"
