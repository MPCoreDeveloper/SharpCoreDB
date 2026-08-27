namespace SharpCoreDB.Tests;

using SharpCoreDB.Base32Encoding;

/// <summary>
/// Unit tests for the Ulid (Universally Unique Lexicographically Sortable Identifier) class.
/// Tests ULID generation, parsing, and timestamp extraction functionality.
/// </summary>
public class UlidTests
{
    [Fact]
    public void Ulid_NewUlid_GeneratesValidUlid()
    {
        // Act
        var ulid = Ulid.NewUlid();

        // Assert
        Assert.NotNull(ulid);
        Assert.NotNull(ulid.Value);
        Assert.NotEmpty(ulid.Value);
        Assert.Equal(26, ulid.Value.Length); // ULIDs are 26 characters
    }

    [Fact]
    public void Ulid_NewUlid_GeneratesUniqueValues()
    {
        // Act
        var ulid1 = Ulid.NewUlid();
        var ulid2 = Ulid.NewUlid();

        // Assert
        Assert.NotEqual(ulid1.Value, ulid2.Value);
    }

    [Fact]
    public void Ulid_Parse_ValidUlid_Success()
    {
        // Arrange
        var originalUlid = Ulid.NewUlid();

        // Act
        var parsedUlid = Ulid.Parse(originalUlid.Value);

        // Assert
        Assert.NotNull(parsedUlid);
        Assert.Equal(originalUlid.Value, parsedUlid.Value);
    }

    [Fact]
    public void Ulid_ToDateTime_ReturnsValidDateTime()
    {
        // Arrange
        var beforeGeneration = DateTime.UtcNow.AddSeconds(-1);
        var ulid = Ulid.NewUlid();
        var afterGeneration = DateTime.UtcNow.AddSeconds(1);

        // Act
        var parsedUlid = Ulid.Parse(ulid.Value);
        var timestamp = parsedUlid.ToDateTime();

        // Assert
        Assert.True(timestamp >= beforeGeneration);
        Assert.True(timestamp <= afterGeneration);
    }

    [Fact]
    public void Ulid_Value_IsUpperCase()
    {
        // Act
        var ulid = Ulid.NewUlid();

        // Assert
        Assert.Equal(ulid.Value, ulid.Value.ToUpper());
    }

    [Fact]
    public void Ulid_OrderedByTime_LexicographicallySortable()
    {
        // Arrange - Generate ULIDs with slight time delays
        var ulid1 = Ulid.NewUlid();
        Thread.Sleep(10); // Small delay to ensure different timestamps
        var ulid2 = Ulid.NewUlid();
        Thread.Sleep(10);
        var ulid3 = Ulid.NewUlid();

        // Act - Compare lexicographically
        var comparison1 = string.Compare(ulid1.Value, ulid2.Value, StringComparison.Ordinal);
        var comparison2 = string.Compare(ulid2.Value, ulid3.Value, StringComparison.Ordinal);

        // Assert - Later ULIDs should be lexicographically greater
        Assert.True(comparison1 < 0); // ulid1 < ulid2
        Assert.True(comparison2 < 0); // ulid2 < ulid3
    }

    [Fact]
    public void Ulid_MultipleGenerations_AllValid()
    {
        // Act
        var ulids = new List<Ulid>();
        for (int i = 0; i < 100; i++)
        {
            ulids.Add(Ulid.NewUlid());
        }

        // Assert
        Assert.Equal(100, ulids.Count);
        foreach (var ulid in ulids)
        {
            Assert.NotNull(ulid.Value);
            Assert.Equal(26, ulid.Value.Length);
        }

        // Verify all are unique
        var uniqueValues = ulids.Select(u => u.Value).Distinct().Count();
        Assert.Equal(100, uniqueValues);
    }

    [Fact]
    public void Ulid_ToString_ReturnsValue()
    {
        // Arrange
        var ulid = Ulid.NewUlid();

        // Act
        var stringValue = ulid.ToString();

        // Assert
        Assert.Equal(ulid.Value, stringValue);
    }

    // ================================================================
    // ULID specification compatibility (Crockford Base32)
    // ================================================================

    [Fact]
    public void Ulid_SpecVector_NewUlid_TimestampMatchesCanonicalPrefix()
    {
        // Canonical test vector (official ULID spec / oklog/ulid Go implementation):
        //   ULID: 0000XSNJG0MQJHBF4QX1EFD6Y3  <=>  timestamp 1000000000 ms
        var ulid = Ulid.NewUlid(1_000_000_000);

        Assert.StartsWith("0000XSNJG0", ulid.Value);
    }

    [Fact]
    public void Ulid_SpecVector_Parse_ReturnsExpectedTimestamp()
    {
        // The first 10 characters of a standards-compliant ULID encode the 48-bit timestamp.
        var ulid = Ulid.Parse("0000XSNJG0MQJHBF4QX1EFD6Y3");

        Assert.Equal(1_000_000_000L, ulid.ToUnixTime());
    }

    [Fact]
    public void Ulid_Encode_Is26Chars_FirstCharCarriesOnly3Bits()
    {
        var ulid = Ulid.NewUlid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // A ULID is a 128-bit value in 26 Crockford Base32 characters; the first
        // character carries only 3 significant bits, so it is always in "01234567".
        Assert.Equal(26, ulid.Value.Length);
        Assert.Contains(ulid.Value[0], "01234567");
    }

    [Fact]
    public void Ulid_Decode_FirstCharacterAbove7_Rejected()
    {
        // The largest valid ULID is 7ZZZ...Z (2^128 - 1); a leading character above '7'
        // would exceed the 128-bit range and must be rejected.
        Assert.Throws<ArgumentException>(() => Base32.Decode("8ZZZZZZZZZZZZZZZZZZZZZZZZZ"));
    }

    [Fact]
    public void Ulid_Decode_MaximumValidUlid_Accepted()
    {
        // 7ZZZ...Z is the maximum valid ULID (timestamp = 2^48 - 1).
        var bytes = Base32.Decode("7ZZZZZZZZZZZZZZZZZZZZZZZZZ");

        Assert.Equal(16, bytes.Length);
    }

    [Fact]
    public void Ulid_NewUlid_TimestampAbove48Bits_Rejected()
    {
        // 2^48 is outside the ULID timestamp range (max 2^48 - 1 milliseconds).
        Assert.Throws<ArgumentOutOfRangeException>(() => Ulid.NewUlid(0x1_0000_0000_0000));
    }

    // ================================================================
    // Legacy (pre-1.9.5) ULID upgrade / backwards compatibility
    // ================================================================

    [Fact]
    public void Ulid_FromLegacy_Preserves128BitValue()
    {
        // Build a 16-byte value: timestamp 1000000000 ms + fixed 80-bit randomness.
        var bytes = new byte[16];
        var ts = 1_000_000_000L;
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(ts & 0xFF);
            ts >>= 8;
        }

        for (int i = 6; i < 16; i++)
        {
            bytes[i] = 0xAB;
        }

        // Legacy encoding (what pre-1.9.5 SharpCoreDB produced).
        var legacy = Base32.LegacyEncode(bytes);
        Assert.Equal(26, legacy.Length);

        // Convert and verify the 128-bit value round-trips exactly.
        var upgraded = Ulid.FromLegacy(legacy);
        var decoded = Base32.Decode(upgraded.Value);

        Assert.Equal(bytes, decoded);
    }

    [Fact]
    public void Ulid_FromLegacy_PreservesTimestamp()
    {
        var bytes = new byte[16];
        var ts = 1_000_000_000L;
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(ts & 0xFF);
            ts >>= 8;
        }

        for (int i = 6; i < 16; i++)
        {
            bytes[i] = 0x11;
        }

        var legacy = Base32.LegacyEncode(bytes);
        var upgraded = Ulid.FromLegacy(legacy);

        Assert.Equal(1_000_000_000L, upgraded.ToUnixTime());
    }

    [Fact]
    public void Ulid_FromLegacy_InvalidString_Throws()
    {
        Assert.Throws<ArgumentException>(() => Ulid.FromLegacy("not-a-valid-ulid"));
        Assert.Throws<ArgumentException>(() => Ulid.FromLegacy(""));
    }

    [Fact]
    public void Ulid_TryFromLegacy_ValidAndInvalid()
    {
        var bytes = new byte[16];
        bytes[0] = 0x01;
        for (int i = 1; i < 16; i++)
        {
            bytes[i] = 0x23;
        }

        var legacy = Base32.LegacyEncode(bytes);

        Assert.True(Ulid.TryFromLegacy(legacy, out var upgraded));
        Assert.NotNull(upgraded);
        Assert.Equal(bytes, Base32.Decode(upgraded!.Value));

        Assert.False(Ulid.TryFromLegacy("garbage", out _));
        Assert.False(Ulid.TryFromLegacy("", out _));
    }
}
