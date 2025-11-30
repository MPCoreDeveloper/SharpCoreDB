using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of IStorage using encrypted files and memory-mapped files for performance.
/// </summary>
public class Storage(ICryptoService crypto, byte[] key) : IStorage
{
    private readonly ICryptoService _crypto = crypto;
    private readonly byte[] _key = key;

    /// <inheritdoc />
    public void Write(string path, string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
        var encrypted = _crypto.Encrypt(_key, plain);
        File.WriteAllBytes(path, encrypted);
    }

    /// <inheritdoc />
    public string? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var encrypted = File.ReadAllBytes(path);
        try
        {
            var plain = _crypto.Decrypt(_key, encrypted);
            return Encoding.UTF8.GetString(plain);
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
        using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        using var accessor = mmf.CreateViewAccessor();
        var length = accessor.Capacity;
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, (int)length);
        var plain = _crypto.Decrypt(_key, buffer);
        return Encoding.UTF8.GetString(plain);
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        byte[] toEncrypt;
        if (data.Length > 1024)
        {
            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, CompressionMode.Compress))
            {
                brotli.Write(data, 0, data.Length);
            }
            var compressed = ms.ToArray();
            toEncrypt = new byte[compressed.Length + 1];
            toEncrypt[0] = 1; // compressed
            Array.Copy(compressed, 0, toEncrypt, 1, compressed.Length);
        }
        else
        {
            toEncrypt = new byte[data.Length + 1];
            toEncrypt[0] = 0; // uncompressed
            Array.Copy(data, 0, toEncrypt, 1, data.Length);
        }
        var encrypted = _crypto.Encrypt(_key, toEncrypt);
        File.WriteAllBytes(path, encrypted);
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path)) return null;
        var encrypted = File.ReadAllBytes(path);
        var decrypted = _crypto.Decrypt(_key, encrypted);
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
}
