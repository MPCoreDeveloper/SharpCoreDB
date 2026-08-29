// tests/SharpCoreDB.Tests/Security/SingleFileEncryptionTests.cs
// Regression tests for SingleFile (.scdb) encryption via DatabaseOptions.EncryptionKey.
// Validates the fix where SingleFileStorageProvider was not applying AES-256-GCM
// encryption to block reads/writes, resulting in plaintext data on disk.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests.Security;

public sealed class SingleFileEncryptionTests : IAsyncLifetime
{
    private string _testDir = null!;
    private ServiceProvider _serviceProvider = null!;
    private DatabaseFactory _factory = null!;

    private const string SecretData = "classified-payload-regression-test-7f3a9c";
    private const string DummyPassword = "unused-in-singlefile-mode";

    public ValueTask InitializeAsync()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SharpCoreDB_EncTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();

        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup on Windows where file handles may linger.
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] GenerateKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    private string DbPath(string name) => Path.Combine(_testDir, $"{name}.scdb");

    private DatabaseOptions EncryptedOptions(byte[] key) => new()
    {
        StorageMode = StorageMode.SingleFile,
        EnableEncryption = true,
        EncryptionKey = key,
        CreateImmediately = true
    };

    private static bool FileContainsBytes(string filePath, byte[] needle)
    {
        var haystack = File.ReadAllBytes(filePath);
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private async Task CreateEncryptedDbWithSecret(string path, byte[] key)
    {
        var db = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(key));
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL($"INSERT INTO Secrets VALUES (1, '{SecretData}')");
            db.ForceSave();
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    // ── 1. Plaintext-at-rest ─────────────────────────────────────────────

    /// <summary>
    /// The secret must NOT appear verbatim in the .scdb file when encryption is enabled.
    /// This is the primary regression test for the original bug where SingleFile mode
    /// wrote all block data as plaintext regardless of EnableEncryption.
    /// </summary>
    [Fact]
    public async Task EncryptedData_IsNotPlaintext_OnDisk()
    {
        var path = DbPath("atrest");
        var key = GenerateKey();

        await CreateEncryptedDbWithSecret(path, key);

        var secretBytes = Encoding.UTF8.GetBytes(SecretData);
        Assert.False(FileContainsBytes(path, secretBytes),
            "Secret data was found in plaintext on disk. Encryption is not being applied to SingleFile blocks.");
    }

    // ── 2. Correct-key roundtrip ─────────────────────────────────────────

    /// <summary>
    /// Data written with a key must be readable when the database is reopened
    /// with the same key.
    /// </summary>
    [Fact]
    public async Task CorrectKey_CanRoundtrip_Data()
    {
        var path = DbPath("roundtrip");
        var key = GenerateKey();

        await CreateEncryptedDbWithSecret(path, key);

        // Reopen with the same key
        var db = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(key));
        try
        {
            var rows = db.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal(SecretData, rows[0]["Data"]?.ToString());
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    // ── 3. Wrong-key data inaccessibility ────────────────────────────────

    /// <summary>
    /// Opening with a different 32-byte key must NOT return the original data.
    /// The current implementation surfaces this as "Table Secrets does not exist"
    /// because the table directory decrypts to garbage and no tables are loaded.
    /// A future improvement should throw an explicit AuthenticationException.
    /// </summary>
    [Fact]
    public async Task WrongKey_CannotAccess_Data()
    {
        var path = DbPath("wrongkey");
        var correctKey = GenerateKey();
        var wrongKey = GenerateKey();

        await CreateEncryptedDbWithSecret(path, correctKey);

        var db = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(wrongKey));
        try
        {
            // The original table must not be accessible.
            // Current behavior: table directory decrypts to garbage → table not found.
            var tables = db.GetTables();
            Assert.DoesNotContain(tables, t => t.Name.Equals("Secrets", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    // ── 4. Wrong-key file integrity ──────────────────────────────────────

    /// <summary>
    /// Opening with a wrong key must NOT mutate the file on disk.
    /// The file hash before and after a wrong-key open must be identical.
    /// This guards against a wrong-key session accidentally overwriting
    /// encrypted blocks with garbage or plaintext.
    /// </summary>
    [Fact]
    public async Task WrongKey_DoesNotMutate_File()
    {
        var path = DbPath("fileintegrity");
        var correctKey = GenerateKey();
        var wrongKey = GenerateKey();

        await CreateEncryptedDbWithSecret(path, correctKey);

        var hashBefore = ComputeSha256(path);

        // Open with wrong key, perform a read attempt, then dispose
        var db = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(wrongKey));
        try
        {
            // Attempt to query (will fail to find the table, but must not write)
            try { db.ExecuteQuery("SELECT * FROM Secrets"); }
            catch { /* Expected: table not found or decryption garbage */ }
        }
        finally
        {
            await db.DisposeAsync();
        }

        var hashAfter = ComputeSha256(path);
        Assert.Equal(hashBefore, hashAfter);
    }

    // ── 5. Wrong-key reopen survival ─────────────────────────────────────

    /// <summary>
    /// After a wrong-key open-and-close cycle, reopening with the CORRECT key
    /// must still return the original data. This ensures the wrong-key session
    /// did not corrupt the encrypted blocks or the table directory.
    /// </summary>
    [Fact]
    public async Task WrongKey_OriginalData_SurvivesReopen()
    {
        var path = DbPath("survival");
        var correctKey = GenerateKey();
        var wrongKey = GenerateKey();

        await CreateEncryptedDbWithSecret(path, correctKey);

        // Wrong-key open and close
        var wrongDb = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(wrongKey));
        await wrongDb.DisposeAsync();

        // Correct-key reopen must still work
        var db = _factory.CreateWithOptions(path, DummyPassword, EncryptedOptions(correctKey));
        try
        {
            var rows = db.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal(SecretData, rows[0]["Data"]?.ToString());
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    // ── 6. Key length validation ─────────────────────────────────────────

    /// <summary>
    /// DatabaseOptions.Validate() must reject EncryptionKey values that are
    /// not exactly 32 bytes when EnableEncryption is true.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void EncryptionKey_MustBe32Bytes(int keyLength)
    {
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = new byte[keyLength]
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    // ── 7. DI requirement ────────────────────────────────────────────────

    /// <summary>
    /// When EnableEncryption is true, ICryptoService must be registered in DI.
    /// Creating a SingleFile database without ICryptoService must throw.
    /// </summary>
    [Fact]
    public void EncryptionKey_RequiresCryptoService_InDI()
    {
        // Build a provider WITHOUT ICryptoService
        var emptyServices = new ServiceCollection();
        using var emptyProvider = emptyServices.BuildServiceProvider();
        var emptyFactory = new DatabaseFactory(emptyProvider);

        var path = DbPath("nodiservice");
        var key = GenerateKey();

        Assert.Throws<InvalidOperationException>(() =>
            emptyFactory.CreateWithOptions(path, DummyPassword, EncryptedOptions(key)));
    }

    // ── 8. Control: no encryption means plaintext ────────────────────────

    /// <summary>
    /// When EnableEncryption is false, data IS stored in plaintext.
    /// This is the control test that confirms the plaintext scan in test 1
    /// is meaningful (i.e., the scan technique works).
    /// </summary>
    [Fact]
    public async Task NoEncryption_DataIsPlaintext_Control()
    {
        var path = DbPath("control");

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = false,
            CreateImmediately = true
        };

        var db = _factory.CreateWithOptions(path, DummyPassword, options);
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL($"INSERT INTO Secrets VALUES (1, '{SecretData}')");
            db.ForceSave();
        }
        finally
        {
            await db.DisposeAsync();
        }

        var secretBytes = Encoding.UTF8.GetBytes(SecretData);
        Assert.True(FileContainsBytes(path, secretBytes),
            "Control test: secret should be plaintext when encryption is disabled.");
    }
}
