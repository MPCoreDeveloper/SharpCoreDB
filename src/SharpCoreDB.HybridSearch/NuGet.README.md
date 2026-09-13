# SharpCoreDB.HybridSearch

Hybrid (lexical + vector) retrieval for SharpCoreDB.

Runs the **BM25 lexical leg** (`SharpCoreDB.Search`) and the **vector leg**
(`SharpCoreDB.VectorSearch`) and fuses them with **reciprocal-rank fusion**.

Fusion consumes **ranks**, never raw scores: a BM25 score and a cosine distance are not numerically
comparable, and a merge that adds them produces plausible results in the wrong order — silently.
Every hit carries its per-leg rank and score so "the answer changed" and "one leg changed" stay
distinguishable.

## Target

`net11.0` only, with C# 15 preview — the v2.1 RC line.

## License

MIT. Inspired by the munarium-datastore fusion design (Apache-2.0).
