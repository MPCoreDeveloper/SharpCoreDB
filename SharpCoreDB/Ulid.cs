// <copyright file="Ulid.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.Base32Encoding;
using System.Security.Cryptography;

/// <summary>
/// Represents a Universally Unique Lexicographically Sortable Identifier (ULID).
/// </summary>
/// <param name="Value">The string value of the ULID.</param>
public record Ulid(string Value)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ulid"/> class.
    /// Initializes a new instance of Ulid with an empty value.
    /// </summary>
    public Ulid()
        : this(string.Empty)
    {
    }

    /// <summary>
    /// Creates a new ULID with the current timestamp.
    /// </summary>
    /// <returns>A new Ulid instance.</returns>
    public static Ulid NewUlid()
    {
        return NewUlid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Creates a new ULID with the specified timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp to use.</param>
    /// <returns>A new Ulid instance.</returns>
    public static Ulid NewUlid(DateTimeOffset timestamp)
    {
        return NewUlid(timestamp.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Creates a new ULID with the specified timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp to use.</param>
    /// <returns>A new Ulid instance.</returns>
    public static Ulid NewUlid(DateTime timestamp)
    {
        return NewUlid(new DateTimeOffset(timestamp).ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Creates a new ULID with the specified timestamp in milliseconds since Unix epoch.
    /// </summary>
    /// <param name="timestamp">The timestamp in milliseconds.</param>
    /// <returns>A new Ulid instance.</returns>
    public static Ulid NewUlid(long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentException("Timestamp must be a positive number.");
        }

        Span<byte> ulidBytes = stackalloc byte[16];

        for (int i = 5; i >= 0; i--)
        {
            ulidBytes[i] = (byte)(timestamp & 0xFF);
            timestamp >>= 8;
        }

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(ulidBytes[6..]);
        }

        string ulid = Base32.Encode(ulidBytes.ToArray());
        return new Ulid(ulid);
    }

    /// <summary>
    /// Extracts the timestamp from a ULID.
    /// </summary>
    /// <param name="ulid">The ULID to extract from.</param>
    /// <returns>The timestamp as a DateTime.</returns>
    public static DateTime GetTimestampFromUlid(Ulid ulid)
    {
        if (ulid.Value.Length != 26)
        {
            throw new ArgumentException("Invalid ULID length. ULID should be 26 characters long.");
        }

        ReadOnlySpan<byte> bytes = Base32.Decode(ulid.Value[..10]);

        long timestamp = ExtractTimestamp(bytes);

        return DateTime.UnixEpoch.AddMilliseconds(timestamp);
    }

    /// <summary>
    /// Converts the ULID to a DateTime.
    /// </summary>
    /// <returns>The timestamp as a DateTime.</returns>
    public DateTime ToDateTime()
    {
        return GetTimestampFromUlid(this);
    }

    /// <summary>
    /// Converts the ULID to epoch milliseconds.
    /// </summary>
    /// <returns>The timestamp in milliseconds since Unix epoch.</returns>
    public long ToEpoch()
    {
        if (string.IsNullOrEmpty(this.Value) || this.Value.Length != 26)
        {
            return 0; // if the value is empty or not 26 characters long, return 0
        }

        ReadOnlySpan<byte> bytes = Base32.Decode(this.Value[..10]);

        return ExtractTimestamp(bytes);
    }

    /// <summary>
    /// Converts the ULID to Unix time in milliseconds.
    /// </summary>
    /// <returns>The timestamp in milliseconds since Unix epoch.</returns>
    public long ToUnixTime()
    {
        return this.ToEpoch();
    }

    private static long ExtractTimestamp(ReadOnlySpan<byte> bytes)
    {
        long timestamp = 0;
        for (int i = 0; i < 6; i++)
        {
            timestamp = timestamp << 8 | bytes[i];
        }

        return timestamp;
    }

    /// <summary>
    /// Returns the string representation of the ULID.
    /// </summary>
    /// <returns>The ULID value.</returns>
    public override string ToString()
    {
        return this.Value;
    }

    /// <summary>
    /// Checks if the ULID has a value.
    /// </summary>
    /// <returns>True if the ULID has a value, otherwise false.</returns>
    public bool HasValue()
    {
        return !string.IsNullOrEmpty(this.Value);
    }

    /// <summary>
    /// Tries to parse a string into a ULID.
    /// </summary>
    /// <param name="ulidString">The string to parse.</param>
    /// <param name="ulid">The parsed ULID if successful.</param>
    /// <returns>True if parsing was successful, otherwise false.</returns>
    public static bool TryParse(string ulidString, out Ulid? ulid)
    {
        ulid = null;

        if (string.IsNullOrEmpty(ulidString) || ulidString.Length != 26)
        {
            return false;
        }

        try
        {
            ReadOnlySpan<byte> bytes = Base32.Decode(ulidString);
            long timestamp = ExtractTimestamp(bytes);
            if (timestamp < 0)
            {
                return false;
            }

            ulid = new Ulid(ulidString);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a string into a ULID.
    /// </summary>
    /// <param name="ulidString">The string to parse.</param>
    /// <returns>The parsed ULID.</returns>
    /// <exception cref="ArgumentException">Thrown when the ULID string is not valid.</exception>
    public static Ulid Parse(string ulidString)
    {
        if (string.IsNullOrEmpty(ulidString) || ulidString.Length != 26)
        {
            throw new ArgumentException("Invalid ULID string. ULID should be 26 characters long.");
        }

        try
        {
            ReadOnlySpan<byte> bytes = Base32.Decode(ulidString);
            long timestamp = ExtractTimestamp(bytes);
            if (timestamp < 0)
            {
                throw new ArgumentException("Invalid ULID string.");
            }

            return new Ulid(ulidString);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid ULID string format.");
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Invalid ULID string.");
        }
    }
}
