using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace SharpCoreDB.Core.File;

/// <summary>
/// Handles file I/O operations using memory-mapped files for improved performance.
/// Falls back to FileStream for small files (less than 50 MB) or when memory mapping is not supported.
/// </summary>
/// <param name="filePath">The path to the file to be memory-mapped.</param>
/// <param name="useMemoryMapping">Whether to use memory mapping. If false, uses FileStream.</param>
public sealed class MemoryMappedFileHandler(string filePath, bool useMemoryMapping = true) : IDisposable
{
    private readonly string _filePath = filePath;
    private readonly bool _useMemoryMapping = useMemoryMapping;
    private MemoryMappedFile? _memoryMappedFile;
    private FileStream? _fileStream;
    private bool _disposed;

    /// <summary>
    /// Minimum file size threshold for using memory mapping (50 MB).
    /// Files smaller than this will use FileStream for better performance.
    /// </summary>
    public const long MinFileSizeForMemoryMapping = 50L * 1024 * 1024; // 50 MB

    /// <summary>
    /// Gets whether the file should use memory mapping based on size and configuration.
    /// </summary>
    private bool ShouldUseMemoryMapping
    {
        get
        {
            if (!_useMemoryMapping)
                return false;

            if (!System.IO.File.Exists(_filePath))
                return false;

            try
            {
                var fileInfo = new FileInfo(_filePath);
                return fileInfo.Length >= MinFileSizeForMemoryMapping;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Reads the entire file contents as a byte array.
    /// Uses memory-mapped files for large files, FileStream for small files.
    /// </summary>
    /// <returns>The file contents as a byte array, or null if the file doesn't exist.</returns>
    public byte[]? ReadAllBytes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!System.IO.File.Exists(_filePath))
            return null;

        try
        {
            if (ShouldUseMemoryMapping)
            {
                return ReadAllBytesMemoryMapped();
            }
            else
            {
                return System.IO.File.ReadAllBytes(_filePath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // If memory mapping fails, fall back to FileStream
            return System.IO.File.ReadAllBytes(_filePath);
        }
    }

    /// <summary>
    /// Reads a specific range of bytes from the file using memory-mapped access.
    /// </summary>
    /// <param name="offset">The offset in the file to start reading from.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The requested bytes, or null if the file doesn't exist or parameters are invalid.</returns>
    public unsafe byte[]? ReadBytes(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!System.IO.File.Exists(_filePath) || offset < 0 || length <= 0)
            return null;

        var fileInfo = new FileInfo(_filePath);
        
        // Check for potential overflow and validate range
        if (offset > fileInfo.Length || length > fileInfo.Length || offset + length > fileInfo.Length)
            return null;

        try
        {
            if (ShouldUseMemoryMapping)
            {
                return ReadBytesMemoryMapped(offset, length);
            }
            else
            {
                return ReadBytesFileStream(offset, length);
            }
        }
        catch
        {
            // Fall back to FileStream on error
            return ReadBytesFileStream(offset, length);
        }
    }

    /// <summary>
    /// Reads bytes from a specific offset using memory-mapped file access with unsafe operations.
    /// </summary>
    private unsafe byte[]? ReadBytesMemoryMapped(long offset, int length)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(_filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
        
        var buffer = new byte[length];
        byte* pointer = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        
        try
        {
            var span = new Span<byte>(pointer, length);
            span.CopyTo(buffer);
            return buffer;
        }
        finally
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    /// <summary>
    /// Reads bytes from a specific offset using traditional FileStream.
    /// </summary>
    private byte[]? ReadBytesFileStream(long offset, int length)
    {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[length];
        var bytesRead = fs.Read(buffer, 0, length);
        
        if (bytesRead < length)
        {
            Array.Resize(ref buffer, bytesRead);
        }
        
        return buffer;
    }

    /// <summary>
    /// Reads all bytes from the file using memory-mapped access with unsafe operations.
    /// </summary>
    private unsafe byte[]? ReadAllBytesMemoryMapped()
    {
        var fileInfo = new FileInfo(_filePath);
        var length = fileInfo.Length;

        if (length == 0)
            return Array.Empty<byte>();

        // Check for files larger than int.MaxValue (2GB)
        if (length > int.MaxValue)
        {
            throw new NotSupportedException($"Memory-mapped files larger than 2GB are not supported. File size: {length:N0} bytes");
        }

        using var mmf = MemoryMappedFile.CreateFromFile(_filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
        
        var buffer = new byte[length];
        byte* pointer = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        
        try
        {
            var span = new Span<byte>(pointer, (int)length);
            span.CopyTo(buffer);
            return buffer;
        }
        finally
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    /// <summary>
    /// Writes data to the file. Memory mapping is not used for writes to maintain data integrity.
    /// </summary>
    /// <param name="data">The data to write to the file.</param>
    public void WriteAllBytes(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);

        // Always use FileStream for writes to ensure data integrity
        System.IO.File.WriteAllBytes(_filePath, data);
    }

    /// <summary>
    /// Checks if the file exists.
    /// </summary>
    /// <returns>True if the file exists, false otherwise.</returns>
    public bool Exists() => System.IO.File.Exists(_filePath);

    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    /// <returns>The file size in bytes, or 0 if the file doesn't exist.</returns>
    public long GetFileSize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!System.IO.File.Exists(_filePath))
            return 0;

        var fileInfo = new FileInfo(_filePath);
        return fileInfo.Length;
    }

    /// <summary>
    /// Creates a persistent memory-mapped file for the specified file path.
    /// This keeps the mapping open for the lifetime of this handler instance.
    /// </summary>
    /// <remarks>
    /// Call this method when you need persistent access to a file. The mapping will remain
    /// until the handler is disposed. This is useful for database files that are accessed frequently.
    /// </remarks>
    public void CreatePersistentMapping()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ShouldUseMemoryMapping || !System.IO.File.Exists(_filePath))
            return;

        try
        {
            // Dispose existing mapping if any
            _memoryMappedFile?.Dispose();

            // Create new persistent mapping
            _memoryMappedFile = MemoryMappedFile.CreateFromFile(
                _filePath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read);
        }
        catch
        {
            // If memory mapping fails, fall back to FileStream mode
            _memoryMappedFile = null;
        }
    }

    /// <summary>
    /// Reads bytes using the persistent memory mapping if available.
    /// Falls back to standard read methods if no persistent mapping exists.
    /// </summary>
    /// <param name="offset">The offset to start reading from.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The requested bytes.</returns>
    public unsafe byte[]? ReadBytesFromPersistentMapping(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_memoryMappedFile == null)
            return ReadBytes(offset, length);

        try
        {
            using var accessor = _memoryMappedFile.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
            var buffer = new byte[length];
            byte* pointer = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            
            try
            {
                var span = new Span<byte>(pointer, length);
                span.CopyTo(buffer);
                return buffer;
            }
            finally
            {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch
        {
            // Fall back to standard read on error
            return ReadBytes(offset, length);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _memoryMappedFile?.Dispose();
        _fileStream?.Dispose();
        
        _disposed = true;
    }
}
