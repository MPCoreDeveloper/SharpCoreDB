using System.Security.Cryptography;
using System.Text;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of ICryptoService using PBKDF2 for key derivation and AES-256-GCM for encryption.
/// </summary>
public class CryptoService : ICryptoService
{
    /// <inheritdoc />
    public byte[] DeriveKey(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    /// <inheritdoc />
    public byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipher = new byte[data.Length];
        aes.Encrypt(nonce, data, cipher, tag);
        return nonce.Concat(cipher).Concat(tag).ToArray();
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] key, byte[] encryptedData)
    {
        var nonce = encryptedData.Take(AesGcm.NonceByteSizes.MaxSize).ToArray();
        var tag = encryptedData.TakeLast(AesGcm.TagByteSizes.MaxSize).ToArray();
        var cipher = encryptedData.Skip(AesGcm.NonceByteSizes.MaxSize).Take(encryptedData.Length - AesGcm.NonceByteSizes.MaxSize - AesGcm.TagByteSizes.MaxSize).ToArray();
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var plain = new byte[cipher.Length];
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
