// <copyright file="DataTypes.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

/// <summary>
/// Data types supported by SharpCoreDB.
/// </summary>
public enum DataType
{
    /// <summary>32-bit integer type.</summary>
    Integer,

    /// <summary>String type.</summary>
    String,

    /// <summary>Double precision floating point type.</summary>
    Real,

    /// <summary>Binary large object type.</summary>
    Blob,

    /// <summary>Boolean type.</summary>
    Boolean,

    /// <summary>Date and time type.</summary>
    DateTime,

    /// <summary>64-bit integer type.</summary>
    Long,

    /// <summary>Decimal type.</summary>
    Decimal,

    /// <summary>Universally unique lexicographically sortable identifier type.</summary>
    Ulid,

    /// <summary>Globally unique identifier type.</summary>
    Guid,

    /// <summary>Direct row reference type for index-free adjacency.</summary>
    RowRef,

    /// <summary>Vector embedding type (fixed-dimension float32 array for similarity search).</summary>
    Vector,
}
