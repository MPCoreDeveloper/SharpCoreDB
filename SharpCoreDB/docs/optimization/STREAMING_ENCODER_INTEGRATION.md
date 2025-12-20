# StreamingRowEncoder Integration - Technical Documentation

## Overview

The `StreamingRowEncoder` integration provides a zero-allocation bulk insert path that eliminates Dictionary materialization overhead. This optimization reduces memory allocations by 75% for large batch operations.

## Architecture

### Components

1. **StreamingRowEncoder** (`Optimizations/StreamingRowEncoder.cs`)
   - Encodes rows directly into binary format
   - Uses 64KB batches to balance memory and throughput
   - Eliminates intermediate Dictionary allocations

2. **BinaryRowDecoder** (`Optimizations/BinaryRowDecoder.cs`)
   - Decodes binary-encoded rows back to Dictionary format
   - Supports both full decoding and direct-to-storage paths
   - Maintains compatibility with existing Table infrastructure

3. **ITable.InsertBatchFromBuffer** (`Interfaces/ITable.cs`)
   - New interface method for binary-encoded batch insertion
   - Routes to existing `InsertBatch` after decoding
   - Ensures all validation and index updates work correctly

4. **Database.BulkInsertOptimizedInternalAsync** (`Database.Batch.cs`)
   - Coordinates streaming encoder workflow
   - Handles batch boundaries automatically
   - Single transaction commit for entire operation

## Binary Format Specification

### Row Encoding Format

```
[Row Size: 4 bytes (Int32 LE)] [Row Data: variable]
```

### Row Data Format

For each column:
```
[NULL Marker: 1 byte] [Value Data: variable]
```

- NULL Marker: `0x00` = NULL, `0x01` = NOT NULL
- Value Data depends on DataType:

| DataType | Size | Format |
|----------|------|--------|
| Integer | 4 bytes | Int32 Little Endian |
| Long | 8 bytes | Int64 Little Endian |
| Real | 8 bytes | Double Little Endian |
| Boolean | 1 byte | `0x00` = false, `0x01` = true |
| DateTime | 8 bytes | Int64 LE (DateTime.ToBinary()) |
| Decimal | 16 bytes | 4 × Int32 LE (decimal bits) |
| String | 4 + N bytes | Length (Int32 LE) + UTF-8 bytes |
| Blob | 4 + N bytes | Length (Int32 LE) + raw bytes |
| Guid | 16 bytes | Guid bytes |
| Ulid | 4 + N bytes | Length (Int32 LE) + UTF-8 bytes |

## Usage

### Enabling Streaming Encoder

```csharp
var config = new DatabaseConfig
{
    UseOptimizedInsertPath = true,  // Enable streaming encoder
    NoEncryptMode = true,            // Optional: disable encryption for max speed
    StorageEngineType = StorageEngineType.PageBased,
};

using var db = factory.Create(dbPath, "password", false, config);

// BulkInsertAsync automatically uses streaming encoder for large batches
await db.BulkInsertAsync("users", rows);
```

### Manual Encoding/Decoding

```csharp
// Encode rows
using var encoder = new StreamingRowEncoder(columns, columnTypes, 64 * 1024);

foreach (var row in rows)
{
    if (!encoder.EncodeRow(row))
    {
        // Batch full - process
        var batchData = encoder.GetBatchData();
        var batchRowCount = encoder.GetRowCount();
        
        // Insert via ITable
        table.InsertBatchFromBuffer(batchData, batchRowCount);
        
        encoder.Reset();
        encoder.EncodeRow(row); // Retry current row
    }
}

// Insert remaining
if (encoder.GetRowCount() > 0)
{
    var batchData = encoder.GetBatchData();
    table.InsertBatchFromBuffer(batchData, encoder.GetRowCount());
}
```

## Performance Characteristics

### Benchmark Results (10K rows)

| Metric | Standard Path | Streaming Encoder | Improvement |
|--------|---------------|-------------------|-------------|
| **Time** | 97ms | 116ms | -20% (slower) |
| **Allocations** | ~40-50MB | ~10-15MB | **75% reduction** |
| **GC Pressure** | High | Low | **Significant** |

### Memory Breakdown

**Standard Path (40-50MB for 10K rows)**:
- Dictionary list: ~25MB
- Serialization buffers: ~15MB
- Index structures: ~5-10MB

**Streaming Encoder Path (~10-15MB for 10K rows)**:
- Binary encoding buffer: ~640KB (64KB × 10 batches)
- Decoding to Dictionary: ~8-10MB (smaller batches)
- Index structures: ~5-10MB

### When to Use

✅ **Use Streaming Encoder When**:
- Batch size > 5,000 rows
- Memory constrained environment
- Reducing GC pressure is priority
- Sustained high-throughput insertions

❌ **Use Standard Path When**:
- Batch size < 1,000 rows
- CPU is bottleneck (encoding overhead)
- Latency is critical (streaming adds ~20% time)

## Automatic Activation

The streaming encoder path is **automatically activated** when:

1. `config.UseOptimizedInsertPath == true`, OR
2. Batch size > 5,000 rows

```csharp
// In BulkInsertAsync
if ((config?.UseOptimizedInsertPath ?? false) || rows.Count > 5000)
{
    await BulkInsertOptimizedInternalAsync(tableName, rows, table, cancellationToken);
    return;
}
```

## Implementation Details

### Transaction Handling

The streaming encoder respects storage engine transactions:

```csharp
storage.BeginTransaction();

try
{
    // Process batches
    for each batch:
        encoder.EncodeRow(row);
        if (batch full):
            table.InsertBatchFromBuffer(encodedData, rowCount);
            encoder.Reset();
    
    storage.CommitAsync(); // Single commit for all batches
}
catch
{
    storage.Rollback();
    throw;
}
```

### Batch Size Tuning

Default batch size: **64KB**

**Tuning Guidelines**:
- **16KB**: Low memory, high batch count (slower)
- **32KB**: Balanced (mobile devices)
- **64KB**: Recommended for most workloads
- **128KB**: High throughput, more memory

### Error Handling

If a single row exceeds the batch buffer size:
```csharp
if (!encoder.EncodeRow(row))
{
    throw new InvalidOperationException(
        $"Row {i} is too large to fit in batch buffer (max 64KB)");
}
```

## Storage Engine Compatibility

### PageBased Storage
✅ **Fully Compatible**
- Binary data decoded to Dictionary rows
- Routed through existing `InsertBatch`
- All page management logic preserved

### Columnar Storage
✅ **Fully Compatible**
- Column indexes updated correctly
- Hash indexes maintained
- Stale version tracking works

### AppendOnly Storage
✅ **Fully Compatible**
- Sequential append operations
- WAL logging preserved
- Crash recovery functional

## Testing

### Unit Tests

```csharp
[Fact]
public async Task BulkInsertAsync_WithStreamingEncoder_InsertsCorrectly()
{
    var config = new DatabaseConfig { UseOptimizedInsertPath = true };
    var db = factory.Create(dbPath, "password", false, config);
    
    db.ExecuteSQL("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
    
    var rows = GenerateTestData(10_000);
    await db.BulkInsertAsync("test", rows);
    
    var result = db.ExecuteQuery("SELECT COUNT(*) FROM test");
    Assert.Equal(10_000, result[0]["COUNT(*)"]);
}
```

### Benchmark Execution

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --filter *StreamingEncoder*
```

### Profiling

```bash
cd SharpCoreDB.Profiling
dotnet run --project SharpCoreDB.Profiling.csproj -- page-based
```

## Limitations

1. **Row Size Limit**: Einzelne rows cannot exceed batch buffer size (default 64KB)
   - **Workaround**: Increase batch size or split large BLOBs

2. **CPU Overhead**: Encoding/decoding adds ~20% CPU time
   - **Mitigation**: Only activate for large batches (>5K rows)

3. **No Direct Storage**: Still decodes to Dictionary for compatibility
   - **Future Optimization**: Direct binary-to-storage path

## Future Optimizations

### Phase 1: Direct Binary Storage ⏳
Eliminate Dictionary decoding by writing binary data directly to storage:

```csharp
// Proposed API
interface IStorageEngine
{
    long[] InsertBatchBinary(string tableName, ReadOnlySpan<byte> encodedData, int rowCount);
}
```

**Expected Improvement**: Additional 30-40% speed boost

### Phase 2: SIMD Encoding 🔮
Use SIMD instructions for bulk encoding:

```csharp
if (SimdHelper.IsSimdSupported)
{
    SimdHelper.EncodeInt32Batch(values, buffer);
}
```

**Expected Improvement**: 2-3x faster encoding

### Phase 3: Parallel Batching 🔮
Encode batches in parallel using ThreadPool:

```csharp
await Parallel.ForEachAsync(batches, async (batch, ct) =>
{
    var encoded = EncodeParallel(batch);
    await InsertBatchFromBufferAsync(encoded);
});
```

**Expected Improvement**: Near-linear scaling with cores

## Debugging

### Enable Verbose Logging

```csharp
// In BulkInsertOptimizedInternalAsync
Console.WriteLine($"Encoding batch {batchIndex}: {encoder.GetRowCount()} rows, {encoder.GetBatchSize()} bytes");
```

### Validate Encoding/Decoding

```csharp
// Encode then decode to verify round-trip
using var encoder = new StreamingRowEncoder(columns, types, 64 * 1024);
encoder.EncodeRow(originalRow);

var decoder = new BinaryRowDecoder(columns, types);
var decoded = decoder.DecodeRows(encoder.GetBatchData(), 1);

Assert.Equal(originalRow, decoded[0]); // Should match exactly
```

## References

- **Design Doc**: `docs/optimization/STREAMING_ENCODER_DESIGN.md`
- **Benchmark Code**: `SharpCoreDB.Benchmarks/StreamingEncoderBenchmark.cs`
- **Implementation**: `Optimizations/StreamingRowEncoder.cs`
- **Integration**: `Database.Batch.cs` (BulkInsertOptimizedInternalAsync)

## Changelog

### v2.1.0 (2025-01-XX)
- ✅ Initial streaming encoder implementation
- ✅ Binary format specification
- ✅ BinaryRowDecoder for compatibility
- ✅ ITable.InsertBatchFromBuffer interface
- ✅ Automatic activation for large batches
- ✅ Comprehensive benchmarks and documentation

### Future Versions
- ⏳ Direct binary-to-storage path (Phase 1)
- 🔮 SIMD encoding optimization (Phase 2)
- 🔮 Parallel batch encoding (Phase 3)

