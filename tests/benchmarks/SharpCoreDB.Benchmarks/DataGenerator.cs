// <copyright file="DataGenerator.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using System.Text;
using System.Text.Json;

/// <summary>
/// Utility for generating realistic test data for benchmarks.
/// </summary>
public static class DataGenerator
{
    private static readonly Random _random = new(42); // Fixed seed for reproducibility
    private static readonly string[] _firstNames = ["James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda", "William", "Barbara"];
    private static readonly string[] _lastNames = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez"];
    private static readonly string[] _tags = ["important", "archived", "draft", "published", "reviewed", "pending", "approved", "rejected"];

    /// <summary>
    /// Generates a random document with realistic data.
    /// </summary>
    public static Document GenerateDocument(int? id = null)
    {
        var firstName = _firstNames[_random.Next(_firstNames.Length)];
        var lastName = _lastNames[_random.Next(_lastNames.Length)];
        var name = $"{firstName} {lastName}";
        var email = $"{firstName.ToLower()}.{lastName.ToLower()}@example.com";
        var age = _random.Next(18, 101);
        var score = _random.NextDouble() * 100;
        var isActive = _random.NextDouble() < 0.7; // 70% active
        var tagCount = _random.Next(1, 4);
        var tags = new List<string>();
        for (int i = 0; i < tagCount; i++)
        {
            var tag = _tags[_random.Next(_tags.Length)];
            if (!tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        var metadata = new Dictionary<string, object>
        {
            ["department"] = _random.Next(1, 11),
            ["level"] = _random.Next(1, 6),
            ["hired_year"] = _random.Next(2010, 2026)
        };

        return new Document
        {
            Id = id,
            Name = name,
            Email = email,
            Age = age,
            Score = score,
            Tags = tags,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = isActive,
            Metadata = JsonSerializer.Serialize(metadata)
        };
    }

    /// <summary>
    /// Generates a batch of documents.
    /// </summary>
    public static List<Document> GenerateBatch(int count, int? startId = null)
    {
        var documents = new List<Document>(count);
        for (int i = 0; i < count; i++)
        {
            var id = startId.HasValue ? startId.Value + i : (int?)null;
            documents.Add(GenerateDocument(id));
        }
        return documents;
    }

    /// <summary>
    /// Generates a random string of specified length.
    /// </summary>
    public static string RandomString(int minLength, int maxLength)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var length = _random.Next(minLength, maxLength + 1);
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(chars[_random.Next(chars.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates random vector data for Zvec benchmarks.
    /// </summary>
    public static float[] GenerateVector(int dimensions)
    {
        var vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(_random.NextDouble() * 2 - 1); // Range: -1 to 1
        }
        return vector;
    }
}

/// <summary>
/// Document model for BLite benchmarks.
/// </summary>
public class Document
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public double Score { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Converts document to SQL INSERT statement.
    /// </summary>
    public string ToInsertSQL(string tableName = "documents")
    {
        var idValue = Id.HasValue ? Id.Value.ToString() : "NULL";
        var tagsJson = JsonSerializer.Serialize(Tags);
        return $"INSERT INTO {tableName} (id, name, email, age, score, tags, created_at, updated_at, is_active, metadata) " +
               $"VALUES ({idValue}, '{Name}', '{Email}', {Age}, {Score}, '{tagsJson}', '{CreatedAt:yyyy-MM-dd HH:mm:ss}', " +
               $"'{UpdatedAt:yyyy-MM-dd HH:mm:ss}', {(IsActive ? 1 : 0)}, '{Metadata}')";
    }

    /// <summary>
    /// Converts document to SQL UPDATE statement.
    /// </summary>
    public string ToUpdateSQL(string tableName = "documents")
    {
        if (!Id.HasValue)
        {
            throw new InvalidOperationException("Cannot update document without ID");
        }

        var tagsJson = JsonSerializer.Serialize(Tags);
        return $"UPDATE {tableName} SET name='{Name}', email='{Email}', age={Age}, score={Score}, " +
               $"tags='{tagsJson}', updated_at='{UpdatedAt:yyyy-MM-dd HH:mm:ss}', is_active={(IsActive ? 1 : 0)}, " +
               $"metadata='{Metadata}' WHERE id={Id.Value}";
    }
}
