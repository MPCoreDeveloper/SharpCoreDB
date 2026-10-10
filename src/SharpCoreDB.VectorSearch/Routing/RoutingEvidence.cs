// <copyright file="RoutingEvidence.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore routing.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Routing;

using System.Text;
using SharpCoreDB.Text;

/// <summary>
/// Bounded, engine-neutral evidence that one pool is ABOUT a query. Every field is in [0, 1] except
/// <see cref="HitCount"/>, which is bounded by the caller's own probe size.
/// </summary>
public sealed record RoutingEvidence(
    double TermCoverage,
    double MultiTermFraction,
    double PhraseFraction,
    double TopMargin,
    int HitCount)
{
    /// <summary>
    /// The composite routing score:
    /// <c>(coverage + multiTerm)/2 × (1 + phraseBoost × phraseFraction)</c>. Strong phrase evidence
    /// can overrule density-shaped signals; weak phrase evidence barely moves them.
    /// </summary>
    public double RoutingScore(double phraseBoost)
        => ((TermCoverage + MultiTermFraction) / 2.0) * (1.0 + (phraseBoost * PhraseFraction));
}

/// <summary>
/// Engine-neutral routing signals. Raw per-shard relevance must never be used as a cross-collection
/// routing score: <c>ts_rank</c> and BM25 magnitudes are not comparable, so a raw-density ranking
/// silently routes by engine rather than by evidence. Every signal here is bounded and scale-free —
/// multiply one engine's scores by a thousand and the evidence does not move.
/// </summary>
public static class RoutingSignals
{
    /// <summary>Bump when a signal's definition or the composite changes.</summary>
    public const uint PolicyVersion = 1;

    /// <summary>The query's distinct content (non-stop) vocabulary, in order.</summary>
    public static IReadOnlyList<string> ContentTerms(string query)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string token in Tokenize(query))
        {
            if (!EnglishStopWords.IsStopTerm(token) && seen.Add(token))
            {
                terms.Add(token);
            }
        }

        return terms;
    }

    /// <summary>
    /// The query's adjacent content-word pairs. Adjacency is in the ORIGINAL token order, so a pair
    /// separated by a stop word is not a phrase.
    /// </summary>
    public static IReadOnlyList<(string First, string Second)> QueryPhrases(string query)
    {
        List<string> tokens = Tokenize(query);
        var phrases = new List<(string, string)>();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (!EnglishStopWords.IsStopTerm(tokens[i]) && !EnglishStopWords.IsStopTerm(tokens[i + 1]))
            {
                phrases.Add((tokens[i], tokens[i + 1]));
            }
        }

        return phrases;
    }

    /// <summary>
    /// Rank pools by routing score. An empty pool (no hits) is excluded rather than ranked last:
    /// a route to an empty pool is a route to nothing. Ties break on margin, then hit count, then name.
    /// </summary>
    /// <param name="pools">Pool name + evidence pairs.</param>
    /// <param name="phraseBoost">How strongly phrase evidence overrules the density-shaped signals.</param>
    /// <returns>The pool indexes in ranked order.</returns>
    public static IReadOnlyList<int> Rank(
        IReadOnlyList<(string Name, RoutingEvidence Evidence)> pools,
        double phraseBoost)
    {
        ArgumentNullException.ThrowIfNull(pools);

        var ranked = new List<(int Index, string Name, RoutingEvidence Evidence)>(pools.Count);
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].Evidence.HitCount > 0)
            {
                ranked.Add((i, pools[i].Name, pools[i].Evidence));
            }
        }

        ranked.Sort((a, b) =>
        {
            int byScore = b.Evidence.RoutingScore(phraseBoost).CompareTo(a.Evidence.RoutingScore(phraseBoost));
            if (byScore != 0)
            {
                return byScore;
            }

            int byMargin = b.Evidence.TopMargin.CompareTo(a.Evidence.TopMargin);
            if (byMargin != 0)
            {
                return byMargin;
            }

            int byHits = b.Evidence.HitCount.CompareTo(a.Evidence.HitCount);
            return byHits != 0 ? byHits : string.CompareOrdinal(a.Name, b.Name);
        });

        return ranked.Select(static r => r.Index).ToList();
    }

    /// <summary>Tokenize to lower-cased alphanumeric runs.</summary>
    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder(16);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }
}
