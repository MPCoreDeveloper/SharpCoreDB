namespace SharpCoreDB.Tests.Security;

using System.Security.Cryptography;
using SharpCoreDB.Core.File;
using SharpCoreDB.Services;

public sealed class PageEncryptionAadTests
{
    [Fact]
    public void EncryptDecrypt_WithMatchingPageId_ShouldRoundTrip()
    {
        var crypto = new CryptoService();
        var key = RandomNumberGenerator.GetBytes(32);
        using var encryption = new PageEncryption(crypto, key, pageSize: 4096);

        var plaintext = RandomNumberGenerator.GetBytes(512);

        var encrypted = encryption.EncryptPage(plaintext, pageId: 42);
        var decrypted = encryption.DecryptPage(encrypted, pageId: 42);

        Assert.NotNull(decrypted);
        Assert.True(plaintext.AsSpan().SequenceEqual(decrypted));
    }

    [Fact]
    public void Decrypt_WithDifferentPageId_ShouldFailAuthentication()
    {
        var crypto = new CryptoService();
        var key = RandomNumberGenerator.GetBytes(32);
        using var encryption = new PageEncryption(crypto, key, pageSize: 4096);

        var plaintext = RandomNumberGenerator.GetBytes(256);

        var encrypted = encryption.EncryptPage(plaintext, pageId: 7);
        var decrypted = encryption.DecryptPage(encrypted, pageId: 8);

        Assert.Null(decrypted);
    }
}
