// <copyright file="SharpCoreDBMigrationsSqlGeneratorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Tests.Migrations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

public class SharpCoreDBMigrationsSqlGeneratorTests
{
    private sealed class MigrationDbContext : DbContext
    {
        public DbSet<UserRow> Users => Set<UserRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSharpCoreDB("Data Source=ef_migrations_test.db;Password=TestPassword123");
        }
    }

    private sealed class UserRow
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public int Version { get; set; }
    }

    [Fact]
    public void Generate_WithInsertDataOperation_UsesInsertOrReplace()
    {
        // Arrange
        using var context = new MigrationDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var operation = new InsertDataOperation
        {
            Table = "Users",
            Columns = ["Id", "Score"],
            Values = new object[,] { { 1, 10 } },
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("INSERT OR REPLACE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Score", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithMultiRowInsertDataOperation_EmitsMultipleValueTuples()
    {
        // Arrange
        using var context = new MigrationDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var operation = new InsertDataOperation
        {
            Table = "Users",
            Columns = ["Id", "Score", "Version"],
            Values = new object[,] { { 1, 10, 1 }, { 2, 20, 1 } },
        };

        // Act
        var commands = generator.Generate([operation], context.Model);
        var sql = string.Join("\n", commands.Select(c => c.CommandText));

        // Assert
        Assert.Contains("INSERT OR REPLACE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(1, 10, 1), (2, 20, 1)", sql, StringComparison.OrdinalIgnoreCase);
    }
}
