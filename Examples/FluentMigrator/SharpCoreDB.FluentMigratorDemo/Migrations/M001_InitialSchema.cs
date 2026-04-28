// <copyright file="M001_InitialSchema.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using FluentMigrator;

namespace SharpCoreDB.FluentMigratorDemo;

/// <summary>
/// Migration 1: Creates the initial Products and Categories schema.
/// FluentMigrator generates quoted identifiers (e.g. "Products") which
/// SharpCoreDB's single-file parser handles correctly as of v1.7.2.
/// </summary>
[Migration(1, "Initial schema: Products and Categories")]
public sealed class M001_InitialSchema : global::FluentMigrator.Migration
{
    /// <inheritdoc />
    public override void Up()
    {
        Create.Table("Categories")
            .WithColumn("Id").AsInt64().PrimaryKey().NotNullable()
            .WithColumn("Name").AsString(100).NotNullable()
            .WithColumn("CreatedAt").AsString(30).NotNullable();

        Create.Table("Products")
            .WithColumn("Id").AsInt64().PrimaryKey().NotNullable()
            .WithColumn("CategoryId").AsInt64().NotNullable()
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Price").AsDecimal(18, 2).NotNullable()
            .WithColumn("Stock").AsInt32().NotNullable()
            .WithColumn("CreatedAt").AsString(30).NotNullable();
    }

    /// <inheritdoc />
    public override void Down()
    {
        Delete.Table("Products");
        Delete.Table("Categories");
    }
}
