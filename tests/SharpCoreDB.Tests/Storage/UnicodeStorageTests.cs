// tests/SharpCoreDB.Tests/Storage/UnicodeStorageTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// REGRESSION TESTS: Unicode storage, retrieval, and query matching.
/// 
/// CONTEXT:
/// Edge telemetry often contains internationalized service names, log messages, 
/// and user-generated content. This suite verifies that the engine correctly handles 
/// 3-byte CJK, 4-byte Emoji (including ZWJ sequences), Right-to-Left scripts, and 
/// combining character sequences without silent corruption or false equivalence.
/// </summary>
public sealed class UnicodeStorageTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly List<string> _filesToCleanup = [];

    public UnicodeStorageTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"unicode_test_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(_testDbPath);
    }

    private static void DisposeDatabase(IDatabase database)
    {
        (database as IDisposable)?.Dispose();
    }

    private static DatabaseFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<DatabaseFactory>();
    }

    // ========================================
    // Roundtrip & WHERE Clause Tests
    // ========================================

    [Theory]
    [InlineData("こんにちは世界", "Japanese (3-byte UTF-8)")]
    [InlineData("你好世界", "Chinese (3-byte UTF-8)")]
    [InlineData("안녕하세요", "Korean (3-byte UTF-8)")]
    [InlineData("مرحبا بالعالم", "Arabic RTL (3-byte UTF-8)")]
    [InlineData("שלום עולם", "Hebrew RTL (3-byte UTF-8)")]
    public void Unicode_3ByteAndRTL_RoundtripAndWhereClause_ShouldMatchExactly(string text, string description)
    {
        var factory = BuildFactory();
        var db = factory.Create(_testDbPath, "unused");

        db.ExecuteSQL("CREATE TABLE lang_test (id INT, phrase TEXT)");
        db.ExecuteSQL($"INSERT INTO lang_test VALUES (1, '{text}')");
        db.Flush();

        // Act - Query back using WHERE clause
        var results = db.ExecuteQuery($"SELECT phrase FROM lang_test WHERE phrase = '{text}'");

        // Assert
        Assert.Single(results);
        Assert.Equal(text, results[0]["phrase"]?.ToString());

        DisposeDatabase(db);
    }

    [Fact]
    public void Unicode_EmojiAndZWJ_RoundtripAndWhereClause_ShouldMatchExactly()
    {
        // Arrange - Complex emojis using Zero Width Joiners (ZWJ) and skin tone modifiers
        var familyEmoji = "👨‍👩‍👧‍👦"; // 7 code points, 25 bytes in UTF-8
        var thumbsUp = "👍🏽";          // Thumbs up + medium skin tone modifier

        var factory = BuildFactory();
        var db = factory.Create(_testDbPath, "unused");

        db.ExecuteSQL("CREATE TABLE emoji_test (id INT, symbol TEXT)");
        db.ExecuteSQL($"INSERT INTO emoji_test VALUES (1, '{familyEmoji}')");
        db.ExecuteSQL($"INSERT INTO emoji_test VALUES (2, '{thumbsUp}')");
        db.Flush();

        // Act
        var familyResult = db.ExecuteQuery($"SELECT symbol FROM emoji_test WHERE symbol = '{familyEmoji}'");
        var thumbsResult = db.ExecuteQuery($"SELECT symbol FROM emoji_test WHERE symbol = '{thumbsUp}'");

        // Assert
        Assert.Single(familyResult);
        Assert.Equal(familyEmoji, familyResult[0]["symbol"]?.ToString());
        
        Assert.Single(thumbsResult);
        Assert.Equal(thumbsUp, thumbsResult[0]["symbol"]?.ToString());

        DisposeDatabase(db);
    }

    // ========================================
    // Combining Characters (Normalization) Tests
    // ========================================

    [Fact]
    public void Unicode_CombiningCharacters_ShouldNotNormalize_And_MatchExactly()
    {
        // Arrange
        // U+00E9 (é) is the precomposed form (2 bytes in UTF-8: C3 A9)
        // U+0065 (e) + U+0301 (combining acute accent) is the decomposed form (3 bytes in UTF-8: 65 CC 81)
        var precomposed = "caf\u00E9";   // café
        var decomposed = "cafe\u0301";    // café (visually identical, byte-different)

        var factory = BuildFactory();
        var db = factory.Create(_testDbPath, "unused");

        db.ExecuteSQL("CREATE TABLE norm_test (id INT, word TEXT)");
        db.ExecuteSQL($"INSERT INTO norm_test VALUES (1, '{precomposed}')");
        db.ExecuteSQL($"INSERT INTO norm_test VALUES (2, '{decomposed}')");
        db.Flush();

        // Act - Query for the precomposed version
        var results = db.ExecuteQuery($"SELECT id FROM norm_test WHERE word = '{precomposed}'");

        // Assert
        // SharpCoreDB stores raw UTF-8 bytes and compares byte-for-byte.
        // It should NOT normalize the strings. Therefore, querying for precomposed
        // should only return ID 1, not ID 2.
        Assert.Single(results);
        Assert.Equal(1L, Convert.ToInt64(results[0]["id"]));

        // Verify byte lengths internally if we read the raw block, but at the SQL API level,
        // verifying they are treated as distinct rows is the critical invariant.
        var allRows = db.ExecuteQuery("SELECT * FROM norm_test ORDER BY id");
        Assert.Equal(2, allRows.Count);
        Assert.NotEqual(allRows[0]["word"]?.ToString(), allRows[1]["word"]?.ToString()); // Strict string inequality

        DisposeDatabase(db);
    }

    [Fact]
    public void Unicode_LargeMixedPayload_Roundtrip_ShouldMatchExactly()
    {
        // Arrange - A massive JSON-like payload mixing all script types
        var payload = new StringBuilder();
        payload.Append("{\"logs\":[");
        for (int i = 0; i < 100; i++)
        {
            payload.Append($"{{\"svc\":\"svc-{i}\",\"msg\":\"Hello こんにちは مرحبا שלום 👨‍👩‍👧‍👦\"}},");
        }
        payload.Append("]}");

        var originalString = payload.ToString();
        var factory = BuildFactory();
        var db = factory.Create(_testDbPath, "unused");

        // Use a parameterized-style approach or just large block insertion
        // Since SQL INSERT might choke on massive strings without parameterization,
        // we'll use the low-level provider to prove block-level UTF-8 integrity.
        
        var options = DatabaseOptions.CreateSingleFileDefault();
        using var provider = SingleFileStorageProvider.Open(_testDbPath + "_provider.scdb", options);
        _filesToCleanup.Add(_testDbPath + "_provider.scdb");

        var originalBytes = Encoding.UTF8.GetBytes(originalString);
        
        provider.WriteBlockAsync("mixed_utf8_block", originalBytes).GetAwaiter().GetResult();
        provider.FlushAsync().GetAwaiter().GetResult();

        // Act
        var readBytes = provider.ReadBlockAsync("mixed_utf8_block").GetAwaiter().GetResult();
        var readString = Encoding.UTF8.GetString(readBytes!);

        // Assert
        Assert.Equal(originalString, readString);
    }

    // ========================================
    // Cleanup
    // ========================================

    public void Dispose()
    {
        foreach (var file in _filesToCleanup)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".wal")) File.Delete(file + ".wal");
                if (File.Exists(file + ".vacuum.tmp")) File.Delete(file + ".vacuum.tmp");
                if (File.Exists(file + ".vacuum.tmp.scdb")) File.Delete(file + ".vacuum.tmp.scdb");
                if (File.Exists(file + ".backup")) File.Delete(file + ".backup");
            }
            catch { }
        }
    }
}