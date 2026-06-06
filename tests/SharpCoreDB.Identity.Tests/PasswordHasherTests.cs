// <copyright file="PasswordHasherTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Identity.Tests;

using SharpCoreDB.Identity.Options;
using SharpCoreDB.Identity.Security;

/// <summary>
/// Tests for <see cref="SharpCoreDbPasswordHasher"/> cryptographic functionality.
/// </summary>
public sealed class PasswordHasherTests
{
    private readonly SharpCoreDbPasswordHasher _hasher = new(new SharpCoreIdentityOptions());

    [Fact]
    public void HashPassword_ShouldGenerateDifferentHashesForSamePassword()
    {
        // Arrange
        const string password = "SecurePassword123!";

        // Act
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        // Assert
        Assert.NotEqual(hash1, hash2); // Different salts should produce different hashes
    }

    [Fact]
    public void VerifyHashedPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        const string password = "MySecurePassword456!";
        var hash = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.VerifyHashedPassword(hash, password);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyHashedPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        const string password = "CorrectPassword789!";
        const string wrongPassword = "WrongPassword123!";
        var hash = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.VerifyHashedPassword(hash, wrongPassword);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void HashPassword_WithEmptyPassword_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(string.Empty));
    }

    [Fact]
    public void HashPassword_WithNullPassword_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _hasher.HashPassword(null!));
    }

    [Fact]
    public void VerifyHashedPassword_WithMalformedHash_ShouldReturnFalse()
    {
        // Arrange
        const string malformedHash = "not-a-valid-hash";
        const string password = "SomePassword12345!";

        // Act
        var isValid = _hasher.VerifyHashedPassword(malformedHash, password);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData("ShortPassword123!")]
    [InlineData("MediumPassword123!")]
    [InlineData("VeryLongPasswordWithManyCharacters123456789!@#$%^&*()")]
    public void HashPassword_WithVariousLengths_ShouldSucceed(string password)
    {
        // Act
        var hash = _hasher.HashPassword(password);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(_hasher.VerifyHashedPassword(hash, password));
    }

    [Fact]
    public void HashPassword_WithUnicodeCharacters_ShouldSucceed()
    {
        // Arrange
        const string password = "Пароль123!中文密码";

        // Act
        var hash = _hasher.HashPassword(password);
        var isValid = _hasher.VerifyHashedPassword(hash, password);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HashPassword_ShouldBeResistantToTimingAttacks()
    {
        // Arrange
        const string password = "TimingTestPassword123!";
        var hash = _hasher.HashPassword(password);
        const int iterations = 100;

        // Act - Measure verification time for correct password
        var correctTimes = new List<long>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _hasher.VerifyHashedPassword(hash, password);
            sw.Stop();
            correctTimes.Add(sw.ElapsedTicks);
        }

        // Act - Measure verification time for incorrect password (same length)
        var incorrectTimes = new List<long>();
        for (int i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _hasher.VerifyHashedPassword(hash, "WrongPassword123!_Same123!");
            sw.Stop();
            incorrectTimes.Add(sw.ElapsedTicks);
        }

        // Assert - Times should be similar (within 2x variance) to prevent timing attacks
        var avgCorrect = correctTimes.Average();
        var avgIncorrect = incorrectTimes.Average();
        var ratio = Math.Max(avgCorrect, avgIncorrect) / Math.Min(avgCorrect, avgIncorrect);

        Assert.True(ratio < 2.0, $"Timing difference too large (ratio: {ratio}), potential timing attack vulnerability");
    }
}



