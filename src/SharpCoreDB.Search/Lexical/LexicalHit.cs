// <copyright file="LexicalHit.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Search.Lexical;

/// <summary>
/// One lexical hit: the document id and its BM25 score (higher is better).
/// </summary>
/// <remarks>
/// This type deliberately does not reuse the vector-search candidate type. A lexical score and a
/// cosine distance are not comparable, so keeping them distinct types stops a caller from adding
/// them together by accident; fusion consumes <b>ranks</b> (see the RRF fusion).
/// </remarks>
/// <param name="Id">The index document id (a row id).</param>
/// <param name="Score">The BM25 score; higher is better.</param>
public readonly record struct LexicalHit(long Id, float Score);
