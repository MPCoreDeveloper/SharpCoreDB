// <copyright file="SqlBooleanValue.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Globalization;

/// <summary>
/// SQLite's truth test for a value used as a condition — <c>WHERE is_active</c>, <c>WHERE flag</c>,
/// <c>CASE WHEN …</c>. It is the rule both WHERE evaluators need when the predicate is a single column
/// reference, which is what linq2db's SQLite dialect emits for a boolean property (<c>WHERE [u].[is_active]</c>).
/// <para>
/// The rule is SQLite's: NULL is false, a number is true when it is not zero, and text is converted to a number
/// first (<c>'1'</c> is true, <c>'abc'</c> is false).
/// </para>
/// </summary>
internal static class SqlBooleanValue
{
    /// <summary>
    /// Evaluates a stored value as a SQLite condition.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns><c>true</c> when SQLite would treat the value as true.</returns>
    internal static bool IsTrue(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return false;
            case bool flag:
                return flag;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            case float single:
                return single != 0f;
            case double number:
                return number != 0d;
            case decimal money:
                return money != 0m;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed != 0d;
            case byte[] bytes:
                return bytes.Length > 0;
            default:
                return true;
        }
    }
}
