using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Core.File;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of IStorage using encrypted files and memory-mapped files for performance.
/// </summary>
public class Storage(ICryptoService crypto, byte[] key, DatabaseConfig? config = null) : IStorage
{
    private readonly ICryptoService _crypto = crypto;
    private readonly byte[] _key = key;
    private readonly bool _noEncryption = config?.NoEncryptMode ?? false;
    private readonly DatabaseConfig _config = config ?? DatabaseConfig.Default;

    /// <inheritdoc />
    public void Write(string path, string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
        if (_noEncryption)
        {
            File.WriteAllBytes(path, plain);
        }
        else
        {
            var encrypted = _crypto.Encrypt(_key, plain);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public string? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var data = File.ReadAllBytes(path);
        try
        {
            if (_noEncryption)
            {
                return Encoding.UTF8.GetString(data);
            }
            else
            {
                var plain = _crypto.Decrypt(_key, data);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? ReadMemoryMapped(string path)
    {
        if (!File.Exists(path)) return null;
        
        // Use the new MemoryMappedFileHandler for improved performance
        using var handler = new MemoryMappedFileHandler(path, true);
        var buffer = handler.ReadAllBytes();
        
        if (buffer == null)
            return null;

        if (_noEncryption)
        {
            return Encoding.UTF8.GetString(buffer);
        }
        else
        {
            var plain = _crypto.Decrypt(_key, buffer);
            return Encoding.UTF8.GetString(plain);
        }
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        byte[] toWrite;
        if (data.Length > 1024)
        {
            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, CompressionMode.Compress))
            {
                brotli.Write(data, 0, data.Length);
            }
            var compressed = ms.ToArray();
            toWrite = new byte[compressed.Length + 1];
            toWrite[0] = 1; // compressed
            Array.Copy(compressed, 0, toWrite, 1, compressed.Length);
        }
        else
        {
            toWrite = new byte[data.Length + 1];
            toWrite[0] = 0; // uncompressed
            Array.Copy(data, 0, toWrite, 1, data.Length);
        }
        
        if (_noEncryption)
        {
            File.WriteAllBytes(path, toWrite);
        }
        else
        {
            var encrypted = _crypto.Encrypt(_key, toWrite);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path)) return null;

        // Determine if we should use memory-mapped files based on config and file size
        byte[] fileData;
        if (_config.UseMemoryMapping && ShouldUseMemoryMapping(path))
        {
            using var handler = new MemoryMappedFileHandler(path, true);
            fileData = handler.ReadAllBytes() ?? [];
        }
        else
        {
            fileData = File.ReadAllBytes(path);
        }
        
        byte[] decrypted;
        if (_noEncryption)
        {
            decrypted = fileData;
        }
        else
        {
            decrypted = _crypto.Decrypt(_key, fileData);
        }
        
        if (decrypted.Length == 0) return [];
        var isCompressed = decrypted[0] == 1;
        var data = new byte[decrypted.Length - 1];
        Array.Copy(decrypted, 1, data, 0, data.Length);
        if (isCompressed)
        {
            using var ms = new MemoryStream(data);
            using var brotli = new BrotliStream(ms, CompressionMode.Decompress);
            using var resultMs = new MemoryStream();
            brotli.CopyTo(resultMs);
            return resultMs.ToArray();
        }
        else
        {
            return data;
        }
    }

    /// <summary>
    /// Determines if memory mapping should be used for the given file based on configuration and file size.
    /// </summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if memory mapping should be used, false otherwise.</returns>
    private bool ShouldUseMemoryMapping(string path)
    {
        if (!_config.UseMemoryMapping)
            return false;

        try
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.Length >= _config.MemoryMappingThreshold;
        }
        catch
        {
            return false;
        }
    }
}
