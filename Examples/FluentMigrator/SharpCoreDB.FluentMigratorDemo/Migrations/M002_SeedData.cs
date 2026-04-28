// <copyright file="M002_SeedData.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using FluentMigrator;

namespace SharpCoreDB.FluentMigratorDemo;

/// <summary>
/// Migration 2: Seeds initial Categories and Products data.
/// Demonstrates that INSERT with quoted identifiers works correctly
/// in SharpCoreDB's single-file embedded mode.
/// </summary>
[Migration(2, "Seed: initial categories and products")]
public sealed class M002_SeedData : global::FluentMigrator.Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        var now = DateTime.UtcNow.ToString("o");

        Insert.IntoTable("Categories").Row(new { Id = 1, Name = "Electronics", CreatedAt = now });
        Insert.IntoTable("Categories").Row(new { Id = 2, Name = "Books", CreatedAt = now });

        Insert.IntoTable("Products").Row(new { Id = 1, CategoryId = 1, Name = "Laptop Pro 15", Price = 1299.99m, Stock = 25, CreatedAt = now });
        Insert.IntoTable("Products").Row(new { Id = 2, CategoryId = 1, Name = "Wireless Mouse", Price = 49.99m, Stock = 100, CreatedAt = now });
        Insert.IntoTable("Products").Row(new { Id = 3, CategoryId = 2, Name = "Clean Code", Price = 34.95m, Stock = 50, CreatedAt = now });
    }

    /// <inheritdoc />
    public override void Down()
    {
        Delete.FromTable("Products").AllRows();
        Delete.FromTable("Categories").AllRows();
    }
}
