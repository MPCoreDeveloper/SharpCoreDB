// <copyright file="ParameterExtractorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// ✅ C# 14: Unit tests for ParameterExtractor.
/// Tests parameter extraction, validation, and handling of various SQL patterns.
/// </summary>
public sealed class ParameterExtractorTests
{
    [Fact]
    public void ExtractParameters_WithSingleParameter_ReturnsCorrectInfo()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = @id";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Single(parameters);
        Assert.Equal("id", parameters[0].Name);
        Assert.Equal("@id", parameters[0].FullName);
        Assert.Equal(0, parameters[0].Index);
        Assert.True(parameters[0].Position >= 0);
    }

    [Fact]
    public void ExtractParameters_WithMultipleParameters_ReturnsAllInOrder()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name AND age > @age AND email = @email";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Equal(3, parameters.Length);
        Assert.Equal("name", parameters[0].Name);
        Assert.Equal("age", parameters[1].Name);
        Assert.Equal("email", parameters[2].Name);
        Assert.Equal(0, parameters[0].Index);
        Assert.Equal(1, parameters[1].Index);
        Assert.Equal(2, parameters[2].Index);
    }

    [Fact]
    public void ExtractParameters_WithDuplicateParameters_ReturnUniqueOnly()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = @id OR parent_id = @id";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Single(parameters);
        Assert.Equal("id", parameters[0].Name);
        // Position should be first occurrence
        Assert.True(parameters[0].Position < sql.LastIndexOf("@id"));
    }

    [Fact]
    public void ExtractParameters_WithNoParameters_ReturnsEmptyArray()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = 1";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Empty(parameters);
    }

    [Fact]
    public void ExtractParameters_WithUnderscorePrefixParameter_Recognized()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE _id = @_id";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Single(parameters);
        Assert.Equal("_id", parameters[0].Name);
    }

    [Fact]
    public void ExtractParameters_WithNumberInParameterName_Recognized()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = @user_id123";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Single(parameters);
        Assert.Equal("user_id123", parameters[0].Name);
    }

    [Fact]
    public void HasParameters_WithParameters_ReturnsTrue()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = @id";

        // Act
        var result = ParameterExtractor.HasParameters(sql);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasParameters_WithoutParameters_ReturnsFalse()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE id = 1";

        // Act
        var result = ParameterExtractor.HasParameters(sql);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetParameterCount_WithMultipleParameters_ReturnsCorrectCount()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name AND age > @age";

        // Act
        var count = ParameterExtractor.GetParameterCount(sql);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void GetParameterCount_WithDuplicates_CountsUniqueOnly()
    {
        // Arrange
        var sql = "WHERE id = @id OR parent_id = @id OR manager_id = @id";

        // Act
        var count = ParameterExtractor.GetParameterCount(sql);

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void GetExpectedParameters_ReturnsSetOfParameterNames()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name AND email = @email";

        // Act
        var expected = ParameterExtractor.GetExpectedParameters(sql);

        // Assert
        Assert.Equal(2, expected.Count);
        Assert.Contains("name", expected);
        Assert.Contains("email", expected);
    }

    [Fact]
    public void ValidateParameters_WithAllRequiredParameters_ReturnsValid()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name AND age > @age";
        var parameters = new Dictionary<string, object?> { { "name", "Alice" }, { "age", 30 } };

        // Act
        var (isValid, errorMessage) = ParameterExtractor.ValidateParameters(sql, parameters);

        // Assert
        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void ValidateParameters_WithMissingRequiredParameter_ReturnsInvalid()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name AND age > @age";
        var parameters = new Dictionary<string, object?> { { "name", "Alice" } };  // Missing @age

        // Act
        var (isValid, errorMessage) = ParameterExtractor.ValidateParameters(sql, parameters);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("age", errorMessage);
    }

    [Fact]
    public void ValidateParameters_WithAtSignInProvidedParameters_Recognized()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @name";
        var parameters = new Dictionary<string, object?> { { "@name", "Alice" } };  // With @

        // Act
        var (isValid, errorMessage) = ParameterExtractor.ValidateParameters(sql, parameters);

        // Assert
        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void ValidateParameters_CaseInsensitiveParameterNames()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = @Name";
        var parameters = new Dictionary<string, object?> { { "name", "Alice" } };

        // Act
        var (isValid, errorMessage) = ParameterExtractor.ValidateParameters(sql, parameters);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void AreParametersValid_WithValidNames_ReturnsTrue()
    {
        // Arrange
        var sql = "WHERE id = @id AND name = @user_name AND role_id = @role123";

        // Act
        var result = ParameterExtractor.AreParametersValid(sql);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreParametersValid_WithInvalidStartCharacter_ReturnsFalse()
    {
        // Arrange
        var sql = "WHERE id = @123invalid";  // Starts with digit

        // Act
        var result = ParameterExtractor.AreParametersValid(sql);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ExtractParameters_ComplexQuery_HandlesCorrectly()
    {
        // Arrange
        var sql = @"
            SELECT u.id, u.name, o.order_id
            FROM users u
            JOIN orders o ON u.id = o.user_id
            WHERE u.status = @status
              AND o.total > @min_amount
              AND u.created_date >= @start_date
              AND u.created_date <= @end_date
              AND o.order_id IN (
                  SELECT order_id FROM order_items 
                  WHERE product_id = @product_id
              )";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Equal(5, parameters.Length);
        Assert.Contains(parameters, p => p.Name == "status");
        Assert.Contains(parameters, p => p.Name == "min_amount");
        Assert.Contains(parameters, p => p.Name == "start_date");
        Assert.Contains(parameters, p => p.Name == "end_date");
        Assert.Contains(parameters, p => p.Name == "product_id");
    }

    [Fact]
    public void ExtractParameters_WithStringLiteralsContainingAt_IgnoresThem()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE email = @email AND name = 'user@example.com'";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Single(parameters);
        Assert.Equal("email", parameters[0].Name);
        // Should NOT extract "example" from the string literal
    }

    [Fact]
    public void ExtractParameters_WithNewlinesBetweenParameters_HandlesCorrectly()
    {
        // Arrange
        var sql = @"SELECT *
                   FROM users
                   WHERE name = @name
                     AND age > @age";

        // Act
        var parameters = ParameterExtractor.ExtractParameters(sql);

        // Assert
        Assert.Equal(2, parameters.Length);
    }
}
