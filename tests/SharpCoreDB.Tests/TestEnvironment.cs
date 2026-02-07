// <copyright file="TestEnvironment.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

/// <summary>
/// Provides environment detection and configuration for tests.
/// Used to adjust test behavior based on execution environment (local vs CI).
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Gets a value indicating whether tests are running in a CI environment.
    /// Detects GitHub Actions, Azure Pipelines, and other common CI systems.
    /// </summary>
    public static bool IsCI =>
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ||
        Environment.GetEnvironmentVariable("TF_BUILD") == "true" ||
        Environment.GetEnvironmentVariable("JENKINS_URL") != null ||
        Environment.GetEnvironmentVariable("CIRCLECI") == "true" ||
        Environment.GetEnvironmentVariable("TRAVIS") == "true";

    /// <summary>
    /// Gets the appropriate timeout value based on execution environment.
    /// CI environments get longer timeouts due to variable performance.
    /// </summary>
    /// <param name="localMs">Timeout in milliseconds for local execution.</param>
    /// <param name="ciMs">Timeout in milliseconds for CI execution.</param>
    /// <returns>The appropriate timeout value.</returns>
    public static int GetPerformanceTimeout(int localMs, int ciMs) =>
        IsCI ? ciMs : localMs;

    /// <summary>
    /// Gets the appropriate timeout value with a multiplier for CI.
    /// </summary>
    /// <param name="baseMs">Base timeout in milliseconds.</param>
    /// <param name="ciMultiplier">Multiplier for CI (default 10x).</param>
    /// <returns>The appropriate timeout value.</returns>
    public static int GetTimeout(int baseMs, int ciMultiplier = 10) =>
        IsCI ? baseMs * ciMultiplier : baseMs;

    /// <summary>
    /// Waits for file handles to be released before cleanup.
    /// Longer wait in CI due to slower I/O.
    /// </summary>
    public static void WaitForFileRelease()
    {
        int waitMs = IsCI ? 500 : 100;
        System.Threading.Thread.Sleep(waitMs);
    }

    /// <summary>
    /// Attempts to delete a directory with retries.
    /// </summary>
    /// <param name="path">The directory path to delete.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    public static void CleanupWithRetry(string path, int maxRetries = 3)
    {
        if (!Directory.Exists(path))
            return;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                System.Threading.Thread.Sleep(100 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < maxRetries - 1)
            {
                System.Threading.Thread.Sleep(100 * (i + 1));
            }
        }
    }

    /// <summary>
    /// Gets the environment description for logging.
    /// </summary>
    public static string GetEnvironmentDescription() =>
        IsCI ? "CI Environment" : "Local Development";

    /// <summary>
    /// Asserts that elapsed time is within the given threshold, applying a CI multiplier.
    /// On slow Ubuntu CI runners, timing thresholds are relaxed by <paramref name="ciMultiplier"/>.
    /// </summary>
    /// <param name="elapsedMs">Actual elapsed milliseconds.</param>
    /// <param name="localMaxMs">Maximum allowed milliseconds for local execution.</param>
    /// <param name="ciMultiplier">Multiplier for CI (default 10x for slow Ubuntu runners).</param>
    /// <param name="label">Optional label for the assertion message.</param>
    public static void AssertPerformance(long elapsedMs, int localMaxMs, int ciMultiplier = 10, string? label = null)
    {
        var maxMs = GetTimeout(localMaxMs, ciMultiplier);
        var env = GetEnvironmentDescription();
        var prefix = label is not null ? $"{label}: " : "";
        Assert.True(
            elapsedMs < maxMs,
            $"{prefix}Expected < {maxMs}ms ({env}, base={localMaxMs}ms), got {elapsedMs}ms");
    }
}
