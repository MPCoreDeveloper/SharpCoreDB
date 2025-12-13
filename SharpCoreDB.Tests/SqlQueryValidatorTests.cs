// <copyright file="SqlQueryValidatorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;
using SharpCoreDB.Services;
using System.Collections.Generic;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for SqlQueryValidator parameter validation.
/// </summary>
public class SqlQueryValidatorTests
{
    [Fact]
    public void ValidateQuery_NamedParameters_Correct_NoWarning()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name, email) VALUES (@id, @name, @email)";
        var parameters = new Dictionary<string, object?>
        {
            { "id", 1 },
            { "name", "Alice" },
            { "email", "alice@test.com" }
        };

        // Act & Assert - should not throw
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Lenient, strictParameterValidation: true);
    }

    [Fact]
    public void ValidateQuery_NamedParameters_MissingKey_WarnsInLenientMode()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "id", 1 }
            // Missing "name" key!
        };

        // Act & Assert - in lenient mode, should write warning to console but not throw
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Lenient, strictParameterValidation: true);
        // Warning: "Missing parameters for placeholders: @name"
    }

    [Fact]
    public void ValidateQuery_NamedParameters_MissingKey_ThrowsInStrictMode()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "id", 1 }
            // Missing "name" key!
        };

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Strict, strictParameterValidation: true));
        
        Assert.Contains("Missing parameters for placeholders: @name", ex.Message);
    }

    [Fact]
    public void ValidateQuery_NamedParameters_WrongKey_DetectedWithStrictValidation()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "user_id", 1 },      // Wrong key! Should be "id"
            { "username", "Alice" } // Wrong key! Should be "name"
        };

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Strict, strictParameterValidation: true));
        
        Assert.Contains("Missing parameters for placeholders", ex.Message);
    }

    [Fact]
    public void ValidateQuery_NamedParameters_WrongKey_NoWarningWithoutStrictValidation()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "user_id", 1 },
            { "username", "Alice" }
        };

        // Act & Assert - should NOT throw when strict validation disabled
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Lenient, strictParameterValidation: false);
        // No warning because strict validation is off
    }

    [Fact]
    public void ValidateQuery_PositionalParameters_Correct_NoWarning()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (?, ?)";
        var parameters = new Dictionary<string, object?>
        {
            { "0", 1 },
            { "1", "Alice" }
        };

        // Act & Assert
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Lenient, strictParameterValidation: true);
    }

    [Fact]
    public void ValidateQuery_PositionalParameters_CountMismatch_Warns()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name, email) VALUES (?, ?, ?)";
        var parameters = new Dictionary<string, object?>
        {
            { "0", 1 },
            { "1", "Alice" }
            // Missing third parameter!
        };

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Strict));
        
        Assert.Contains("Parameter count mismatch", ex.Message);
    }

    [Fact]
    public void ValidateQuery_MixedParameterStyles_Warns()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (?, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "0", 1 },
            { "name", "Alice" }
        };

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Strict));
        
        Assert.Contains("Mixed parameter styles detected", ex.Message);
    }

    [Fact]
    public void ValidateQuery_Disabled_NoValidation()
    {
        // Arrange
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";
        var parameters = new Dictionary<string, object?>
        {
            { "wrong_key", 1 }  // Wrong key, but should be ignored
        };

        // Act & Assert - should not throw or warn
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Disabled, strictParameterValidation: true);
    }

    [Fact]
    public void ValidateQuery_UnusedParameters_WarnsIfExcessive()
    {
        // Arrange
        var sql = "INSERT INTO users (id) VALUES (@id)";
        var parameters = new Dictionary<string, object?>
        {
            { "id", 1 },
            { "name", "Alice" },    // Unused
            { "email", "test" },    // Unused
            { "age", 25 }           // Unused
        };

        // Act & Assert - warns because unused count >= used count
        SqlQueryValidator.ValidateQuery(sql, parameters, SqlQueryValidator.ValidationMode.Lenient, strictParameterValidation: true);
        // Warning: "Unused parameters provided (not in SQL): name, email, age"
    }

    [Fact]
    public void ValidateQuery_SafeStatement_NoWarning()
    {
        // Arrange
        var sql = "CREATE TABLE users (id INTEGER, name TEXT)";

        // Act & Assert - CREATE TABLE is safe, no warning about string literals
        SqlQueryValidator.ValidateQuery(sql, null, SqlQueryValidator.ValidationMode.Strict, strictParameterValidation: true);
    }

    [Fact]
    public void ValidateQuery_SQLInjectionPattern_Detected()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = 'admin' OR '1'='1'";

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql, null, SqlQueryValidator.ValidationMode.Strict));
        
        Assert.Contains("dangerous SQL pattern", ex.Message);
    }
}
