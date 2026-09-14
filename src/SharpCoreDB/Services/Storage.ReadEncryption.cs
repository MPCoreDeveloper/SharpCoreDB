// <copyright file="Storage.ReadEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Services;

using System.Collections.Concurrent;

/// <summary>
/// Per-path memo of what a file's bytes actually are, for the read path.
/// </summary>
/// <remarks>
/// <para>
/// The historic shape of the legacy whole-file read was "try to decrypt, catch on failure". For a
/// plaintext file that costs a full-size allocation <i>and</i> a thrown exception on <b>every</b>
/// read — and plaintext is the normal case for table files under the default configuration, because
/// per-record at-rest encryption is opt-in
/// (<c>docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md</c> §3-1a/§3-1c).
/// </para>
/// <para>
/// The verdict is therefore memoised per path: the probe is paid once, and every later read takes a
/// branch. A write to the same path re-seeds the verdict — with <see cref="ReadEncryption.Plaintext"/>
/// when the writer knew the bytes were unencrypted, otherwise by forgetting the answer so the next
/// read re-probes. A file can therefore never keep a stale answer.
/// </para>
/// </remarks>
public partial class Storage
{
    /// <summary>What the read path has established about a file so far.</summary>
    private enum ReadEncryption
    {
        /// <summary>Not probed yet.</summary>
        Unknown = 0,

        /// <summary>Raw bytes: no per-record magic header and the legacy decrypt attempt failed.</summary>
        Plaintext = 1,

        /// <summary>Legacy single-blob whole-file AES (meta.dat, .salt, ...).</summary>
        LegacyEncrypted = 2,
    }

    private readonly ConcurrentDictionary<string, ReadEncryption> _readEncryption =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the legacy whole-file decrypt should still be attempted for <paramref name="path"/>.
    /// False once the path is known to be plaintext.
    /// </summary>
    private bool ShouldAttemptLegacyDecrypt(string path)
        => _readEncryption.TryGetValue(path, out ReadEncryption known) && known == ReadEncryption.Plaintext
            ? false
            : true;

    private void RememberLegacyEncryption(string path, bool encrypted)
        => _readEncryption[path] = encrypted ? ReadEncryption.LegacyEncrypted : ReadEncryption.Plaintext;

    /// <summary>
    /// Called by the whole-file writers after they have decided (and applied) their encryption.
    /// A plainly-unencrypted write can be recorded immediately; an encrypted write forgets the
    /// verdict instead, because the on-disk framing for table files is decided per file by the append
    /// path and only a probe can tell the read path which one it got.
    /// </summary>
    private void InvalidateReadEncryption(string path)
    {
        if (this.noEncryption)
        {
            RememberLegacyEncryption(path, encrypted: false);
            return;
        }

        _readEncryption.TryRemove(path, out _);
    }
}
