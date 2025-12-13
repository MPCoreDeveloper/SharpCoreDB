// <copyright file="PlatformHelper.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Runtime.InteropServices;

namespace SharpCoreDB.Services;

/// <summary>
/// Platform detection and platform-specific utilities.
/// </summary>
public static class PlatformHelper
{
    /// <summary>
    /// Gets a value indicating whether the current platform is Windows.
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Gets a value indicating whether the current platform is Linux.
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Gets a value indicating whether the current platform is macOS.
    /// </summary>
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Gets a value indicating whether the current platform is Android.
    /// </summary>
    public static bool IsAndroid => RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"));

    /// <summary>
    /// Gets a value indicating whether the current platform is iOS.
    /// </summary>
    public static bool IsIOS => RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));

    /// <summary>
    /// Gets a value indicating whether running on a mobile platform.
    /// </summary>
    public static bool IsMobile => IsAndroid || IsIOS;

    /// <summary>
    /// Gets a value indicating whether running on a desktop platform.
    /// </summary>
    public static bool IsDesktop => IsWindows || IsLinux || IsMacOS;

    /// <summary>
    /// Gets the default database directory for the current platform.
    /// </summary>
    /// <returns>Platform-appropriate directory path.</returns>
    public static string GetDefaultDatabaseDirectory()
    {
        if (IsMobile)
        {
            // Mobile: Use application-specific data directory
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        // Desktop: Use roaming application data
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbDir = Path.Combine(baseDir, "SharpCoreDB");
        Directory.CreateDirectory(dbDir);
        return dbDir;
    }

    /// <summary>
    /// Gets the default temp directory for tests.
    /// </summary>
    /// <param name="subDirectory">Optional subdirectory name.</param>
    /// <returns>Platform-appropriate temp directory.</returns>
    public static string GetTempDirectory(string subDirectory = "sharpcoredb_tests")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), subDirectory);
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Gets platform-specific database configuration defaults.
    /// </summary>
    /// <returns>DatabaseConfig with platform-appropriate defaults.</returns>
    public static DatabaseConfig GetPlatformDefaults()
    {
        if (IsMobile)
        {
            // Mobile: Optimize for battery and memory
            return new DatabaseConfig
            {
                BufferPoolSize = 8 * 1024 * 1024,  // 8MB
                WalDurabilityMode = DurabilityMode.Async,
                EnableQueryCache = true,
                QueryCacheSize = 100,  // Smaller cache
            };
        }
        else // Desktop
        {
            // Desktop: Optimize for performance
            return new DatabaseConfig
            {
                BufferPoolSize = 64 * 1024 * 1024,  // 64MB
                WalDurabilityMode = DurabilityMode.FullSync,
                EnableQueryCache = true,
                QueryCacheSize = 1000,
            };
        }
    }

    /// <summary>
    /// Gets a human-readable platform description.
    /// </summary>
    /// <returns>Platform description string.</returns>
    public static string GetPlatformDescription()
    {
        var os = IsWindows ? "Windows" :
                 IsLinux ? "Linux" :
                 IsMacOS ? "macOS" :
                 IsAndroid ? "Android" :
                 IsIOS ? "iOS" : "Unknown";

        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        var framework = RuntimeInformation.FrameworkDescription;

        return $"{os} ({arch}) - {framework}";
    }
}
