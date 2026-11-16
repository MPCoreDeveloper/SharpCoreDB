// <copyright file="SqlDateTimeFunctions.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// SQLite's date and time functions — <c>date</c>, <c>time</c>, <c>datetime</c>, <c>julianday</c>,
/// <c>unixepoch</c> and <c>strftime</c> — over evaluated argument <em>values</em>, so they work on a column as well
/// as on a literal.
/// <para>
/// Up to 2.1.4 only a SELECT without FROM evaluated <c>julianday</c>, <c>unixepoch</c> and <c>strftime</c>, from
/// the raw argument text; <c>date</c>, <c>time</c> and <c>datetime</c> did not exist, a <c>±HH:MM</c> suffix was not
/// applied, and an unreadable date threw instead of giving NULL. Semantics follow
/// https://sqlite.org/lang_datefunc.html: a time value is a time string, <c>now</c>, or a number read as a Julian
/// day (or as Unix seconds after the <c>unixepoch</c> modifier); a time string without a zone is UTC; an invalid
/// value or modifier yields NULL; and adding months or years keeps the day number and lets it overflow
/// (<c>2026-01-31 +1 month</c> is <c>2026-03-03</c>).
/// </para>
/// </summary>
internal static class SqlDateTimeFunctions
{
    private const double UnixEpochJulianDay = 2440587.5;

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Regex TimeStringPattern = new(
        @"^(?:(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}))?(?:(?:^|[ T])(?<h>\d{2}):(?<mi>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d+))?)?)?\s*(?<z>[Zz]|[+-]\d{2}:\d{2})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ArithmeticModifierPattern = new(
        @"^(?<n>[+-]?\d+(?:\.\d+)?)\s+(?<unit>day|hour|minute|second|month|year)s?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>Returns <c>true</c> for the names this class evaluates.</summary>
    internal static bool IsDateTimeFunction(string upperName) => upperName is
        "DATE" or "TIME" or "DATETIME" or "JULIANDAY" or "UNIXEPOCH" or "STRFTIME";

    /// <summary>
    /// Evaluates one of the date/time functions. Returns NULL (<c>null</c>) when SQLite does: a missing or unreadable
    /// time value, an unknown modifier, or an unknown <c>strftime</c> conversion.
    /// </summary>
    /// <param name="upperName">The upper-case function name.</param>
    /// <param name="args">The evaluated arguments.</param>
    internal static object? Evaluate(string upperName, ReadOnlySpan<object?> args)
    {
        string? format = null;
        if (upperName == "STRFTIME")
        {
            if (args.Length < 2 || args[0] is null)
            {
                return null;
            }

            format = SqlScalarExpression.ToText(args[0]);
            args = args[1..];
        }

        if (!TryResolve(args, out var value))
        {
            return null;
        }

        return upperName switch
        {
            "DATE" => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "TIME" => value.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "DATETIME" => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "JULIANDAY" => ToJulianDay(value),
            "UNIXEPOCH" => (long)Math.Floor((value - UnixEpoch).TotalSeconds),
            "STRFTIME" => Format(format!, value),
            _ => null,
        };
    }

    /// <summary>Reads the time value (with no argument: now) and applies the modifiers that follow it.</summary>
    private static bool TryResolve(ReadOnlySpan<object?> args, out DateTime value)
    {
        value = default;
        int firstModifier = 1;
        if (args.Length == 0)
        {
            value = DateTime.UtcNow;
            firstModifier = 0;
        }
        else if (args.Length > 1 && args[0] is not null && IsNumber(args[0]) &&
                 SqlScalarExpression.ToText(args[1]).Trim().Equals("unixepoch", StringComparison.OrdinalIgnoreCase))
        {
            // 'unixepoch' is only meaningful directly after a numeric time value.
            if (!TryUnixSeconds(ToDouble(args[0]), out value))
            {
                return false;
            }

            firstModifier = 2;
        }
        else if (!TryParseTimeValue(args[0], out value))
        {
            return false;
        }

        for (int i = firstModifier; i < args.Length; i++)
        {
            if (args[i] is null || !TryApplyModifier(ref value, SqlScalarExpression.ToText(args[i])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a time value: a time string, <c>now</c>, a number (Julian day), or a CLR date.</summary>
    internal static bool TryParseTimeValue(object? raw, out DateTime value)
    {
        value = default;
        switch (raw)
        {
            case null or DBNull:
                return false;
            case DateTime dt:
                value = DateTime.SpecifyKind(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt, DateTimeKind.Utc);
                return true;
            case DateTimeOffset dto:
                value = dto.UtcDateTime;
                return true;
            case DateOnly date:
                value = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                return true;
            case TimeOnly time:
                value = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Add(time.ToTimeSpan());
                return true;
        }

        if (IsNumber(raw))
        {
            return TryFromJulianDay(ToDouble(raw), out value);
        }

        var text = SqlScalarExpression.ToText(raw).Trim();
        if (text.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            value = DateTime.UtcNow;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var julian))
        {
            return TryFromJulianDay(julian, out value);
        }

        var m = TimeStringPattern.Match(text);
        if (!m.Success || (!m.Groups["y"].Success && !m.Groups["h"].Success))
        {
            return false;
        }

        int year = m.Groups["y"].Success ? Int(m, "y") : 2000;
        int month = m.Groups["y"].Success ? Int(m, "mo") : 1;
        int day = m.Groups["y"].Success ? Int(m, "d") : 1;
        int hour = m.Groups["h"].Success ? Int(m, "h") : 0;
        int minute = m.Groups["h"].Success ? Int(m, "mi") : 0;
        int second = m.Groups["s"].Success ? Int(m, "s") : 0;

        if (year < 1 || month is < 1 or > 12 || day is < 1 or > 31 || hour > 24 || minute > 59 || second > 59)
        {
            return false;
        }

        // SQLite accepts day 31 in any month and normalises the overflow ('2026-02-31' is 2026-03-03).
        value = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(day - 1)
            .AddHours(hour)
            .AddMinutes(minute)
            .AddSeconds(second);

        if (m.Groups["f"].Success)
        {
            var digits = m.Groups["f"].Value;
            var fraction = double.Parse("0." + digits, CultureInfo.InvariantCulture);
            value = value.AddTicks((long)Math.Round(fraction * TimeSpan.TicksPerSecond));
        }

        if (m.Groups["z"].Success && m.Groups["z"].Value is not ("Z" or "z"))
        {
            // A "+HH:MM" suffix names local time at that offset; SQLite converts it to UTC.
            var zone = m.Groups["z"].Value;
            var offset = new TimeSpan(
                int.Parse(zone.AsSpan(1, 2), CultureInfo.InvariantCulture),
                int.Parse(zone.AsSpan(4, 2), CultureInfo.InvariantCulture),
                0);
            value = zone[0] == '+' ? value - offset : value + offset;
        }

        return true;
    }

    private static bool TryApplyModifier(ref DateTime value, string modifier)
    {
        var mod = modifier.Trim();
        var lower = mod.ToLowerInvariant();

        switch (lower)
        {
            case "start of day":
                value = value.Date;
                return true;
            case "start of month":
                value = new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                return true;
            case "start of year":
                value = new DateTime(value.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return true;
            case "utc":
                return true;   // values are already held as UTC
            case "localtime":
                value = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value, TimeZoneInfo.Local), DateTimeKind.Utc);
                return true;
        }

        if (lower.StartsWith("weekday ", StringComparison.Ordinal) &&
            int.TryParse(lower.AsSpan("weekday ".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weekday) &&
            weekday is >= 0 and <= 6)
        {
            int ahead = (weekday - (int)value.DayOfWeek + 7) % 7;
            value = value.AddDays(ahead);
            return true;
        }

        var m = ArithmeticModifierPattern.Match(mod);
        if (!m.Success)
        {
            return false;
        }

        var amount = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        try
        {
            value = m.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "day" => value.AddDays(amount),
                "hour" => value.AddHours(amount),
                "minute" => value.AddMinutes(amount),
                "second" => value.AddSeconds(amount),
                "month" => AddMonthsKeepingDay(value, (int)Math.Truncate(amount)),
                "year" => AddMonthsKeepingDay(value, (int)Math.Truncate(amount) * 12),
                _ => value,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// SQLite's month arithmetic: change the month, keep the day number, and let an overflowing day roll into the
    /// next month — unlike <see cref="DateTime.AddMonths"/>, which clamps to the last day.
    /// </summary>
    private static DateTime AddMonthsKeepingDay(DateTime value, int months)
    {
        int totalMonths = (value.Year * 12) + (value.Month - 1) + months;
        int year = totalMonths / 12;
        int month = (totalMonths % 12) + 1;
        return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddDays(value.Day - 1)
            .Add(value.TimeOfDay);
    }

    /// <summary>
    /// <c>strftime</c>: the SQLite conversions, read left to right so <c>%%</c> is one percent sign. An unknown
    /// conversion yields NULL, as in SQLite.
    /// </summary>
    private static string? Format(string format, DateTime value)
    {
        var sb = new StringBuilder(format.Length + 8);
        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            if (++i >= format.Length)
            {
                return null;
            }

            int hour12 = value.Hour % 12 == 0 ? 12 : value.Hour % 12;
            switch (format[i])
            {
                case 'd': sb.Append(value.Day.ToString("D2", inv)); break;
                case 'e': sb.Append(value.Day.ToString(inv).PadLeft(2)); break;
                case 'f': sb.Append(value.Second.ToString("D2", inv)).Append('.').Append(value.Millisecond.ToString("D3", inv)); break;
                case 'F': sb.Append(value.ToString("yyyy-MM-dd", inv)); break;
                case 'H': sb.Append(value.Hour.ToString("D2", inv)); break;
                case 'k': sb.Append(value.Hour.ToString(inv).PadLeft(2)); break;
                case 'I': sb.Append(hour12.ToString("D2", inv)); break;
                case 'l': sb.Append(hour12.ToString(inv).PadLeft(2)); break;
                case 'j': sb.Append(value.DayOfYear.ToString("D3", inv)); break;
                case 'J': sb.Append(ToJulianDay(value).ToString("0.0########", inv)); break;
                case 'm': sb.Append(value.Month.ToString("D2", inv)); break;
                case 'M': sb.Append(value.Minute.ToString("D2", inv)); break;
                case 'p': sb.Append(value.Hour < 12 ? "AM" : "PM"); break;
                case 'P': sb.Append(value.Hour < 12 ? "am" : "pm"); break;
                case 'R': sb.Append(value.ToString("HH:mm", inv)); break;
                case 's': sb.Append(((long)Math.Floor((value - UnixEpoch).TotalSeconds)).ToString(inv)); break;
                case 'S': sb.Append(value.Second.ToString("D2", inv)); break;
                case 'T': sb.Append(value.ToString("HH:mm:ss", inv)); break;
                case 'u': sb.Append(value.DayOfWeek == DayOfWeek.Sunday ? '7' : (char)('0' + (int)value.DayOfWeek)); break;
                case 'w': sb.Append((char)('0' + (int)value.DayOfWeek)); break;
                case 'Y': sb.Append(value.Year.ToString("D4", inv)); break;
                case '%': sb.Append('%'); break;
                default: return null;
            }
        }

        return sb.ToString();
    }

    private static double ToJulianDay(DateTime value) =>
        UnixEpochJulianDay + ((value - UnixEpoch).TotalSeconds / 86400.0);

    private static bool TryFromJulianDay(double julian, out DateTime value)
    {
        value = default;
        if (double.IsNaN(julian) || julian < 1721425.5 || julian > 5373484.5)   // 0001-01-01 .. 9999-12-31
        {
            return false;
        }

        value = UnixEpoch.AddSeconds((julian - UnixEpochJulianDay) * 86400.0);
        return true;
    }

    private static bool TryUnixSeconds(double seconds, out DateTime value)
    {
        value = default;
        try
        {
            value = UnixEpoch.AddSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsNumber(object? value) =>
        value is int or long or short or byte or sbyte or uint or ulong or ushort or double or float or decimal;

    private static double ToDouble(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
}
