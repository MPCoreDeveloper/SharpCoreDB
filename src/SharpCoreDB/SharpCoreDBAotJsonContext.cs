// <copyright file="SharpCoreDBAotJsonContext.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using SharpCoreDB.Migration;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using SharpCoreDB.Storage.Overflow;
using System.Text.Json.Serialization;

/// <summary>
/// Source-generated metadata for the types the core serializes that cannot live on
/// <see cref="SharpCoreDBJsonContext"/>: the generator emits one <em>public</em> property per declared type,
/// so declaring an internal type there fails with CS0053 (a public property whose type is internal). Keeping
/// them in a separate internal context is also what leaves the public context's API surface untouched.
/// <para>
/// Where reflection is disabled this context is combined with <see cref="SharpCoreDBJsonContext"/> by
/// <see cref="AotJsonSerializer"/>, so a call site serializing any of these types still resolves metadata
/// without reflection.
/// </para>
/// </summary>
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(FilePointer))]
[JsonSerializable(typeof(MigrationCheckpoint))]
[JsonSerializable(typeof(UserCredentials))]
[JsonSerializable(typeof(Dictionary<string, UserCredentials>))]
[JsonSerializable(typeof(Dictionary<string, TableMetadataExtended>))]
internal sealed partial class SharpCoreDBAotJsonContext : JsonSerializerContext
{
}
