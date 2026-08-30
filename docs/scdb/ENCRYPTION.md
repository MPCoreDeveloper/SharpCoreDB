# SharpCoreDB Single-File (`.scdb`) Encryption

**Applies to:** single-file (`.scdb`) storage mode · v2.0.x+
**Last updated:** 2026-08-29

---

## 1. Overview

Single-file databases are protected by **AES-256-GCM** encryption **at rest**:

- **Block data** — table rows, index data, table directory, `sys:*` metadata.
- **Metadata regions** — the **block registry** (block/table names, offsets, lengths, types,
  checksums), the **free-space map** (allocation bitmap) and the **write-ahead log**
  (entry headers + payloads).

Before this feature, only block *data* was encrypted (`EncryptionMode = 1`, issue #341); the
block registry, FSM and WAL stayed **plaintext**, leaking schema/structure metadata. New
encrypted files use `EncryptionMode = 2` (full at-rest). Files created with the old
block-data-only mode remain readable.

The **only** plaintext bytes in an encrypted file are the minimal bootstrap required to open it:

| Header field | Purpose |
|---|---|
| `Magic`, `FormatVersion`, `PageSize`, `HeaderSize` | File identification |
| `EncryptionMode` (2) | Full at-rest encryption marker |
| `EncryptionKeyId` | Rotation counter (incremented on every password/key rotation) |
| `KdfSalt` (32 B), `KdfIterations`, `KdfAlgorithm` | Password → key derivation parameters |
| `WrappedDek` (60 B) | The wrapped data-encryption-key (`[nonce][ciphertext][tag]`) |

---

## 2. Key model (envelope encryption)

Two key-material modes are supported; they are **mutually exclusive** in `DatabaseOptions`.

### 2.1 Raw-key mode (default)

You supply a 32-byte key directly:

```csharp
var options = new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionKey = Convert.FromHexString("00010203...1F"), // exactly 32 bytes
    CreateImmediately = true,
};
var db = factory.CreateWithOptions("app.scdb", "unused", options);
```

The key is used **directly** as the AES-256-GCM data-encryption-key (DEK). The provider copies the
key internally and zeroizes its copy on dispose — the caller's array is never modified.

### 2.2 Password mode (envelope encryption, recommended)

You supply a password/passphrase. The engine:

1. generates a **random per-file salt** (32 B) and a **random per-file DEK** (32 B),
2. derives a **key-encryption-key (KEK)** from the password + salt via
   `PBKDF2-HMAC-SHA256` (default **600,000 iterations**, the OWASP-2024 recommendation),
3. wraps the DEK with the KEK (`AES-256-GCM`, `[nonce(12)][cipher(32)][tag(16)]`) and stores the
   wrapped DEK + salt in the header.

```csharp
var options = new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionPassword = "correct horse battery staple",
    CreateImmediately = true,
};
var db = factory.CreateWithOptions("app.scdb", "unused", options);
```

The DEK (what actually encrypts your data) never appears in plaintext — only the **wrapped** DEK
is stored, and only the KEK derived from your password can unwrap it. This makes **password
change an O(1) re-wrap** (Section 4.1) instead of a full file re-encryption.

> **Choose your mode carefully.** Both modes are supported and interoperate at the file level
> through `EncryptionMode`/`KeyMaterialPresent` header flags. A file created in password mode
> must be reopened with the password; a raw-key file must be reopened with the key.

---

## 3. Opening encrypted files

```csharp
// Password mode — open with the same password.
var db = factory.CreateWithOptions("app.scdb", "unused", new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionPassword = "correct horse battery staple",
});
```

Wrong credentials **fail loudly**: a wrong password fails the DEK unwrap
(`CryptographicException`), and a wrong raw key fails GCM authentication while loading the
encrypted block registry. The database will not silently open with an empty schema.

---

## 4. Key & password rotation

> 🔑 Passwords can be exposed. Being able to rotate the credential without rebuilding the
> database is a requirement for production encryption. SharpCoreDB provides two rotation
> operations.

### 4.1 `ChangeEncryptionPasswordAsync(newPassword)` — O(1) re-wrap

Only valid for **password-mode** files. The DEK is unchanged; a new random salt is generated, a
new KEK is derived from the new password, and the **same DEK** is re-wrapped and written back to
the header. No data is rewritten.

```csharp
var result = await db.ChangeEncryptionPasswordAsync("a-new-better-password");
if (result.Success)
{
    Console.WriteLine($"Password changed (key id {result.KeyId}).");
}
```

After this operation the old password no longer opens the file.

### 4.2 `RotateEncryptionKeyAsync(newKey | newPassword)` — full re-key

Re-encrypts **every block plus the block registry, FSM and WAL** under a fresh DEK. It is
implemented as a **crash-safe rewrite to a temporary file followed by an atomic swap** (the same
pattern used by full VACUUM, issue #343): the original file stays valid until the swap completes.

```csharp
// Raw-key mode: rotate to a fresh key.
var newKey = RandomNumberGenerator.GetBytes(32);
var result = await db.RotateEncryptionKeyAsync(newKey: newKey);
// ...open future sessions with newKey.

// Password mode: generate a fresh DEK and wrap it with a (new) password.
var result = await db.RotateEncryptionKeyAsync(newPassword: "fresh-password");
// ...open future sessions with "fresh-password".
```

`RotateEncryptionKeyAsync` returns `EncryptionRotationResult` with `Operation`,
`KeyId` (rotation counter), `BlocksReEncrypted` and `Success`/`ErrorMessage`.

### 4.3 `EncryptionRotationResult`

```csharp
public enum EncryptionRotationOperation { PasswordChanged, KeyRotated }

public sealed class EncryptionRotationResult
{
    public EncryptionRotationOperation Operation { get; init; }
    public ushort KeyId { get; init; }
    public int BlocksReEncrypted { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
```

### 4.4 When to rotate

- **Password change** (`ChangeEncryptionPasswordAsync`): after any suspected password exposure,
  onboarding/offboarding, or policy expiry. Cheap — run it regularly.
- **DEK rotation** (`RotateEncryptionKeyAsync`): after a suspected **key** exposure, or when the
  AES-GCM operation counter approaches the 2^32 nonce-exhaustion limit (the engine tracks this
  conceptually via `CryptoConstants.MAX_GCM_OPERATIONS`). Expensive (full rewrite) — run rarely.

---

## 5. Full worked example

```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();

var path = Path.Combine(Path.GetTempPath(), $"demo-{Guid.NewGuid():N}.scdb");
const string password = "initial-password";

// ── 1. Create a password-encrypted single-file database ─────────────────────────────
var createOptions = new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionPassword = password,
    CreateImmediately = true,
};
using (var db = factory.CreateWithOptions(path, "unused", createOptions))
{
    db.ExecuteSQL("CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Note TEXT)");
    db.ExecuteSQL("INSERT INTO Customers VALUES (1, 'Alice', 'classified-note')");
    db.ExecuteSQL("INSERT INTO Customers VALUES (2, 'Bob',   'another-secret')");
    db.ForceSave();
}

// ── 2. Reopen, read back, and change the password (O(1), no data rewrite) ──────────
using (var db = factory.CreateWithOptions(path, "unused", new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionPassword = password,
}))
{
    var rows = db.ExecuteQuery("SELECT COUNT(*) AS c FROM Customers");
    Console.WriteLine($"Customers after reopen: {rows[0]["cnt"]}");

    var changed = await db.ChangeEncryptionPasswordAsync("rotated-password");
    Console.WriteLine($"Password changed: {changed.Success} (key id {changed.KeyId})");

    // Optional: full re-key under a brand-new DEK wrapped with a new password.
    var rekeyed = await db.RotateEncryptionKeyAsync(newPassword: "post-compromise-password");
    Console.WriteLine($"Re-keyed: {rekeyed.Success} ({rekeyed.BlocksReEncrypted} blocks)");
}

// ── 3. The old password no longer works ─────────────────────────────────────────────
try
{
    using var stale = factory.CreateWithOptions(path, "unused", new DatabaseOptions
    {
        StorageMode = StorageMode.SingleFile,
        EnableEncryption = true,
        EncryptionPassword = password, // old password
    });
    Console.WriteLine("UNEXPECTED: old password still works!");
}
catch (CryptographicException)
{
    Console.WriteLine("OK: old password is rejected.");
}

// ── 4. Full VACUUM keeps working on encrypted databases (issue #343) ───────────────
using (var db = factory.CreateWithOptions(path, "unused", new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    EnableEncryption = true,
    EncryptionPassword = "post-compromise-password",
}))
{
    var vacuum = await db.VacuumAsync(VacuumMode.Full);
    Console.WriteLine($"VACUUM (encrypted): {vacuum.Success}");
    Console.WriteLine($"Rows still there:   {db.ExecuteQuery("SELECT COUNT(*) AS c FROM Customers")[0]["cnt"]}");
}

File.Delete(path);
```

---

## 6. Format & layout

Encrypted regions use the in-place page cipher (`[nonce(12)][ciphertext][tag(16)]` layout):

| Region | Encryption scope | Notes |
|---|---|---|
| `BlockRegistry` | root block (grows by relocation, issue #345) | written as one full-size ciphertext blob; GCM auth failure on open ⇒ `CryptographicException` |
| `FreeSpaceMap` | named block `sys:fsm` (grows by relocation) | header + L1 bitmap + extents encrypted together |
| `WAL` | each 4096-byte entry slot | WAL header stays plaintext (head/tail offsets); payload capped at 3972 B to leave room for the GCM overhead |
| Header | bootstrap only | magic/version/mode/key-id + KDF salt + wrapped DEK |

### Compatibility

- `EncryptionMode = 0` — plaintext file, unchanged.
- `EncryptionMode = 1` — block-data-only encryption (legacy #341). Still readable; metadata
  regions are plaintext and **not** newly encrypted.
- `EncryptionMode = 2` — full at-rest encryption (this feature). New encrypted files.

---

## 7. Native AOT & Sonar compliance

- **Native AOT safe.** The implementation uses only AOT-compatible primitives:
  `System.Security.Cryptography.AesGcm`, `Rfc2898DeriveBytes.Pbkdf2`,
  `MemoryMarshal.AsBytes`, `Span<T>`/`fixed` buffers and `RandomNumberGenerator` — **no runtime
  reflection, no `dynamic`, no `Expression` compilation**. The `tools/SharpCoreDB.AotSmoke`
  console app publishes with `PublishAot=true` and exercises the encrypted single-file path
  (password mode, insert, password change, DEK rotation, reopen, full VACUUM) with exit code 0.
- **SonarClean.** New code follows the repository's `CODING_STANDARDS_CSHARP14.md` conventions:
  XML docs on all public members, no `#region` misuse, no magic numbers (constants in
  `CryptoConstants` / `ScdbFileHeader`), no commented-out code, no unused fields/usings.

---

## 8. Test coverage

`tests/SharpCoreDB.Tests`:

- `SingleFileEncryptionTests` — raw-key + password round-trips, wrong key/password rejection,
  open-without-encryption rejection, **no plaintext metadata on disk**, password change
  (O(1) re-wrap), DEK rotation (full re-key), and full VACUUM on an encrypted database.
- `EncryptionCryptoTests` — `AesGcmEncryption` key-derivation + wrap/unwrap round-trips,
  `EncryptPage`/`DecryptPage` region round-trip, wrong-KEK rejection, tamper detection.
- `DatabaseOptions` validation — `EncryptionKey` vs `EncryptionPassword` mutual exclusivity.

