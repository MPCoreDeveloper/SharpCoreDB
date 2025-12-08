// <copyright file="TestDataGenerator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Bogus;

namespace SharpCoreDB.Benchmarks.Infrastructure;

/// <summary>
/// Generates consistent test data for benchmarks across all database engines.
/// </summary>
public class TestDataGenerator
{
    private readonly Faker faker = new Faker();
    
    public class UserRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Generates a batch of user records.
    /// </summary>
    public List<UserRecord> GenerateUsers(int count, int startId = 1)
    {
        var users = new List<UserRecord>(count);
        
        for (int i = 0; i < count; i++)
        {
            users.Add(new UserRecord
            {
                Id = startId + i,
                Name = faker.Name.FullName(),
                Email = faker.Internet.Email(),
                Age = faker.Random.Int(18, 80),
                CreatedAt = faker.Date.Past(2),
                IsActive = faker.Random.Bool(0.8f)
            });
        }
        
        return users;
    }

    /// <summary>
    /// Generates a single user record.
    /// </summary>
    public UserRecord GenerateUser(int id)
    {
        return new UserRecord
        {
            Id = id,
            Name = faker.Name.FullName(),
            Email = faker.Internet.Email(),
            Age = faker.Random.Int(18, 80),
            CreatedAt = faker.Date.Past(2),
            IsActive = faker.Random.Bool(0.8f)
        };
    }
}
