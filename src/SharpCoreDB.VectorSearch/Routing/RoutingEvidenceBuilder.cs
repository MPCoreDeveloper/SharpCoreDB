// <copyright file="RoutingEvidenceBuilder.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore routing.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Routing;

/// <summary>
/// Computes bounded routing evidence from a probe pool's candidate texts.
/// </summary>
public static class RoutingEvidenceBuilder
{
    /// <summary>
    /// Build evidence for one pool.
    /// </summary>
    /// <param name="contentTerms">The query's content vocabulary (see <see cref="RoutingSignals.ContentTerms"/>).</param>
    /// <param name="phrases">The query's adjacent content-word pairs (see <see cref="RoutingSignals.QueryPhrases"/>).</param>
    /// <param name="candidateTexts">The pool's probe texts.</param>
    /// <param name="topMargin">Optional <c>(top - third) / top</c> margin over the pool's own leg values.</param>
    public static RoutingEvidence Build(
        IReadOnlyList<string> contentTerms,
        IReadOnlyList<(string First, string Second)> phrases,
        IReadOnlyList<string> candidateTexts,
        double? topMargin = null)
    {
        ArgumentNullException.ThrowIfNull(contentTerms);
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(candidateTexts);

        int hits = candidateTexts.Count;
        var perTextTerms = new List<HashSet<string>>(hits);
        var perTextTokens = new List<List<string>>(hits);
        foreach (string text in candidateTexts)
        {
            List<string> tokens = RoutingSignals.Tokenize(text);
            perTextTokens.Add(tokens);
            perTextTerms.Add(new HashSet<string>(tokens, StringComparer.Ordinal));
        }

        double coverage = 0.0;
        if (contentTerms.Count > 0)
        {
            int present = 0;
            foreach (string term in contentTerms)
            {
                foreach (HashSet<string> terms in perTextTerms)
                {
                    if (terms.Contains(term))
                    {
                        present++;
                        break;
                    }
                }
            }

            coverage = (double)present / contentTerms.Count;
        }

        double multiTerm = 0.0;
        if (hits > 0 && contentTerms.Count > 0)
        {
            int multiCount = 0;
            foreach (HashSet<string> terms in perTextTerms)
            {
                int matched = 0;
                foreach (string term in contentTerms)
                {
                    if (terms.Contains(term))
                    {
                        matched++;
                    }
                }

                if (matched >= 2)
                {
                    multiCount++;
                }
            }

            multiTerm = (double)multiCount / hits;
        }

        double phraseFraction = 0.0;
        if (hits > 0 && phrases.Count > 0)
        {
            int phraseCount = 0;
            foreach (List<string> tokens in perTextTokens)
            {
                if (ContainsAnyPhrase(tokens, phrases))
                {
                    phraseCount++;
                }
            }

            phraseFraction = (double)phraseCount / hits;
        }

        return new RoutingEvidence(
            coverage,
            multiTerm,
            phraseFraction,
            topMargin ?? 0.0,
            hits);
    }

    private static bool ContainsAnyPhrase(
        List<string> tokens,
        IReadOnlyList<(string First, string Second)> phrases)
    {
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            foreach ((string first, string second) in phrases)
            {
                if (tokens[i] == first && tokens[i + 1] == second)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
