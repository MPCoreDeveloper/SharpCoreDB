# SharpCoreDB.Search

Lexical (full-text) search primitives for SharpCoreDB — the munarium-inspired lexical stack,
implemented as pure managed C# 15 / .NET 11:

- **`MunariumTokenizer`** — a classifying tokenizer that recognises structures rather than splitting
  on every non-alphanumeric character: signed/scientific/decimal numbers, dotted chains, slash
  joins, alphanumeric words and hyphenated compounds (emitting the compound *and* its parts, each at
  its own position). Exact byte offsets are preserved.
- **`EnglishStopWords`** — PostgreSQL 16's `english` stop list, embedded.
- **`SnowballStemmer`** — English stemming applied to **words only**: a token carrying an ASCII digit
  is not a word and is not stemmed.
- **`Bm25Index`** — an inverted index with positions and Okapi BM25 scoring.
- **`FullTextIndex`** — builds an index from `(id, text)` pairs and answers `Search(query, k)`.

## Target

`net11.0` only, with C# 15 preview — the v2.1 RC line. The net10.0 / C# 14 line lives on `master`.

## License

MIT. Inspired (conceptually, not ported) by `munarium-datastore` (Apache-2.0) `tokenizer.rs`,
`stopwords.rs` and `lexical.rs`.
