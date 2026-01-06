// <copyright file="EnumToIntConverter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SharpCoreDB.Viewer.Converters;

/// <summary>
/// Converts between enum values and integer indices for ComboBox binding.
/// </summary>
public class EnumToIntConverter : IValueConverter
{
    public static readonly EnumToIntConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return System.Convert.ToInt32(enumValue);
        }
        
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && targetType.IsEnum)
        {
            return Enum.ToObject(targetType, intValue);
        }
        
        return Enum.ToObject(targetType, 0);
    }
}
