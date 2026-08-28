// <copyright file="JsonSerializerContext.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using SharpCoreDB.DataStructures;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON serializer context for SharpCoreDB to support Native AOT compilation and source generation features in .NET 5+.
/// Required for .NET 10 when reflection-based serialization is disabled.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<Dictionary<string, object>>))]
[JsonSerializable(typeof(Table))]
[JsonSerializable(typeof(List<Table>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(List<object?>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(Dictionary<int, long>))]
[JsonSerializable(typeof(DataType))]
[JsonSerializable(typeof(List<DataType>))]
[JsonSerializable(typeof(SharpCoreDB.Storage.Hybrid.StorageMode))]
[JsonSerializable(typeof(CollationType))]
[JsonSerializable(typeof(List<CollationType>))]
[JsonSerializable(typeof(ForeignKeyConstraint))]
[JsonSerializable(typeof(List<ForeignKeyConstraint>))]
[JsonSerializable(typeof(TableMetadataDto))]
[JsonSerializable(typeof(List<TableMetadataDto>))]
public partial class SharpCoreDBJsonContext : JsonSerializerContext
{
}
