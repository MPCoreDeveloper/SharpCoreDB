// <copyright file="EnglishStopWords.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. PostgreSQL 16's english.stop list, embedded.
// </copyright>

namespace SharpCoreDB.Text;

/// <summary>
/// PostgreSQL 16's <c>english</c> stop list, embedded rather than read at runtime so nothing here has
/// a filesystem dependency. Shared by the lexical analyzer and the vector-search routing signals.
/// </summary>
public static class EnglishStopWords
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "i", "me", "my", "myself", "we", "our", "ours", "ourselves", "you", "your", "yours",
        "yourself", "yourselves", "he", "him", "his", "himself", "she", "her", "hers", "herself",
        "it", "its", "itself", "they", "them", "their", "theirs", "themselves", "what", "which",
        "who", "whom", "this", "that", "these", "those", "am", "is", "are", "was", "were", "be",
        "been", "being", "have", "has", "had", "having", "do", "does", "did", "doing", "a", "an",
        "the", "and", "but", "if", "or", "because", "as", "until", "while", "of", "at", "by",
        "for", "with", "about", "against", "between", "into", "through", "during", "before",
        "after", "above", "below", "to", "from", "up", "down", "in", "out", "on", "off", "over",
        "under", "again", "further", "then", "once", "here", "there", "when", "where", "why",
        "how", "all", "any", "both", "each", "few", "more", "most", "other", "some", "such",
        "no", "nor", "not", "only", "own", "same", "so", "than", "too", "very", "s", "t", "can",
        "will", "just", "don", "should", "now",
    };

    /// <summary>The stop-word set (ordinal, already lower-cased).</summary>
    public static IReadOnlySet<string> All => Words;

    /// <summary>Whether a folded (lower-cased) token is one of PostgreSQL's English stop words.</summary>
    public static bool IsStopTerm(string token) => Words.Contains(token);
}
