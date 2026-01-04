// <copyright file="Database.Migration.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Migration;

/// <summary>
/// Database migration methods.
/// Provides instance methods for migrating the current database format.
/// NOTE: These methods require the master password which is not stored in the Database class.
/// Users must provide the password when calling migration methods.
/// </summary>
public partial class Database
{
    /// <summary>
    /// Migrates the current database to single-file (.scdb) format.
    /// This method creates a new .scdb file with the same data.
    /// The original database remains unchanged.
    /// </summary>
    /// <param name="targetScdbPath">Target .scdb file path</param>
    /// <param name="masterPassword">Master password for the database</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration result</returns>
    /// <exception cref="InvalidOperationException">If database is already in single-file format</exception>
    public async Task<MigrationResult> MigrateToSingleFileAsync(
        string targetScdbPath,
        string masterPassword,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check current format
        if (Directory.Exists(_dbPath))
        {
            // Database is in directory format, can migrate
            return await DatabaseMigrator.MigrateToSingleFileAsync(
                _dbPath,
                targetScdbPath,
                masterPassword,
                null, // Use default options
                progress,
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                "Database is already in single-file format. Use MigrateToDirectoryAsync to convert back.");
        }
    }

    /// <summary>
    /// Migrates the current database to directory-based format.
    /// This method creates a new directory with the same data.
    /// The original database remains unchanged.
    /// </summary>
    /// <param name="targetDirectoryPath">Target directory path</param>
    /// <param name="masterPassword">Master password for the database</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration result</returns>
    /// <exception cref="InvalidOperationException">If database is already in directory format</exception>
    public async Task<MigrationResult> MigrateToDirectoryAsync(
        string targetDirectoryPath,
        string masterPassword,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check current format
        if (!Directory.Exists(_dbPath))
        {
            // Database is in single-file format, can migrate
            return await DatabaseMigrator.MigrateToDirectoryAsync(
                _dbPath,
                targetDirectoryPath,
                masterPassword,
                null, // Use default options
                progress,
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                "Database is already in directory format. Use MigrateToSingleFileAsync to convert.");
        }
    }

    /// <summary>
    /// Validates that another database has identical content to this one.
    /// Useful for verifying migration success.
    /// </summary>
    /// <param name="otherDatabasePath">Path to other database</param>
    /// <param name="masterPassword">Master password for the database</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    public async Task<ValidationResult> ValidateAgainstAsync(
        string otherDatabasePath,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await DatabaseMigrator.ValidateMigrationAsync(
            _dbPath,
            otherDatabasePath,
            masterPassword,
            cancellationToken);
    }

    /// <summary>
    /// Gets the current storage mode of the database.
    /// </summary>
    public StorageMode CurrentStorageMode => Directory.Exists(_dbPath) 
        ? StorageMode.Directory 
        : StorageMode.SingleFile;
}
