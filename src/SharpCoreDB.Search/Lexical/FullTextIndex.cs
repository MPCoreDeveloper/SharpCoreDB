// <copyright file="FullTextIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore lexical.rs (BM25 over Tantivy).
// </copyright>

namespace SharpCoreDB.Search.Lexical;

/// <summary>
/// An in-memory full-text index: an inverted index with positions, scored with Okapi BM25.
/// </summary>
/// <remarks>
/// Documents are indexed through <see cref="LexicalAnalyzer"/>, so queries and documents share one
/// definition of a term. A query whose analyzed terms contain an adjacent pair (a phrase) gets a
/// bounded boost — positions are stored precisely so that rule is possible.
/// </remarks>
public sealed class FullTextIndex
{
    /// <summary>BM25 term-frequency saturation.</summary>
    public const double K1 = 1.2;

    /// <summary>BM25 length-normalization.</summary>
    public const double B = 0.75;

    /// <summary>The multiplicative phrase bonus applied when every adjacent query pair is present.</summary>
    public const double PhraseBoost = 1.5;

    private readonly Dictionary<string, List<Posting>> _postings = new(StringComparer.Ordinal);
    private readonly Dictionary<long, int> _lengths = new();
    private readonly List<long> _ids = new();
    private long _totalTerms;

    /// <summary>Gets the number of indexed documents.</summary>
    public int DocumentCount => _ids.Count;

    /// <summary>Gets the average analyzed document length.</summary>
    public double AverageDocumentLength => _ids.Count == 0 ? 0.0 : (double)_totalTerms / _ids.Count;

    /// <summary>Index one document.</summary>
    /// <param name="id">The document (row) id.</param>
    /// <param name="text">The document text.</param>
    public void Add(long id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<(string Term, int Position)> analyzed = LexicalAnalyzer.Analyze(text);
        _lengths[id] = analyzed.Count;
        _totalTerms += analyzed.Count;
        _ids.Add(id);

        var positionsByTerm = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach ((string term, int position) in analyzed)
        {
            if (!positionsByTerm.TryGetValue(term, out List<int>? positions))
            {
                positions = [];
                positionsByTerm[term] = positions;
            }

            positions.Add(position);
        }

        foreach ((string term, List<int> positions) in positionsByTerm)
        {
            if (!_postings.TryGetValue(term, out List<Posting>? postings))
            {
                postings = [];
                _postings[term] = postings;
            }

            postings.Add(new Posting(id, positions.Count, positions));
        }
    }

    /// <summary>
    /// Score the index for a query and return the best <paramref name="k"/> hits (higher score first,
    /// ties broken by id so results are reproducible).
    /// </summary>
    public IReadOnlyList<LexicalHit> Search(string query, int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        if (_ids.Count == 0)
        {
            return [];
        }

        IReadOnlyList<string> terms = LexicalAnalyzer.Terms(query);
        if (terms.Count == 0)
        {
            return [];
        }

        double averageLength = AverageDocumentLength;
        int documents = _ids.Count;
        var scores = new Dictionary<long, double>();

        foreach (string term in terms)
        {
            if (!_postings.TryGetValue(term, out List<Posting>? postings) || postings.Count == 0)
            {
                continue;
            }

            // BM25 idf in its standard +0.5 smoothed form.
            double documentFrequency = postings.Count;
            double idf = Math.Log(1.0 + ((documents - documentFrequency + 0.5) / (documentFrequency + 0.5)));

            foreach (Posting posting in postings)
            {
                double tf = posting.Frequency;
                double length = _lengths[posting.Id];
                double denominator = tf + (K1 * (1.0 - B + (B * length / averageLength)));
                double contribution = idf * (tf * (K1 + 1.0) / denominator);
                scores[posting.Id] = scores.TryGetValue(posting.Id, out double existing)
                    ? existing + contribution
                    : contribution;
            }
        }

        if (scores.Count == 0)
        {
            return [];
        }

        IReadOnlyList<(string First, string Second)> phrases = LexicalAnalyzer.AdjacentTerms(query);
        if (phrases.Count > 0)
        {
            foreach (long id in scores.Keys.ToList())
            {
                if (ContainsEveryPhrase(id, phrases))
                {
                    scores[id] *= PhraseBoost;
                }
            }
        }

        var hits = new List<LexicalHit>(scores.Count);
        foreach ((long id, double score) in scores)
        {
            hits.Add(new LexicalHit(id, (float)score));
        }

        hits.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Id.CompareTo(b.Id);
        });

        return hits.Take(k).ToList();
    }

    private bool ContainsEveryPhrase(long id, IReadOnlyList<(string First, string Second)> phrases)
    {
        foreach ((string first, string second) in phrases)
        {
            if (!HasAdjacent(id, first, second))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAdjacent(long id, string first, string second)
    {
        List<int>? firstPositions = PositionsOf(first, id);
        List<int>? secondPositions = PositionsOf(second, id);
        if (firstPositions is null || secondPositions is null)
        {
            return false;
        }

        foreach (int position in firstPositions)
        {
            if (secondPositions.Contains(position + 1))
            {
                return true;
            }
        }

        return false;
    }

    private List<int>? PositionsOf(string term, long id)
    {
        if (!_postings.TryGetValue(term, out List<Posting>? postings))
        {
            return null;
        }

        foreach (Posting posting in postings)
        {
            if (posting.Id == id)
            {
                return posting.Positions;
            }
        }

        return null;
    }

    private sealed record Posting(long Id, int Frequency, List<int> Positions);
}
