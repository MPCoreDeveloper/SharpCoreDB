// <copyright file="Storage.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using SharpCoreDB.Core.File;
using SharpCoreDB.Interfaces;

/// <summary>
/// Implementation of IStorage using encrypted files and memory-mapped files for performance.
/// </summary>
public class Storage(ICryptoService crypto, byte[] key, DatabaseConfig? config = null) : IStorage
{
    private readonly ICryptoService crypto = crypto;
    private readonly byte[] key = key;
    private readonly bool noEncryption = config?.NoEncryptMode ?? false;
    private readonly bool useMemoryMapping = config?.UseMemoryMapping ?? true;

    /// <inheritdoc />
    public void Write(string path, string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, plain);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, plain);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var data = File.ReadAllBytes(path);
        try
        {
            if (this.noEncryption)
            {
                return Encoding.UTF8.GetString(data);
            }
            else
            {
                var plain = this.crypto.Decrypt(this.key, data);
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
        if (!File.Exists(path))
        {
            return null;
        }

        using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        using var accessor = mmf.CreateViewAccessor();
        var length = accessor.Capacity;
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, (int)length);
        if (this.noEncryption)
        {
            return Encoding.UTF8.GetString(buffer);
        }
        else
        {
            var plain = this.crypto.Decrypt(this.key, buffer);
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

        if (this.noEncryption)
        {
            File.WriteAllBytes(path, toWrite);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, toWrite);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Use memory-mapped file handler for improved performance on large files
        byte[] fileData;
        if (this.useMemoryMapping)
        {
            using var handler = MemoryMappedFileHandler.TryCreate(path, this.useMemoryMapping);
            if (handler != null && handler.IsMemoryMapped)
            {
                fileData = handler.ReadAllBytes();
            }
            else
            {
                // Fallback to traditional file reading
                fileData = File.ReadAllBytes(path);
            }
        }
        else
        {
            fileData = File.ReadAllBytes(path);
        }

        byte[] decrypted;
        if (this.noEncryption)
        {
            decrypted = fileData;
        }
        else
        {
            decrypted = this.crypto.Decrypt(this.key, fileData);
        }

        if (decrypted.Length == 0)
        {
            return [];
        }

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
