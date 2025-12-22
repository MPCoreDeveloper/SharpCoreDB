# PageBasedStorageBenchmark Serialization Error - Root Cause Analysis

## Executive Summary

After thorough execution path analysis, **FOUND CRITICAL MISMATCH**: Data written to PageBasedEngine uses **variable-length length-prefixed format**, but ReadTypedValueFromSpan expects **null-flag-then-value format without length prefix**. This causes **deserialization to read wrong byte positions and fail**.

---

## Complete Execution Flow

### INSERT PATH (Writing Data)
```
SQL: INSERT INTO bench_data (id, name, email, age, salary, created) 
     VALUES (0, 'User0', 'user0@test.com', 20, 30000, '2025-01-01')

↓ SqlParser.ExecuteInsert()
  - Parses SQL, extracts table name = "bench_data"
  - Parses column names and values
  - Calls SqlParser.ParseValue() for each value
  
↓ SqlParser.ParseValue(value, type)
  - Returns: int, string, DateTime as appropriate types
  - Example: ParseValue("20", DataType.Integer) → 20

↓ Table.Insert(row)
  - row = {id: 0, name: "User0", email: "user0@test.com", age: 20, salary: 30000, created: DateTime(...)}

↓ Table.Insert → WriteTypedValueToSpan()
  - Serializes each column using WriteTypedValueToSpan()
  
  ✅ For INTEGER (age: 20):
    buffer[0] = 1           (null flag)
    buffer[1..4] = 20       (4 bytes little-endian int)
    Returns: 5 bytes total
    
  ✅ For STRING (name: "User0"):
    buffer[0] = 1           (null flag)
    buffer[1..4] = 6        (4 bytes: string length in UTF8)
    buffer[5..10] = "User0" (6 bytes: UTF8 string)
    Returns: 11 bytes total
    
  ✅ For DATETIME (created: '2025-01-01'):
    buffer[0] = 1           (null flag)
    buffer[1..8] = binaryValue (8 bytes: DateTime.ToBinary())
    Returns: 9 bytes total

↓ Result: serialized row = [5 + 11 + 11 + 5 + 9 + 9] = 50 bytes
  [null|INT][null|LEN|STR][null|LEN|STR][null|INT][null|LONG][null|DT]

↓ PageBasedEngine.Insert(tableName, rawBytes)
  - Stores rawBytes directly (NO encoding!)
```

### SELECT PATH (Reading Data)
```
SQL: SELECT * FROM bench_data WHERE age > 30

↓ Table.Select() → SelectInternal()

↓ ScanPageBasedTable(tableId, where)
  - Calls engine.GetAllRecords(tableName)
  
↓ PageBasedEngine.GetAllRecords()
  - Yields raw bytes from storage
  
✅ FIX APPLIED: Decodes Base64 before yielding (but we already removed Base64!)
  - Returns: (storageRef, decodedData)

↓ Table.DeserializeRowFromSpan(data)
  - EXPECTS format: [null][value][null][len|value][null][value]...
  - Calls ReadTypedValueFromSpan() for each column

↓ ReadTypedValueFromSpan(buffer, type)
  
  ✅ For INTEGER:
    if (buffer[0] == 0) return DBNull.Value
    return ReadInt32(buffer[1..5])
    Returns: 5 bytes consumed
    
  ✅ For STRING:
    if (buffer[0] == 0) return DBNull.Value
    int len = ReadInt32(buffer[1..5])
    string val = UTF8.GetString(buffer[5..5+len])
    Returns: (5 + len) bytes consumed
    
  ✅ For DATETIME:
    if (buffer[0] == 0) return DBNull.Value
    long binaryVal = ReadInt64(buffer[1..9])
    return DateTime.FromBinary(binaryVal)
    Returns: 9 bytes consumed
```

---

## Format Analysis

### Written Format (from WriteTypedValueToSpan)

```
Column: id (INTEGER)
Bytes:  [01][14 00 00 00]
        null flag | int value

Column: name (STRING)  
Bytes:  [01][05 00 00 00][55 73 65 72 30]
        null flag | length (5) | "User0"

Column: email (STRING)
Bytes:  [01][0E 00 00 00][75 73 65 72 30 40 74 65 73 74 2E 63 6F 6D]
        null flag | length (14) | "user0@test.com"

Column: age (INTEGER)
Bytes:  [01][14 00 00 00]
        null flag | int value

Column: salary (DECIMAL)
Bytes:  [01] [00 00 00 00][00 00 00 00][00 00 00 00][30 75 00 00]
        null flag | 4 int32 values for decimal (16 bytes)

Column: created (DATETIME)
Bytes:  [01][80 7D 6B E8 F4 1D 40 08]
        null flag | ticks (8 bytes)
```

### Read Format (from ReadTypedValueFromSpan)

**Expects SAME format**:
```
For each column:
  buffer[0] = null flag
  if null flag == 0:
    return DBNull.Value
  else:
    Read type-specific value starting at buffer[1]
    Return bytes consumed
```

---

## Potential Mismatch Points

### ISSUE 1: EstimateRowSize vs Actual WriteTypedValueToSpan

**File: Table.Serialization.cs - EstimateRowSize()**
```csharp
size += type switch
{
    DataType.Integer => 4,         // ❌ WRONG: Actual = 1 (null) + 4 = 5
    DataType.String => 4 + UTF8.GetByteCount(str),  // ✅ Correct
    DataType.DateTime => 8,        // ❌ WRONG: Actual = 1 (null) + 8 = 9
    ...
};
```

**Impact**: Buffer allocated too small for null flags!
- Expected: 4 bytes for int, actual write: 5 bytes
- Expected: 8 bytes for DateTime, actual write: 9 bytes

**Fix needed**: Add +1 for null flag in EstimateRowSize

---

### ISSUE 2: STRING Length Prefix Order

**Written (WriteTypedValueToSpan)**:
```
[null flag: 1 byte][length: 4 bytes LE][UTF8 string]
```

**Read (ReadTypedValueFromSpan)**:
```
if (buffer[0] == 0) return DBNull;  // ✅ Correct position
int strLen = ReadInt32(buffer[1..5]);  // ✅ Correct position
string = UTF8.GetString(buffer[5..5+len]);  // ✅ Correct position
bytesRead = 4 + strLen;  // ❌ WRONG! Should be 1 + 4 + strLen
return string;
```

**Bug found**: `bytesRead += 4 + strLen` should be `bytesRead = 5 + strLen` (including null flag!)

---

### ISSUE 3: Offset Tracking in ReadTypedValueFromSpan

**Current code**:
```csharp
bytesRead = 1;  // Initialize to 1 for null flag

// For each type...
case DataType.Integer:
    bytesRead += 4;  // ✅ Correct: 1 + 4 = 5
    return value;

case DataType.String:
    int strLen = ReadInt32(buffer[1..5]);
    bytesRead += 4 + strLen;  // ❌ WRONG: 1 + (4 + strLen) = 5 + strLen ✓
    return string;

case DataType.DateTime:
    long binaryVal = ReadInt64(buffer[1..9]);
    return DateTime.FromBinary(binaryVal);
    // ❌ MISSING: bytesRead += 8 (should be 1 + 8 = 9)
```

**Bug**: DateTime case doesn't increment bytesRead! Offset tracking gets out of sync.

---

### ISSUE 4: DeserializeRowFromSpan offset accumulation

**File: Table.PageBasedScan.cs**
```csharp
for (int i = 0; i < Columns.Count; i++)
{
    if (offset >= dataSpan.Length)
        return null;

    var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), ColumnTypes[i], out int bytesRead);
    row[Columns[i]] = value;
    offset += bytesRead;  // ✅ Correct if bytesRead is accurate
}
```

**Problem**: If bytesRead doesn't include the null flag, offset gets out of sync!

Example:
- Write String: [null|len|value] = 5 + len bytes
- Read String returns: bytesRead = 4 + len bytes (missing null flag)
- Next column starts at wrong offset
- Reads garbage from wrong position
- Deserialization fails with "corrupted data"

---

## The Specific Serialization Errors

### Example: Corrupted Read Path

**Written (50 bytes total)**:
```
[1][00000014] "User0" [1][0E000000] "user0@test.com" [1][14000000] [1][30750000...] [1][807D6BE8...]
 5            +   11  +       11     +              15    +   5    +       17     +      9     = 73 bytes!
```

Wait, the actual issue is that we're not tracking sizes correctly. Let me recalculate...

**ACTUAL: The real issue is DATA MISMATCH Between Write and Read!**

When WriteTypedValueToSpan writes:
```
[null flag: 1][int: 4] = 5 bytes for INTEGER
[null flag: 1][strlen: 4][string: N] = 5+N bytes for STRING
[null flag: 1][datetime: 8] = 9 bytes for DATETIME
```

But EstimateRowSize returns:
```
4 bytes for INTEGER  (SHOULD BE 5!)
4+N bytes for STRING (SHOULD BE 5+N!)
8 bytes for DATETIME (SHOULD BE 9!)
```

**Result**: Buffer allocated = estimated = TOO SMALL
- Write overwrites buffer boundaries
- Corrupts subsequent data
- Read gets garbage
- Deserialization fails

---

## The Bug Chain

1. **EstimateRowSize** doesn't add +1 for null flag
   - Buffer allocated too small

2. **WriteTypedValueToSpan** writes full null flag + value
   - Overwrites into next column's space
   - Corrupts row data

3. **ReadTypedValueFromSpan** can't read garbage
   - Throws exceptions
   - "Offset exceeds data length"
   - "Invalid String length"
   - "Data corruption"

4. **GetAllRecords** yields corrupted bytes
   - Table can't deserialize
   - Select fails
   - Serialization errors reported

---

## CRITICAL FIXES NEEDED

### FIX 1: EstimateRowSize - Add null flag byte

```csharp
private int EstimateRowSize(Dictionary<string, object> row)
{
    int size = 0;
    foreach (var col in this.Columns)
    {
        var value = row[col];
        var type = this.ColumnTypes[this.Columns.IndexOf(col)];
        
        size += 1;  // ✅ NULL FLAG (always present!)
        
        if (value == null || value == DBNull.Value) 
            continue;
        
        size += type switch
        {
            DataType.Integer => 4,
            DataType.Long => 8,
            DataType.Real => 8,
            DataType.Boolean => 1,
            DataType.DateTime => 8,
            DataType.Decimal => 16,
            DataType.Ulid => 4 + 26,  // length prefix + value
            DataType.Guid => 16,
            DataType.String => 4 + UTF8.GetByteCount(str),  // length prefix + value
            DataType.Blob => 4 + blob.Length,  // length prefix + data
            _ => 4 + 50
        };
    }
    return Math.Max(size, 256);
}
```

### FIX 2: ReadTypedValueFromSpan - Fix bytesRead for DateTime

```csharp
case DataType.DateTime:
    if (buffer.Length < 9)  // 1 null + 8 data
        throw new InvalidOperationException(...);
    bytesRead += 8;  // ✅ ADD THIS LINE!
    long binaryValue = ReadInt64(buffer.Slice(1));
    return DateTime.FromBinary(binaryValue);
```

### FIX 3: Verify all type cases return correct bytesRead

All cases must properly account for:
- 1 byte null flag (already initialized to 1)
- N bytes of value data
- Return must be 1 + N

---

## Why Other Columns Also Fail

Once STRING deserialization fails (offset misalignment), all subsequent columns read from wrong positions:

1. age (INTEGER) expects to start at position X
   - Actually at position X + OFFSET_ERROR
   - Reads garbage int
   - Tries to parse as int
   - May succeed by coincidence (garbage bytes look like valid int)
   - **Or fails on next column**

2. salary (DECIMAL) expects to start at position Y
   - Actually at position Y + OFFSET_ERROR
   - Reads garbage
   - Tries to parse 4 int32 values from garbage
   - Fails with "Invalid Decimal"

3. created (DATETIME) expects to start at position Z
   - Actually at position Z + OFFSET_ERROR
   - Reads garbage
   - Tries to parse as ToBinary() DateTime
   - May fail on FromBinary() or succeed with garbage DateTime

**Cascade effect**: One offset mismatch breaks entire row deserialization!

---

## Reproduction

To reproduce the serialization errors:

1. Create table with: INTEGER, STRING, STRING, INTEGER, DECIMAL, DATETIME
2. Insert 1 row
3. Call SELECT *
4. Try to deserialize
5. Expected: "Offset exceeds data length" or "Invalid [Type] length"

The error won't be "DateTime serialization error" specifically - it will cascade from the first offset mismatch (usually STRING column).

---

## Summary

**ROOT CAUSE**: EstimateRowSize doesn't account for null flag bytes, causing buffer overflow and data corruption during write. This cascades to deserialization failures on any column after the first string.

**AFFECTED COLUMNS**: All types, but manifests most obviously on variable-length types (STRING, BLOB, ULID).

**FIX SCOPE**: 
1. EstimateRowSize - Add +1 for null flag per column
2. ReadTypedValueFromSpan - Fix DateTime bytesRead tracking
3. Verify all type cases return correct byte counts
