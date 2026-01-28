# SharpCoreDB C# 14 / .NET 10 Coding Standards

**Version:** 1.0  
**Target Framework:** .NET 10  
**Language Version:** C# 14  
**Effective Date:** 2025-01-28

> **Mandatory:** All new code and refactorings MUST use modern C# 14 features and patterns where applicable.

---

## 🎯 Core Principles

1. **Modern First**: Always prefer C# 14 features over legacy patterns
2. **Performance**: Zero-allocation where possible, use `Span<T>`, `Memory<T>`, and pooling
3. **Safety**: Leverage null-safety, exhaustive pattern matching, and compile-time checks
4. **Readability**: Use expressive syntax but avoid over-engineering
5. **Async All The Way**: Async methods everywhere, no sync-over-async

---

## 🆕 C# 14 Required Features

### 1. Primary Constructors (Classes & Structs)

**✅ DO:**
```csharp
// Primary constructor with validation
public sealed class BlockRegistry(
    SingleFileStorageProvider provider,
    ulong registryOffset,
    ulong registryLength) : IDisposable
{
    private readonly SingleFileStorageProvider _provider = provider 
        ?? throw new ArgumentNullException(nameof(provider));
    private readonly ulong _registryOffset = registryOffset;
    private readonly ulong _registryLength = registryLength > 0 
        ? registryLength 
        : throw new ArgumentOutOfRangeException(nameof(registryLength));
    
    // Use captured parameters directly
    public void Initialize() => _provider.RegisterComponent(this);
}

// Struct with primary constructor
public readonly record struct PageId(uint Value)
{
    public bool IsValid => Value > 0;
}
```

**❌ DON'T:**
```csharp
// Old-style constructor with assignments
public sealed class BlockRegistry : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _registryOffset;
    
    public BlockRegistry(
        SingleFileStorageProvider provider,
        ulong registryOffset)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _registryOffset = registryOffset;
    }
}
```

---

### 2. Field Keyword in Properties

**✅ DO:**
```csharp
// Auto-property with field access
public class Table
{
    public string Name { get; set; }
    
    public void Validate()
    {
        // C# 14: Access backing field directly
        if (string.IsNullOrEmpty(field))
        {
            field = "DefaultTable";
        }
    }
}

// Property with custom logic
public class StorageProvider
{
    private int _dirtyCount;
    
    public int DirtyCount
    {
        get => _dirtyCount;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _dirtyCount = value;
            
            // C# 14: Use 'field' in custom setter if needed
            OnDirtyCountChanged(field);
        }
    }
}
```

**❌ DON'T:**
```csharp
// Manual backing field when not needed
private string _name;
public string Name
{
    get => _name;
    set => _name = value ?? "Default";
}
```

---

### 3. Collection Expressions

**✅ DO:**
```csharp
// Array initialization
int[] numbers = [1, 2, 3, 4, 5];

// List initialization
List<string> tables = ["Users", "Orders", "Products"];

// Spread operator
int[] first = [1, 2, 3];
int[] second = [4, 5, 6];
int[] combined = [.. first, .. second];

// Method parameters
ProcessBatch([op1, op2, op3]);

// Empty collections
List<int> empty = [];
```

**❌ DON'T:**
```csharp
// Old-style initialization
int[] numbers = new[] { 1, 2, 3, 4, 5 };
List<string> tables = new List<string> { "Users", "Orders" };
List<int> empty = new List<int>();
```

---

### 4. Inline Arrays (Zero-Allocation)

**✅ DO:**
```csharp
// Fixed-size buffer without heap allocation
[InlineArray(32)]
public struct ChecksumBuffer
{
    private byte _element0;
}

// Usage
public void ComputeChecksum(ReadOnlySpan<byte> data)
{
    ChecksumBuffer buffer = default;
    Span<byte> checksumSpan = buffer; // Implicit conversion
    
    if (SHA256.TryHashData(data, checksumSpan, out var written))
    {
        // Use checksum
    }
}

// Small fixed arrays
[InlineArray(16)]
public struct GuidBuffer
{
    private byte _element0;
}
```

**❌ DON'T:**
```csharp
// Heap allocation for fixed-size data
public void ComputeChecksum(ReadOnlySpan<byte> data)
{
    var buffer = new byte[32]; // ❌ Allocates on heap
    SHA256.TryHashData(data, buffer, out _);
}
```

---

### 5. Lock Keyword (Modern Thread Safety)

**✅ DO:**
```csharp
public class ConcurrentCache
{
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<string, Entry> _cache = [];
    
    public void AddOrUpdate(string key, Entry value)
    {
        lock (_cacheLock)
        {
            _cache[key] = value;
        }
    }
    
    public bool TryGet(string key, out Entry value)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(key, out value);
        }
    }
}
```

**❌ DON'T:**
```csharp
// Old object-based locking
private readonly object _lock = new object();

public void AddOrUpdate(string key, Entry value)
{
    lock (_lock) // ❌ Use Lock class instead
    {
        _cache[key] = value;
    }
}
```

---

### 6. Params Collections (Flexible Parameters)

**✅ DO:**
```csharp
// C# 14: params with any collection type
public void ExecuteBatch(params IEnumerable<string> statements)
{
    foreach (var sql in statements)
    {
        Execute(sql);
    }
}

// Usage
ExecuteBatch("INSERT ...", "UPDATE ...");
ExecuteBatch(statementList);
ExecuteBatch(statementArray);

// Params with Span
public int Sum(params ReadOnlySpan<int> values)
{
    var total = 0;
    foreach (var value in values)
    {
        total += value;
    }
    return total;
}
```

**❌ DON'T:**
```csharp
// Limited to arrays only
public void ExecuteBatch(params string[] statements) { }
```

---

### 7. Extension Types (Future-Ready)

**✅ DO:**
```csharp
// C# 14: Explicit extension declaration
public implicit extension TableExtensions for Table
{
    public void FlushOptimized()
    {
        // Access instance members directly
        this.Flush();
        this.CompactIfNeeded();
    }
}

// Usage
var table = new Table("Users");
table.FlushOptimized(); // Looks like instance method
```

**❌ DON'T:**
```csharp
// Old extension method style (still works but less clear)
public static class TableExtensions
{
    public static void FlushOptimized(this Table table)
    {
        table.Flush();
    }
}
```

---

## 🚀 Performance Patterns

### 1. Span<T> and Memory<T>

**✅ DO:**
```csharp
// Zero-copy slicing
public ReadOnlySpan<byte> GetBlockData(int offset, int length)
{
    return _fileData.AsSpan(offset, length);
}

// Avoid allocations in loops
public void ProcessRecords(ReadOnlySpan<byte> data)
{
    var offset = 0;
    while (offset < data.Length)
    {
        var recordLength = BitConverter.ToInt32(data[offset..]);
        var record = data.Slice(offset + 4, recordLength);
        ProcessRecord(record);
        offset += 4 + recordLength;
    }
}

// Span-based parsing
public bool TryParse(ReadOnlySpan<char> input, out int value)
{
    return int.TryParse(input, out value);
}
```

**❌ DON'T:**
```csharp
// Unnecessary allocations
public byte[] GetBlockData(int offset, int length)
{
    var result = new byte[length]; // ❌ Always allocates
    Array.Copy(_fileData, offset, result, 0, length);
    return result;
}
```

---

### 2. ArrayPool<T>

**✅ DO:**
```csharp
public async Task WriteDataAsync(ReadOnlyMemory<byte> data)
{
    var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
    try
    {
        data.CopyTo(buffer);
        await WriteToFileAsync(buffer.AsMemory(0, data.Length));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

**❌ DON'T:**
```csharp
public async Task WriteDataAsync(byte[] data)
{
    var buffer = new byte[data.Length]; // ❌ Allocates every time
    Array.Copy(data, buffer, data.Length);
    await WriteToFileAsync(buffer);
}
```

---

### 3. Async All The Way

**✅ DO:**
```csharp
// Pure async methods
public async Task<List<Record>> GetRecordsAsync(CancellationToken ct = default)
{
    var records = new List<Record>();
    
    await foreach (var record in EnumerateRecordsAsync(ct))
    {
        records.Add(record);
    }
    
    return records;
}

// Async enumerable
public async IAsyncEnumerable<Record> EnumerateRecordsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var offset = 0L;
    while (offset < _fileLength)
    {
        ct.ThrowIfCancellationRequested();
        
        var record = await ReadRecordAsync(offset, ct);
        yield return record;
        
        offset += record.Length;
    }
}

// ConfigureAwait in library code
public async Task FlushAsync(CancellationToken ct = default)
{
    await _stream.FlushAsync(ct).ConfigureAwait(false);
    await _registry.FlushAsync(ct).ConfigureAwait(false);
}
```

**❌ DON'T:**
```csharp
// Sync over async
public List<Record> GetRecords()
{
    return GetRecordsAsync().Result; // ❌ Deadlock risk
}

// Blocking in async
public async Task FlushAsync()
{
    _stream.Flush(); // ❌ Use FlushAsync
    await _registry.FlushAsync();
}
```

---

### 4. Channel<T> for Async Coordination

**✅ DO:**
```csharp
public class WriteQueue
{
    private readonly Channel<WriteOperation> _channel = Channel.CreateBounded<WriteOperation>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    
    public async Task EnqueueAsync(WriteOperation op, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(op, ct);
    }
    
    public async Task ProcessQueueAsync(CancellationToken ct = default)
    {
        await foreach (var op in _channel.Reader.ReadAllAsync(ct))
        {
            await ProcessOperationAsync(op, ct);
        }
    }
}
```

---

### 5. PeriodicTimer for Background Tasks

**✅ DO:**
```csharp
public class BackgroundFlusher : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(100));
    private readonly Task _flushTask;
    private readonly CancellationTokenSource _cts = new();
    
    public BackgroundFlusher()
    {
        _flushTask = Task.Run(FlushLoopAsync, _cts.Token);
    }
    
    private async Task FlushLoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                await FlushAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _flushTask.GetAwaiter().GetResult();
        _cts.Dispose();
    }
}
```

**❌ DON'T:**
```csharp
// Old Timer-based approach
private readonly System.Timers.Timer _timer;

public BackgroundFlusher()
{
    _timer = new System.Timers.Timer(100);
    _timer.Elapsed += (s, e) => Flush(); // ❌ Sync callback
    _timer.Start();
}
```

---

## 🛡️ Safety Patterns

### 1. Nullable Reference Types

**✅ DO:**
```csharp
#nullable enable

public class DatabaseOptions
{
    public required string ConnectionString { get; init; }
    public string? DatabaseName { get; init; }
    public int PageSize { get; init; } = 4096;
    
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ConnectionString);
        
        if (DatabaseName is { Length: 0 })
        {
            throw new ArgumentException("Database name cannot be empty");
        }
    }
}
```

---

### 2. Required Members

**✅ DO:**
```csharp
public class DatabaseConfig
{
    public required string RootPath { get; init; }
    public required StorageMode Mode { get; init; }
    public int PageSize { get; init; } = 4096;
}

// Usage
var config = new DatabaseConfig
{
    RootPath = "C:\\data",
    Mode = StorageMode.SingleFile
}; // ✅ Compiler enforces required properties
```

**❌ DON'T:**
```csharp
public class DatabaseConfig
{
    public string RootPath { get; init; } = null!; // ❌ Use required instead
}
```

---

### 3. Pattern Matching

**✅ DO:**
```csharp
// Exhaustive switch expression
public string GetStorageModeName(StorageMode mode) => mode switch
{
    StorageMode.Directory => "Directory-based",
    StorageMode.SingleFile => "Single-file",
    StorageMode.Columnar => "Columnar",
    _ => throw new ArgumentOutOfRangeException(nameof(mode))
};

// Property patterns
public bool IsValidConfig(DatabaseConfig config) => config switch
{
    { RootPath: null or "" } => false,
    { PageSize: < 1024 or > 65536 } => false,
    { Mode: StorageMode.SingleFile, RootPath: var path } when !path.EndsWith(".scdb") => false,
    _ => true
};

// List patterns
public bool HasValidSequence(int[] values) => values switch
{
    [1, 2, 3, ..] => true,
    [var first, .., var last] when first < last => true,
    _ => false
};
```

---

## 📝 Naming Conventions

### Files and Types

```csharp
// ✅ File names match primary type
// File: BlockRegistry.cs
public sealed class BlockRegistry { }

// File: IStorageProvider.cs
public interface IStorageProvider { }

// File: StorageMode.cs
public enum StorageMode { }

// Partial classes
// File: Database.cs (main)
public partial class Database { }

// File: Database.Transactions.cs
public partial class Database { }
```

### Methods and Properties

```csharp
// ✅ Async suffix for async methods
public async Task FlushAsync(CancellationToken ct = default) { }

// ✅ Try pattern for operations that can fail
public bool TryGetBlock(string name, out BlockEntry entry) { }

// ✅ Get/Set prefix for actions
public BlockEntry GetBlock(string name) { }
public void SetBlockDirty(string name) { }

// ✅ Is/Has prefix for boolean properties
public bool IsDirty { get; }
public bool HasPendingWrites { get; }
```

---

## 🧪 Testing Standards

### Test Naming

```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public void WriteBlockAsync_WithValidData_ShouldSucceed() { }

[Fact]
public void WriteBlockAsync_WhenBufferFull_ShouldWaitForSpace() { }

[Theory]
[InlineData(1024)]
[InlineData(4096)]
[InlineData(65536)]
public void AllocatePages_WithVariousSizes_ShouldReturnValidOffset(int size) { }
```

### Test Structure (AAA Pattern)

```csharp
[Fact]
public async Task FlushAsync_WithDirtyBlocks_ShouldPersistToDisk()
{
    // Arrange
    var tempFile = Path.GetTempFileName() + ".scdb";
    var options = new DatabaseOptions { StorageMode = StorageMode.SingleFile };
    await using var provider = SingleFileStorageProvider.Open(tempFile, options);
    
    var testData = new byte[1024];
    Random.Shared.NextBytes(testData);
    
    // Act
    await provider.WriteBlockAsync("test_block", testData);
    await provider.FlushPendingWritesAsync();
    
    // Assert
    Assert.True(provider.BlockExists("test_block"));
    
    var readData = await provider.ReadBlockAsync("test_block");
    Assert.Equal(testData, readData);
}
```

---

## 📚 Documentation Standards

### XML Documentation

```csharp
/// <summary>
/// Writes a block of data to the storage provider with optional batching.
/// C# 14: Uses modern async patterns with cancellation support.
/// </summary>
/// <param name="blockName">Unique identifier for the block.</param>
/// <param name="data">Data to write. Must not be empty.</param>
/// <param name="cancellationToken">Cancellation token for the operation.</param>
/// <returns>Task that completes when the write is queued (not necessarily flushed).</returns>
/// <exception cref="ArgumentNullException">Thrown if <paramref name="blockName"/> is null.</exception>
/// <exception cref="ObjectDisposedException">Thrown if provider is disposed.</exception>
/// <remarks>
/// This method queues writes to a background channel for batched processing.
/// Call <see cref="FlushPendingWritesAsync"/> to ensure data is persisted to disk.
/// </remarks>
public async Task WriteBlockAsync(
    string blockName, 
    ReadOnlyMemory<byte> data, 
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### Code Comments

```csharp
// ✅ Explain WHY, not WHAT
// Batch writes to reduce disk I/O and improve throughput
await WriteBatchAsync(operations);

// ✅ Document non-obvious behavior
// SHA256 checksum computed on input data (not read-back from disk)
// to avoid extra I/O overhead
var checksum = SHA256.HashData(data.Span);

// ✅ Mark TODO items
// TODO: Implement compression for blocks > 1MB

// ✅ Mark performance-critical sections
// PERF: Hot path - avoid allocations
Span<byte> buffer = stackalloc byte[256];
```

---

## 🚫 Anti-Patterns to Avoid

### 1. Sync Over Async

```csharp
// ❌ DON'T
public void SaveData()
{
    SaveDataAsync().Wait(); // Deadlock risk
    
    var result = GetDataAsync().Result; // Deadlock risk
}

// ✅ DO
public async Task SaveDataAsync()
{
    await SaveDataInternalAsync();
}
```

### 2. Async Void

```csharp
// ❌ DON'T
public async void ProcessData() // Can't catch exceptions
{
    await DoWorkAsync();
}

// ✅ DO
public async Task ProcessDataAsync()
{
    await DoWorkAsync();
}
```

### 3. Premature Optimization

```csharp
// ❌ DON'T optimize without profiling
public void ProcessRecords()
{
    // Complex unsafe pointer arithmetic without proven need
    unsafe { /* ... */ }
}

// ✅ DO: Start simple, measure, then optimize
public void ProcessRecords()
{
    foreach (var record in _records)
    {
        ProcessRecord(record);
    }
}
```

### 4. Ignoring Cancellation

```csharp
// ❌ DON'T ignore cancellation tokens
public async Task LongRunningOperationAsync(CancellationToken ct)
{
    for (int i = 0; i < 1000000; i++)
    {
        await DoWorkAsync(); // No cancellation check!
    }
}

// ✅ DO check cancellation regularly
public async Task LongRunningOperationAsync(CancellationToken ct)
{
    for (int i = 0; i < 1000000; i++)
    {
        ct.ThrowIfCancellationRequested();
        await DoWorkAsync(ct);
    }
}
```

---

## ✅ Code Review Checklist

Before submitting a PR, verify:

- [ ] All code uses C# 14 features where applicable
- [ ] No `object` locks (use `Lock` class)
- [ ] No collection initializers (use collection expressions `[]`)
- [ ] Async methods have `Async` suffix
- [ ] All async methods accept `CancellationToken`
- [ ] No sync-over-async patterns
- [ ] Span<T> used for hot paths
- [ ] ArrayPool<T> used for temporary buffers
- [ ] Nullable reference types enabled and warnings clean
- [ ] Pattern matching used instead of if/else chains
- [ ] XML documentation on public APIs
- [ ] Unit tests follow AAA pattern
- [ ] Performance impact measured (if applicable)

---

## 📖 References

### Official Documentation
- [C# 14 What's New](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- [.NET 10 Performance Improvements](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)
- [Span<T> and Memory<T>](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)
- [Channel<T> Guide](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)

### Training Resources
- Microsoft Learn: "Modern C# Development"
- Pluralsight: "C# 14 New Features"
- YouTube: ".NET Performance Tips"

---

**Compliance:** All team members must follow these standards for new code as of 2025-01-28.  
**Review Cycle:** Quarterly review and updates as C# evolves.  
**Questions:** Raise in team standup or create an issue.

---

**Version History:**
- v1.0 (2025-01-28): Initial release for C# 14 / .NET 10
