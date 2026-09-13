// <copyright file="LexicalAnalyzer.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore lexical.rs.
// </copyright>

namespace SharpCoreDB.Search.Lexical;

using SharpCoreDB.Text;

/// <summary>
/// The analyzer pipeline: classify (tokenize) → fold (lower-case) → drop stop words → stem words.
/// </summary>
/// <remarks>
/// Index and query pass through this same pipeline. That is what keeps them consistent: a query term
/// is the same string as the indexed term because both went through one definition of "analyze".
/// </remarks>
public static class LexicalAnalyzer
{
    /// <summary>
    /// The analyzer contract version. Changing the tokenizer or the stemmer changes what an index
    /// contains, so it is part of a build's identity rather than an implementation detail.
    /// </summary>
    public const string ContractVersion =
        MunariumTokenizer.AnalyzerContractVersion + "+" + EnglishWordStemmer.StemmerId;

    /// <summary>
    /// Analyze text into (term, position) pairs. Positions come from the tokenizer, so a stop word
    /// that is dropped leaves a gap — which is exactly what makes adjacent-term tests meaningful.
    /// </summary>
    public static IReadOnlyList<(string Term, int Position)> Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var analyzed = new List<(string Term, int Position)>();
        foreach (LexicalToken token in MunariumTokenizer.Tokenize(text))
        {
            string folded = token.Text.ToLowerInvariant();
            if (EnglishStopWords.IsStopTerm(folded))
            {
                continue;
            }

            string term = EnglishWordStemmer.StemWordOnly(folded);
            if (term.Length > 0)
            {
                analyzed.Add((term, token.Position));
            }
        }

        return analyzed;
    }

    /// <summary>Analyze text into its distinct terms.</summary>
    public static IReadOnlyList<string> Terms(string text)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string term, _) in Analyze(text))
        {
            if (seen.Add(term))
            {
                terms.Add(term);
            }
        }

        return terms;
    }

    /// <summary>The adjacent term pairs of a query, in order (the phrase candidates).</summary>
    public static IReadOnlyList<(string First, string Second)> AdjacentTerms(string text)
    {
        IReadOnlyList<(string Term, int Position)> analyzed = Analyze(text);
        var pairs = new List<(string, string)>();
        for (int i = 0; i + 1 < analyzed.Count; i++)
        {
            if (analyzed[i + 1].Position == analyzed[i].Position + 1)
            {
                pairs.Add((analyzed[i].Term, analyzed[i + 1].Term));
            }
        }

        return pairs;
    }
}
