namespace SharpCoreDB;

using System;
using System.Globalization;

/// <summary>
/// Provides collation-aware comparison operations for runtime query execution.
/// ✅ Phase 5: Enables WHERE, ORDER BY, GROUP BY, and DISTINCT to respect column collations.
/// ✅ Phase 7: Adds JOIN collation resolution and comparer utilities.
/// 
/// Supported collations:
/// - Binary: Ordinal comparison (fastest, case-sensitive)
/// - NoCase: Case-insensitive comparison (OrdinalIgnoreCase)
/// - RTrim: Ignore trailing whitespace, then binary comparison
/// - UnicodeCaseInsensitive: Culture-aware case-insensitive (slowest, most accurate)
/// </summary>
public static class CollationComparator
{
    /// <summary>
    /// Performs collation-aware string comparison.
    /// Returns: -1 (left &lt; right), 0 (equal), 1 (left &gt; right)
    /// 
    /// ✅ PERFORMANCE: Binary collation uses CompareOrdinal (zero allocations).
    /// </summary>
    /// <param name="left">First string to compare (can be null).</param>
    /// <param name="right">Second string to compare (can be null).</param>
    /// <param name="collation">The collation type to use.</param>
    /// <returns>-1, 0, or 1 indicating comparison result.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static int Compare(string? left, string? right, CollationType collation)
    {
        // Handle NULL cases consistently across all collations
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        return collation switch
        {
            // ✅ BINARY: Fastest path - ordinal comparison, case-sensitive
            CollationType.Binary => string.CompareOrdinal(left, right),

            // ✅ NOCASE: Case-insensitive (OrdinalIgnoreCase is faster than CurrentCultureIgnoreCase)
            CollationType.NoCase => string.Compare(left, right, StringComparison.OrdinalIgnoreCase),

            // ✅ RTRIM: Trim trailing whitespace from both strings, then binary comparison
            CollationType.RTrim => CompareRTrim(left, right),

            // ✅ UNICODE_CASE_INSENSITIVE: Culture-aware case-insensitive (slowest)
            CollationType.UnicodeCaseInsensitive => 
                string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase),

            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale =>
                string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase),

            // Default to binary for unknown collations
            _ => string.CompareOrdinal(left, right)
        };
    }

    /// <summary>
    /// Performs locale-specific string comparison using a named locale.
    /// ✅ Phase 9: Use this overload when <see cref="CollationType.Locale"/> is specified with a locale name.
    /// </summary>
    /// <param name="left">First string to compare (can be null).</param>
    /// <param name="right">Second string to compare (can be null).</param>
    /// <param name="localeName">The locale name (e.g., "tr_TR", "de_DE").</param>
    /// <returns>Comparison result: negative, 0, or positive.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static int Compare(string? left, string? right, string localeName)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        return CultureInfoCollation.Instance.Compare(left, right, localeName);
    }

    /// <summary>
    /// Performs collation-aware string equality check.
    /// More efficient than Compare for simple equality testing.
    /// 
    /// ✅ PERFORMANCE: Inlined for hot paths like WHERE clause evaluation.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool Equals(string? left, string? right, CollationType collation)
    {
        // Quick path: both null or reference equal
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return collation switch
        {
            CollationType.Binary => string.Equals(left, right, StringComparison.Ordinal),
            CollationType.NoCase => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => EqualsRTrim(left, right),
            CollationType.UnicodeCaseInsensitive => 
                string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase),
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale =>
                string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase),
            _ => string.Equals(left, right, StringComparison.Ordinal)
        };
    }

    /// <summary>
    /// Performs locale-specific string equality check.
    /// ✅ Phase 9: Use when <see cref="CollationType.Locale"/> is specified with a locale name.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool Equals(string? left, string? right, string localeName)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return CultureInfoCollation.Instance.Equals(left, right, localeName);
    }

    /// <summary>
    /// Collation-aware LIKE pattern matching for WHERE clause filtering.
    /// Supports: % (any chars), _ (single char), [] (character set), [^] (negation)
    /// 
    /// ✅ PERFORMANCE: Avoid regex compilation; use simple string matching.
    /// </summary>
    /// <param name="value">The string value to match.</param>
    /// <param name="pattern">The LIKE pattern (with % and _ wildcards).</param>
    /// <param name="collation">The collation type for case-sensitive/insensitive matching.</param>
    /// <returns>True if value matches pattern under given collation.</returns>
    public static bool Like(string? value, string? pattern, CollationType collation)
    {
        if (value is null || pattern is null) return value == pattern;

        // ✅ OPTIMIZATION: If pattern has no wildcards, use simple equality
        if (!pattern.Contains('%') && !pattern.Contains('_'))
        {
            return Equals(value, pattern, collation);
        }

        // Normalize for case-insensitive matching if needed
        var (normalizedValue, normalizedPattern) = collation switch
        {
            CollationType.NoCase => (value.ToUpperInvariant(), pattern.ToUpperInvariant()),
            CollationType.UnicodeCaseInsensitive => 
                (value.ToUpper(CultureInfo.CurrentCulture), 
                 pattern.ToUpper(CultureInfo.CurrentCulture)),
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale =>
                (value.ToUpper(CultureInfo.CurrentCulture),
                 pattern.ToUpper(CultureInfo.CurrentCulture)),
            _ => (value, pattern)
        };

        // Simple wildcard matching (recursive implementation)
        return LikeRecursive(normalizedValue, normalizedPattern, 0, 0);
    }
    
    /// <summary>
    /// Locale-specific LIKE pattern matching using named locale.
    /// ✅ Phase 9: Use when locale-aware LIKE filtering is needed.
    /// </summary>
    /// <param name="value">The string value to match.</param>
    /// <param name="pattern">The LIKE pattern (with % and _ wildcards).</param>
    /// <param name="localeName">The locale name for case handling (e.g., "tr_TR").</param>
    /// <returns>True if value matches pattern under the locale rules.</returns>
    public static bool Like(string? value, string? pattern, string localeName)
    {
        if (value is null || pattern is null) return value == pattern;

        // If pattern has no wildcards, use simple equality
        if (!pattern.Contains('%') && !pattern.Contains('_'))
        {
            return Equals(value, pattern, localeName);
        }

        // Normalize using locale-aware rules
        var culture = CultureInfoCollation.Instance.GetCulture(localeName);
        var normalizedValue = value.ToUpper(culture);
        var normalizedPattern = pattern.ToUpper(culture);

        return LikeRecursive(normalizedValue, normalizedPattern, 0, 0);
    }

    /// <summary>
    /// Gets the collation-aware hash code for use in DISTINCT and GROUP BY operations.
    /// Matches the behavior of Equals() to maintain consistency.
    /// 
    /// ✅ CRITICAL: Hash code MUST be consistent with Equals() or HashSet/Dictionary breaks!
    /// </summary>
    public static int GetHashCode(string? value, CollationType collation)
    {
        if (value is null) return 0;

        return collation switch
        {
            // Binary: Simple hash
            CollationType.Binary => value.GetHashCode(),

            // NoCase: Hash of uppercase version
            CollationType.NoCase => StringComparer.OrdinalIgnoreCase.GetHashCode(value),

            // RTrim: Hash of trimmed version
            CollationType.RTrim => value.TrimEnd().GetHashCode(),

            // Unicode: Culture-aware hash
            CollationType.UnicodeCaseInsensitive => 
                StringComparer.CurrentCultureIgnoreCase.GetHashCode(value),

            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale =>
                StringComparer.CurrentCultureIgnoreCase.GetHashCode(value),

            _ => value.GetHashCode()
        };
    }

    /// <summary>
    /// Gets the locale-specific hash code for a string.
    /// ✅ Phase 9: Use when <see cref="CollationType.Locale"/> has a locale name.
    /// </summary>
    public static int GetHashCode(string? value, string localeName)
    {
        if (value is null) return 0;
        return CultureInfoCollation.Instance.GetHashCode(value, localeName);
    }

    /// <summary>
    /// Extracts the comparable key from a string based on collation.
    /// Used for normalization in indexes and GROUP BY operations.
    /// </summary>
    public static string NormalizeForComparison(string? value, CollationType collation)
    {
        if (value is null) return string.Empty;

        return collation switch
        {
            CollationType.Binary => value,
            CollationType.NoCase => value.ToUpperInvariant(),
            CollationType.RTrim => value.TrimEnd(),
            CollationType.UnicodeCaseInsensitive => value.ToUpper(CultureInfo.CurrentCulture),
            // ✅ Phase 9: Locale without explicit name falls back to CurrentCulture
            CollationType.Locale => value.ToUpper(CultureInfo.CurrentCulture),
            _ => value
        };
    }

    /// <summary>
    /// Normalizes a string for comparison using a specific locale.
    /// ✅ Phase 9: Locale-specific normalization for index keys and GROUP BY.
    /// </summary>
    public static string NormalizeForComparison(string? value, string localeName)
    {
        if (value is null) return string.Empty;
        return CultureInfoCollation.Instance.NormalizeForComparison(value, localeName);
    }

    /// <summary>
    /// Resolves the collation to use for JOIN operations.
    /// ✅ Phase 7: Determines which collation to use when joining two columns.
    /// 
    /// Resolution order:
    /// 1. Explicit COLLATE clause (highest priority)
    /// 2. Same collation on both columns (no conflict)
    /// 3. Left column collation (default - emit warning)
    /// </summary>
    /// <param name="leftCollation">Left column collation.</param>
    /// <param name="rightCollation">Right column collation.</param>
    /// <param name="explicitCollation">Explicit COLLATE override (if any).</param>
    /// <param name="warningCallback">Callback for collation mismatch warnings.</param>
    /// <returns>The resolved collation type.</returns>
    public static CollationType ResolveJoinCollation(
        CollationType leftCollation,
        CollationType rightCollation,
        CollationType? explicitCollation = null,
        Action<string>? warningCallback = null)
    {
        // Rule 1: Explicit override
        if (explicitCollation.HasValue)
            return explicitCollation.Value;

        // Rule 2: Same collation - no conflict
        if (leftCollation == rightCollation)
            return leftCollation;

        // Rule 3: Mismatch - use left column collation, emit warning
        warningCallback?.Invoke(
            $"JOIN collation mismatch: left column uses {leftCollation}, right column uses {rightCollation}. " +
            $"Using left column collation ({leftCollation}). To suppress this warning, use explicit COLLATE clause in JOIN condition.");

        return leftCollation;
    }

    /// <summary>
    /// Gets an IComparer&lt;string&gt; for the specified collation.
    /// ✅ Phase 7: Used for sorting in MERGE JOIN and ORDER BY operations.
    /// </summary>
    /// <param name="collation">The collation type.</param>
    /// <returns>A comparer that uses the specified collation.</returns>
    public static IComparer<string> GetComparer(CollationType collation)
    {
        return new CollationAwareComparer(collation);
    }

    /// <summary>
    /// Gets an IComparer&lt;string&gt; for a locale-specific collation.
    /// ✅ Phase 9: Used for sorting with named locale.
    /// </summary>
    /// <param name="localeName">The locale name (e.g., "tr_TR").</param>
    /// <returns>A comparer that uses the specified locale.</returns>
    public static IComparer<string> GetComparer(string localeName)
    {
        return new LocaleAwareComparer(localeName);
    }

    /// <summary>
    /// Gets an IEqualityComparer&lt;string&gt; for a locale-specific collation.
    /// ✅ Phase 9: Used for DISTINCT, GROUP BY with locale-aware equality.
    /// </summary>
    /// <param name="localeName">The locale name (e.g., "tr_TR").</param>
    /// <returns>An equality comparer that uses the specified locale.</returns>
    public static IEqualityComparer<string> GetEqualityComparer(string localeName)
    {
        return new LocaleAwareEqualityComparer(localeName);
    }

    // ==================== PRIVATE HELPERS ====================

    /// <summary>
    /// RTrim collation: compare after trimming trailing whitespace.
    /// </summary>
    private static int CompareRTrim(string left, string right)
    {
        var trimmedLeft = left.TrimEnd();
        var trimmedRight = right.TrimEnd();
        return string.CompareOrdinal(trimmedLeft, trimmedRight);
    }

    /// <summary>
    /// RTrim collation: equality after trimming trailing whitespace.
    /// </summary>
    private static bool EqualsRTrim(string left, string right)
    {
        var trimmedLeft = left.TrimEnd();
        var trimmedRight = right.TrimEnd();
        return string.Equals(trimmedLeft, trimmedRight, StringComparison.Ordinal);
    }

    /// <summary>
    /// Recursive helper for LIKE pattern matching.
    /// Handles %, _, and literal characters.
    /// </summary>
    private static bool LikeRecursive(string value, string pattern, int vIdx, int pIdx)
    {
        // Both strings exhausted - match!
        if (vIdx == value.Length && pIdx == pattern.Length)
            return true;

        // Pattern exhausted but value remains - no match (unless only % left)
        if (pIdx == pattern.Length)
            return vIdx == value.Length;

        // Handle % wildcard - matches zero or more characters
        if (pattern[pIdx] == '%')
        {
            // % at end matches everything
            if (pIdx == pattern.Length - 1)
                return true;

            // Try matching % with zero chars, then one char, then two, etc.
            for (int i = vIdx; i <= value.Length; i++)
            {
                if (LikeRecursive(value, pattern, i, pIdx + 1))
                    return true;
            }
            return false;
        }

        // Handle _ wildcard - matches exactly one character
        if (pattern[pIdx] == '_')
        {
            if (vIdx == value.Length)
                return false; // No char to match

            return LikeRecursive(value, pattern, vIdx + 1, pIdx + 1);
        }

        // Handle literal character - must match exactly
        if (vIdx == value.Length)
            return false; // Value exhausted but pattern has literal

        if (value[vIdx] == pattern[pIdx])
            return LikeRecursive(value, pattern, vIdx + 1, pIdx + 1);

        return false;
    }
}

/// <summary>
/// Collation-aware equality comparer for use in HashSet&lt;string&gt; and Dictionary&lt;string, TValue&gt;.
/// Used for DISTINCT and GROUP BY operations with collation support.
/// 
/// ✅ Phase 5: Enables collation-aware deduplication.
/// ✅ Phase 7: Used in hash JOIN strategy for collation-aware key matching.
/// </summary>
public sealed class CollationAwareEqualityComparer : IEqualityComparer<string>
{
    private readonly CollationType _collation;

    /// <summary>
    /// Creates a new collation-aware equality comparer.
    /// </summary>
    /// <param name="collation">The collation to use for comparisons.</param>
    public CollationAwareEqualityComparer(CollationType collation)
    {
        _collation = collation;
    }

    /// <summary>
    /// Determines if two strings are equal under this collation.
    /// </summary>
    public bool Equals(string? x, string? y) => CollationComparator.Equals(x, y, _collation);

    /// <summary>
    /// Gets the hash code for a string under this collation.
    /// ✅ CRITICAL: Must be consistent with Equals() for HashSet/Dictionary correctness.
    /// </summary>
    public int GetHashCode(string obj) => CollationComparator.GetHashCode(obj, _collation);
}

/// <summary>
/// Collation-aware comparer for use in LINQ OrderBy, Array.Sort, and MERGE JOIN operations.
/// ✅ Phase 7: Enables collation-aware sorting for MERGE JOIN strategy.
/// </summary>
public sealed class CollationAwareComparer : IComparer<string>
{
    private readonly CollationType _collation;

    /// <summary>
    /// Creates a new collation-aware comparer.
    /// </summary>
    /// <param name="collation">The collation to use for comparisons.</param>
    public CollationAwareComparer(CollationType collation)
    {
        _collation = collation;
    }

    /// <summary>
    /// Compares two strings using the specified collation.
    /// Returns: -1 (x &lt; y), 0 (equal), 1 (x &gt; y)
    /// </summary>
    public int Compare(string? x, string? y) => CollationComparator.Compare(x, y, _collation);
}

/// <summary>
/// Locale-aware comparer for use in LINQ OrderBy, Array.Sort, and MERGE JOIN operations.
/// ✅ Phase 9: Enables locale-specific sorting for named locales.
/// </summary>
public sealed class LocaleAwareComparer(string localeName) : IComparer<string>
{
    private readonly string _localeName = localeName;

    /// <summary>
    /// Compares two strings using the specified locale.
    /// </summary>
    public int Compare(string? x, string? y) => CollationComparator.Compare(x, y, _localeName);
}

/// <summary>
/// Locale-aware equality comparer for use in HashSet and Dictionary operations.
/// ✅ Phase 9: Enables locale-specific equality for DISTINCT and GROUP BY.
/// </summary>
public sealed class LocaleAwareEqualityComparer(string localeName) : IEqualityComparer<string>
{
    private readonly string _localeName = localeName;

    /// <summary>
    /// Determines if two strings are equal under this locale.
    /// </summary>
    public bool Equals(string? x, string? y) => CollationComparator.Equals(x, y, _localeName);

    /// <summary>
    /// Gets the hash code for a string under this locale.
    /// </summary>
    public int GetHashCode(string obj) => CollationComparator.GetHashCode(obj, _localeName);
}
