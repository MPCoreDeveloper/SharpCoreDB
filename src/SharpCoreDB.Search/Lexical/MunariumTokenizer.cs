// <copyright file="MunariumTokenizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore tokenizer.rs.
// </copyright>

namespace SharpCoreDB.Search.Lexical;

/// <summary>One emitted token: its text, its position and its exact source span.</summary>
/// <param name="Text">The token text, with the source casing preserved.</param>
/// <param name="Position">Position in the emitted stream (compounds and their parts each get one).</param>
/// <param name="OffsetFrom">Start offset in the source (inclusive).</param>
/// <param name="OffsetTo">End offset in the source (exclusive).</param>
public readonly record struct LexicalToken(string Text, int Position, int OffsetFrom, int OffsetTo);

/// <summary>
/// The classifying tokenizer: it recognises structures rather than splitting on every
/// non-alphanumeric character, which is where a character-class split diverges from a real
/// PostgreSQL-style parser.
/// </summary>
/// <remarks>
/// Classes implemented (the ones the parity corpora actually exercise):
/// <list type="bullet">
/// <item><b>Numbers</b>: unsigned, signed (<c>-2025</c>, <c>+3.5</c>), decimal
/// (<c>900000.50</c> — and <c>900000.5</c> stays a DIFFERENT token), dotted chains (<c>4.2.1</c>,
/// <c>198.51.100.140</c>), scientific (<c>1.2e-9</c>).</item>
/// <item><b>Sign absorption</b>: a <c>-</c>/<c>+</c> immediately followed by a digit starts a signed
/// number wherever it stands, which is how <c>CVE-2025-31337</c> becomes <c>CVE</c> · <c>-2025</c> ·
/// <c>-31337</c>.</item>
/// <item><b>Digit/slash joins</b>: <c>07/638,337</c> → <c>07/638</c> + <c>337</c>.</item>
/// <item><b>Letter-digit dotted chains</b>: <c>v4.2.1</c> stays whole.</item>
/// <item><b>Alphanumeric words</b>: <c>US7654321B2</c>, <c>2e9</c>, <c>45eagles</c> stay whole.</item>
/// <item><b>Hyphenated compounds</b>: an all-letter chain emits the compound AND its parts, each at
/// its own position; a digit part breaks the chain instead.</item>
/// </list>
/// Offsets are UTF-16 code-unit offsets into the source string (for ASCII text they coincide with
/// byte offsets).
/// </remarks>
public static class MunariumTokenizer
{
    /// <summary>The analyzer contract version, recorded in a build spec so an analyzer change is a new identity.</summary>
    public const string AnalyzerContractVersion = "munarium-en@2";

    /// <summary>Tokenize a text into the classifying token stream.</summary>
    public static IReadOnlyList<LexicalToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<LexicalToken>(Math.Max(4, text.Length / 6));
        int position = 0;
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];

            if (IsLetter(c))
            {
                i = ReadWord(text, i, ref position, tokens);
            }
            else if (IsDigit(c))
            {
                i = ReadNumber(text, i, ref position, tokens);
            }
            else if ((c == '-' || c == '+') && i + 1 < n && IsDigit(text[i + 1]))
            {
                // Sign absorption: the sign belongs to the number.
                i = ReadSignedNumber(text, i, ref position, tokens);
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }

    private static bool IsLetter(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || char.IsLetter(c);

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);

    private static void Emit(List<LexicalToken> tokens, string text, int from, int to, ref int position)
        => tokens.Add(new LexicalToken(text.Substring(from, to - from), position++, from, to));

    /// <summary>
    /// Read a word: an alphanumeric run (including letter-digit dotted chains), an all-letter hyphen
    /// compound (which also emits its parts), or a plain word.
    /// </summary>
    private static int ReadWord(string text, int start, ref int position, List<LexicalToken> tokens)
    {
        int n = text.Length;

        // Leading letters only, so a hyphen compound can be recognised before digits break it.
        int lettersEnd = start;
        while (lettersEnd < n && IsLetter(text[lettersEnd]))
        {
            lettersEnd++;
        }

        // Letters followed by a digit: a numword / letter-digit dotted chain (US7654321B2, rc2, v4.2.1).
        if (lettersEnd < n && IsDigit(text[lettersEnd]))
        {
            int i = lettersEnd;
            while (i < n)
            {
                if (IsLetterOrDigit(text[i]))
                {
                    i++;
                }
                else if (text[i] == '.' && i + 1 < n && IsDigit(text[i + 1]))
                {
                    i++;
                }
                else
                {
                    break;
                }
            }

            Emit(tokens, text, start, i, ref position);
            return i;
        }

        // All-letter hyphen compound: the compound AND its parts, each at its own position. A digit
        // part breaks the chain instead, which the caller's sign-absorption path then picks up.
        if (lettersEnd < n && text[lettersEnd] == '-' && lettersEnd + 1 < n && IsLetter(text[lettersEnd + 1]))
        {
            var partStarts = new List<int> { start };
            var partEnds = new List<int> { lettersEnd };
            int j = lettersEnd;

            while (j < n && text[j] == '-' && j + 1 < n && IsLetter(text[j + 1]))
            {
                j++;
                int partStart = j;
                while (j < n && IsLetter(text[j]))
                {
                    j++;
                }

                partStarts.Add(partStart);
                partEnds.Add(j);
            }

            Emit(tokens, text, start, j, ref position);
            for (int p = 0; p < partStarts.Count; p++)
            {
                Emit(tokens, text, partStarts[p], partEnds[p], ref position);
            }

            return j;
        }

        Emit(tokens, text, start, lettersEnd, ref position);
        return lettersEnd;
    }

    private static int ReadNumber(string text, int start, ref int position, List<LexicalToken> tokens)
    {
        int n = text.Length;
        int i = start;
        while (i < n && IsDigit(text[i]))
        {
            i++;
        }

        // Digit/slash join: 07/638,337 becomes 07/638 then 337.
        if (i + 1 < n && text[i] == '/' && IsDigit(text[i + 1]))
        {
            i++;
            while (i < n && IsDigit(text[i]))
            {
                i++;
            }

            Emit(tokens, text, start, i, ref position);
            return i;
        }

        // Dotted chain / decimal: 4.2.1, 211.100, 198.51.100.140, 900000.50.
        if (i + 1 < n && text[i] == '.' && IsDigit(text[i + 1]))
        {
            while (i + 1 < n && text[i] == '.' && IsDigit(text[i + 1]))
            {
                i++;
                while (i < n && IsDigit(text[i]))
                {
                    i++;
                }
            }

            TryAbsorbExponent(text, ref i);
            Emit(tokens, text, start, i, ref position);
            return i;
        }

        // A letter after the digits makes it an alphanumeric word: 2e9, 45eagles, 12elephants.
        if (i < n && IsLetter(text[i]))
        {
            while (i < n && IsLetterOrDigit(text[i]))
            {
                i++;
            }
        }

        Emit(tokens, text, start, i, ref position);
        return i;
    }

    private static int ReadSignedNumber(string text, int start, ref int position, List<LexicalToken> tokens)
    {
        int n = text.Length;
        int i = start + 1; // the sign is part of the token
        while (i < n && IsDigit(text[i]))
        {
            i++;
        }

        if (i + 1 < n && text[i] == '.' && IsDigit(text[i + 1]))
        {
            while (i + 1 < n && text[i] == '.' && IsDigit(text[i + 1]))
            {
                i++;
                while (i < n && IsDigit(text[i]))
                {
                    i++;
                }
            }

            TryAbsorbExponent(text, ref i);
        }

        Emit(tokens, text, start, i, ref position);
        return i;
    }

    /// <summary>
    /// Absorb a scientific exponent onto a decimal number — but only when its digits do NOT flow into
    /// letters. <c>1.2e-9</c> absorbs; <c>1.2e-9x</c> does not (it becomes <c>1.2</c> · <c>e</c> ·
    /// <c>-9</c> · <c>x</c>).
    /// </summary>
    private static void TryAbsorbExponent(string text, ref int i)
    {
        int n = text.Length;
        if (i >= n || (text[i] != 'e' && text[i] != 'E'))
        {
            return;
        }

        int j = i + 1;
        if (j < n && (text[j] == '+' || text[j] == '-'))
        {
            j++;
        }

        int digitsStart = j;
        while (j < n && IsDigit(text[j]))
        {
            j++;
        }

        if (j == digitsStart || (j < n && IsLetter(text[j])))
        {
            return;
        }

        i = j;
    }
}
