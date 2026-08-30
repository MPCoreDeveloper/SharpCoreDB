// src\SharpCoreDB\Storage\BlockCompressionMode.cs
namespace SharpCoreDB.Storage;

/// <summary>
/// Compression algorithm for SingleFile block data.
/// </summary>
public enum BlockCompressionMode
{
    /// <summary>No compression. Default for backward compatibility.</summary>
    None = 0,
    /// <summary>Brotli compression. Best ratio for text/JSON payloads.</summary>
    Brotli = 1,
    /// <summary>GZip compression. Faster decompression, slightly larger.</summary>
    GZip = 2
}