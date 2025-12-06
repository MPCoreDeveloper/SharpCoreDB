// <copyright file="DataTypes.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
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
}
