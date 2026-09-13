// <copyright file="EnglishWordStemmer.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore tokenizer.rs (WordOnlyStemmer).
// </copyright>

namespace SharpCoreDB.Search.Lexical;

/// <summary>
/// English stemming applied to <b>words only</b>.
/// </summary>
/// <remarks>
/// A token carrying an ASCII digit is not a word and is <b>not</b> stemmed. Stems are a property of
/// printed words; running them over an opaque token that merely ends in a stemmable suffix changes
/// the token (an md5 fragment ending in <c>…e</c> would lose it), and makes an indexed value
/// disagree with the value that was actually recorded.
/// </remarks>
public static class EnglishWordStemmer
{
    /// <summary>The stemmer identity, recorded so a stemmer change is a new analyzer contract.</summary>
    public const string StemmerId = "porter-en@1";

    /// <summary>Whether a token is a "word" for stemming purposes (i.e. carries no ASCII digit).</summary>
    public static bool IsWord(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        foreach (char c in token)
        {
            if (c >= '0' && c <= '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Stem a token when it is a word; otherwise return it unchanged.</summary>
    public static string StemWordOnly(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return IsWord(token) ? Stem(token) : token;
    }

    /// <summary>Stem an English word (lower-cased input is assumed; casing is not folded here).</summary>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length <= 2)
        {
            return word;
        }

        string w = word;
        w = Step1Aw(w);
        w = Step1B(w);
        w = Step1C(w);
        w = Step2(w);
        w = Step3(w);
        w = Step4(w);
        w = Step5(w);
        return w;
    }

    /// <summary>Porter step 1a: plurals.</summary>
    private static string Step1Aw(string w)
    {
        if (EndsWith(w, "sses"))
        {
            return w[..^2];
        }

        if (EndsWith(w, "ies"))
        {
            return w[..^2];
        }

        if (EndsWith(w, "ss"))
        {
            return w;
        }

        return EndsWith(w, "s") ? w[..^1] : w;
    }

    /// <summary>Porter step 1b: past tense / gerund.</summary>
    private static string Step1B(string w)
    {
        if (EndsWith(w, "eed"))
        {
            string stem = w[..^3];
            return Measure(stem) > 0 ? stem + "ee" : w;
        }

        if (EndsWith(w, "ed") && ContainsVowel(w[..^2]))
        {
            return Prolong(w[..^2]);
        }

        if (EndsWith(w, "ing") && ContainsVowel(w[..^3]))
        {
            return Prolong(w[..^3]);
        }

        return w;
    }

    private static string Prolong(string stem)
    {
        if (EndsWith(stem, "at") || EndsWith(stem, "bl") || EndsWith(stem, "iz"))
        {
            return stem + "e";
        }

        if (EndsDoubleConsonant(stem) && !EndsWith(stem, "l") && !EndsWith(stem, "s") && !EndsWith(stem, "z"))
        {
            return stem[..^1];
        }

        return Measure(stem) == 1 && EndsCvc(stem) ? stem + "e" : stem;
    }

    /// <summary>Porter step 1c: a final Y after a vowel becomes I.</summary>
    private static string Step1C(string w)
        => w.Length >= 2 && w[^1] == 'y' && ContainsVowel(w[..^1]) ? w[..^1] + "i" : w;

    // Suffix tables, ordered longest-first so a longer rule is never shadowed by a shorter one.
    private static readonly (string Suffix, string Replacement)[] Step2Rules =
    [
        ("ization", "ize"), ("ational", "ate"), ("fulness", "ful"), ("ousness", "ous"),
        ("iveness", "ive"), ("tional", "tion"), ("biliti", "ble"), ("lessli", "less"),
        ("entli", "ent"), ("ation", "ate"), ("alism", "al"), ("aliti", "al"),
        ("ousli", "ous"), ("iviti", "ive"), ("fulli", "ful"), ("enci", "ence"),
        ("anci", "ance"), ("abli", "able"), ("izer", "ize"), ("ator", "ate"),
        ("alli", "al"), ("eli", "e"),
    ];

    private static readonly (string Suffix, string Replacement)[] Step3Rules =
    [
        ("ational", "ate"), ("tional", "tion"), ("alize", "al"), ("icate", "ic"),
        ("iciti", "ic"), ("ative", ""), ("ical", "ic"), ("ness", ""), ("ful", ""),
    ];

    private static readonly string[] Step4Suffixes =
    [
        "ement", "ance", "ence", "able", "ible", "ment", "ant", "ent", "ism",
        "ate", "iti", "ous", "ive", "ize", "ion", "al", "er", "ic", "ou",
    ];

    /// <summary>Porter step 2: derivational suffixes with m &gt; 0.</summary>
    private static string ApplyTable(string w, (string Suffix, string Replacement)[] rules)
    {
        foreach ((string suffix, string replacement) in rules)
        {
            if (!EndsWith(w, suffix))
            {
                continue;
            }

            string stem = w[..^suffix.Length];
            if (Measure(stem) > 0)
            {
                return stem + replacement;
            }

            return w;
        }

        return w;
    }

    private static string Step2(string w) => ApplyTable(w, Step2Rules);

    private static string Step3(string w) => ApplyTable(w, Step3Rules);

    /// <summary>Porter step 4: suffixes removed only when m &gt; 1.</summary>
    private static string Step4(string w)
    {
        foreach (string suffix in Step4Suffixes)
        {
            if (!EndsWith(w, suffix))
            {
                continue;
            }

            string stem = w[..^suffix.Length];
            if (Measure(stem) <= 1)
            {
                return w;
            }

            // ION survives unless the stem ends S or T.
            if (suffix == "ion" && (stem.Length == 0 || (stem[^1] != 's' && stem[^1] != 't')))
            {
                return w;
            }

            return stem;
        }

        return w;
    }

    /// <summary>Porter step 5: a final E and a final doubled L.</summary>
    private static string Step5(string w)
    {
        // 5a: (m > 1) E -> ""
        if (EndsWith(w, "e"))
        {
            string stem = w[..^1];
            if (Measure(stem) > 1)
            {
                w = stem;
            }
        }

        // 5b: (m > 1 and *d and *L) -> single letter
        if (EndsWith(w, "ll") && Measure(w[..^1]) > 1)
        {
            w = w[..^1];
        }

        return w;
    }

    // ---- Porter helper predicates ------------------------------------------------

    /// <summary>Porter's definition: a consonant is a letter other than A,E,I,O,U, and other than Y preceded by a consonant.</summary>
    private static bool IsConsonant(string w, int i)
    {
        char c = w[i];
        return c switch
        {
            'a' or 'e' or 'i' or 'o' or 'u' => false,
            'y' => i == 0 || IsConsonant(w, i - 1),
            _ => true,
        };
    }

    /// <summary>Porter's measure m: the number of VC sequences in [C](VC)^m[V].</summary>
    private static int Measure(string w)
    {
        int n = w.Length;
        int i = 0;
        int m = 0;

        while (i < n && IsConsonant(w, i))
        {
            i++;
        }

        while (i < n)
        {
            while (i < n && !IsConsonant(w, i))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            m++;
            while (i < n && IsConsonant(w, i))
            {
                i++;
            }
        }

        return m;
    }

    private static bool ContainsVowel(string w)
    {
        for (int i = 0; i < w.Length; i++)
        {
            if (!IsConsonant(w, i))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWith(string w, string suffix)
        => w.EndsWith(suffix, StringComparison.Ordinal);

    private static bool EndsDoubleConsonant(string w)
        => w.Length >= 2 && w[^1] == w[^2] && IsConsonant(w, w.Length - 1);

    /// <summary>Porter's *o: a stem ending cvc where the final consonant is not w, x or y.</summary>
    private static bool EndsCvc(string w)
    {
        int n = w.Length;
        if (n < 3 || !IsConsonant(w, n - 1) || IsConsonant(w, n - 2) || !IsConsonant(w, n - 3))
        {
            return false;
        }

        char last = w[n - 1];
        return last is not ('w' or 'x' or 'y');
    }
}
