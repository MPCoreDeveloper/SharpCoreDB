// <copyright file="CryptoConstants.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Constants;

/// <summary>
/// Constants for cryptographic operations.
/// Extracted from magic numbers to improve maintainability and security.
/// </summary>
public static class CryptoConstants
{
    /// <summary>
    /// AES-GCM nonce size in bytes (96 bits as per NIST SP 800-38D).
    /// </summary>
    public const int GCM_NONCE_SIZE = 12;

    /// <summary>
    /// AES-GCM authentication tag size in bytes (128 bits).
    /// </summary>
    public const int GCM_TAG_SIZE = 16;

    /// <summary>
    /// AES-256 key size in bytes (256 bits).
    /// </summary>
    public const int AES_KEY_SIZE = 32;

    /// <summary>
    /// PBKDF2 iteration count (600,000 as per OWASP 2024 recommendations).
    /// See: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
    /// </summary>
    public const int PBKDF2_ITERATIONS = 600000;

    /// <summary>
    /// Database-specific salt size in bytes (256 bits for high security).
    /// </summary>
    public const int DATABASE_SALT_SIZE = 32;

    /// <summary>
    /// Maximum number of GCM encryptions per key before rotation required (2^32 - safety margin).
    /// AES-GCM is limited to 2^32 operations per key to prevent nonce collision.
    /// </summary>
    public const long MAX_GCM_OPERATIONS = (1L << 32) - 10000; // 4,294,957,296 operations

    /// <summary>
    /// Warning threshold for GCM operations (90% of max).
    /// </summary>
    public const long GCM_OPERATIONS_WARNING_THRESHOLD = (long)(MAX_GCM_OPERATIONS * 0.9);
}
