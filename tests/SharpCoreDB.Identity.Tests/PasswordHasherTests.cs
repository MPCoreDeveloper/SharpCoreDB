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
    private static SharpCoreDbPasswordHasher CreateHasher() => new(new SharpCoreIdentityOptions());

    [Fact]
    public void HashPassword_ShouldGenerateDifferentHashesForSamePassword()
    {
        // Arrange
        var hasher = CreateHasher();
        const string password = "SecurePassword123!";

        // Act
        var hash1 = hasher.HashPassword(password);
        var hash2 = hasher.HashPassword(password);

        // Assert
        Assert.NotEqual(hash1, hash2); // Different salts should produce different hashes
    }

    [Fact]
    public void VerifyHashedPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var hasher = CreateHasher();
        const string password = "MySecurePassword456!";
        var hash = hasher.HashPassword(password);

        // Act
        var isValid = hasher.VerifyHashedPassword(hash, password);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyHashedPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        var hasher = CreateHasher();
        const string password = "CorrectPassword789!";
        const string wrongPassword = "WrongPassword123!";
        var hash = hasher.HashPassword(password);

        // Act
        var isValid = hasher.VerifyHashedPassword(hash, wrongPassword);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void HashPassword_WithEmptyPassword_ShouldThrow()
    {
        // Arrange
        var hasher = CreateHasher();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => hasher.HashPassword(string.Empty));
    }

    [Fact]
    public void HashPassword_WithNullPassword_ShouldThrow()
    {
        // Arrange
        var hasher = CreateHasher();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => hasher.HashPassword(null!));
    }

    [Fact]
    public void VerifyHashedPassword_WithMalformedHash_ShouldReturnFalse()
    {
        // Arrange
        var hasher = CreateHasher();
        const string malformedHash = "not-a-valid-hash";
        const string password = "SomePassword12345!";

        // Act
        var isValid = hasher.VerifyHashedPassword(malformedHash, password);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData("ShortPassword123!")]
    [InlineData("MediumPassword123!")]
    [InlineData("VeryLongPasswordWithManyCharacters123456789!@#$%^&*()")]
    public void HashPassword_WithVariousLengths_ShouldSucceed(string password)
    {
        // Arrange
        var hasher = CreateHasher();

        // Act
        var hash = hasher.HashPassword(password);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(hasher.VerifyHashedPassword(hash, password));
    }

    [Fact]
    public void HashPassword_WithUnicodeCharacters_ShouldSucceed()
    {
        // Arrange
        var hasher = CreateHasher();
        const string password = "Пароль123!中文密码";

        // Act
        var hash = hasher.HashPassword(password);
        var isValid = hasher.VerifyHashedPassword(hash, password);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void HashPassword_ShouldBeResistantToTimingAttacks()
    {
        // Arrange - Create isolated hasher instance for this test
        var fastOptions = new SharpCoreIdentityOptions
        {
            Password = new SharpCorePasswordOptions
            {
                IterationCount = 100_000, // Minimum secure threshold for testing
                SaltSize = 16,
                HashSize = 32,
                Algorithm = SharpCorePbkdf2Algorithm.Sha256
            }
        };
        var testHasher = new SharpCoreDbPasswordHasher(fastOptions);

        const string password = "TimingTestPassword123!";
        var hash = testHasher.HashPassword(password);
        const int warmupIterations = 10;
        const int iterations = 200;

        // Warmup - eliminate JIT compilation effects
        for (int i = 0; i < warmupIterations; i++)
        {
            testHasher.VerifyHashedPassword(hash, password);
            testHasher.VerifyHashedPassword(hash, "WrongPassword123!_Same123!");
        }

        // Force GC to minimize interference during actual measurements
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

        // Act - Measure verification time for correct password
        var correctTimes = new List<long>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            testHasher.VerifyHashedPassword(hash, password);
            sw.Stop();
            correctTimes.Add(sw.ElapsedTicks);
        }

        // Act - Measure verification time for incorrect password (same length)
        var incorrectTimes = new List<long>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            testHasher.VerifyHashedPassword(hash, "WrongPassword123!_Same123!");
            sw.Stop();
            incorrectTimes.Add(sw.ElapsedTicks);
        }

        // Assert - Use trimmed median to reduce impact of outliers
        // Remove top and bottom 10% of samples to account for GC pauses and CPU scheduling
        var trimmedCorrect = TrimOutliers(correctTimes, 0.10);
        var trimmedIncorrect = TrimOutliers(incorrectTimes, 0.10);

        var medianCorrect = GetMedian(trimmedCorrect);
        var medianIncorrect = GetMedian(trimmedIncorrect);
        var ratio = Math.Max(medianCorrect, medianIncorrect) / Math.Min(medianCorrect, medianIncorrect);

        // Use 2.5x threshold to account for system variance while still detecting real timing attacks
        Assert.True(ratio < 2.5, $"Timing difference too large (ratio: {ratio:F2}), potential timing attack vulnerability. " +
            $"Median correct: {medianCorrect:F0} ticks, Median incorrect: {medianIncorrect:F0} ticks");
    }

    private static List<long> TrimOutliers(List<long> values, double trimPercentage)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var trimCount = (int)(sorted.Count * trimPercentage);
        return sorted.Skip(trimCount).Take(sorted.Count - (2 * trimCount)).ToList();
    }

    private static double GetMedian(List<long> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int count = sorted.Count;
        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        else
        {
            return sorted[count / 2];
        }
    }
}



