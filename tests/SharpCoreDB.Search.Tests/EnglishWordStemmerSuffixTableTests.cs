// <copyright file="EnglishWordStemmerSuffixTableTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Search.Tests;

using SharpCoreDB.Search.Lexical;
using Xunit;

/// <summary>
/// One word per suffix-table entry of <see cref="EnglishWordStemmer"/>: each step-2 and step-3 rule, plus the
/// step-4 suffixes it can leave behind.
/// <para>
/// Added because the remaining survivors in that file are individual <c>String</c> mutations inside the
/// <c>Step2Rules</c> and <c>Step3Rules</c> tables: a renamed or dropped suffix changed the stem of the words
/// that use it and nothing noticed. The Porter pairs in <c>EnglishWordStemmerPorterTests</c> cover about half
/// the entries; these words cover the rest, one rule at a time.
/// </para>
/// </summary>
public sealed class EnglishWordStemmerSuffixTableTests
{
    public static TheoryData<string, string> SuffixTablePairs => new()
    {
        // step 2 — one word per rule, chosen so no earlier (longer) rule matches
        { "modernization", "modern" },      // ization -> ize, removed again by step 4 (ize)
        { "educational", "educ" },          // ational -> ate, removed by step 4 (ate)
        { "peacefulness", "peace" },        // fulness -> ful, then step 3 (ful -> "")
        { "graciousness", "gracious" },     // ousness -> ous; step 4 needs m > 1 and m is 1
        { "passiveness", "passiv" },       // iveness -> ive, then step 5a removes the e (m(passiv) == 2)
        { "functional", "function" },          // tional -> tion; step 4 (ion) does not apply (m(funct) == 1)
        { "possibility", "possibl" },      // step 1c, biliti -> ble, then step 5a removes the e
        { "carelessly", "careless" },       // lessli -> less
        { "evidently", "evid" },         // entli -> ent, then step 4 (ent) with m(evid) == 2
        { "navigation", "navig" },          // ation -> ate, then step 4 (ate)
        { "liberalism", "liber" },          // alism -> al, then step 4 (al)
        { "personality", "person" },        // step 1c, then aliti -> al, then step 4 (al)
        { "graciously", "gracious" },       // step 1c, then ousli -> ous
        { "activity", "activ" },           // step 1c, iviti -> ive, then step 5a removes the e
        { "peacefully", "peace" },          // step 1c, then fulli -> ful, then step 3 (ful -> "")
        { "frequency", "frequenc" },        // step 1c, then enci -> ence, then step 5a drops the e
        { "discrepancy", "discrep" },    // step 1c, anci -> ance, then step 4 (ance) with m == 2
        { "remarkably", "remark" },      // step 1c, abli -> able, then step 4 (able) with m == 2
        { "popularizer", "popular" },       // izer -> ize, then step 4 (ize)
        { "accelerator", "acceler" },       // ator -> ate, then step 4 (ate)
        { "radically", "radic" },           // step 1c, then alli -> al, then step 4 (al)
        { "loosely", "loose" },             // step 1c, then eli -> e; step 5a needs m > 1

        // step 3 — the rules step 2 does not already claim
        { "complicate", "complic" },          // icate -> ic; step 4 (ic) does not apply (m(compl) == 1)
        { "specificity", "specif" },        // step 1c, then iciti -> ic, then step 4 (ic)
        { "imaginative", "imagin" },        // ative -> "", nothing claims "in" afterwards
        { "logical", "logic" },             // ical -> ic; step 4 (ic) needs m > 1 and m is 1
        { "happiness", "happi" },           // ness -> "" ("ss" keeps step 1a from touching it)
        { "useful", "use" },                // ful -> "", then step 5a keeps the e (m == 1)
    };

    [Theory]
    [MemberData(nameof(SuffixTablePairs))]
    public void Stem_AppliesTheSuffixTableEntry(string word, string expected)
    {
        Assert.Equal(expected, EnglishWordStemmer.Stem(word));
    }
}
