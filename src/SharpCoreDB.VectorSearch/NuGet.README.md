# SharpCoreDB.VectorSearch v1.9.5

**SIMD-Accelerated Vector Similarity Search**

Semantic search and similarity matching **50-100x faster than SQLite** using HNSW indexing and SIMD acceleration.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ Phase 8 complete: HNSW-accelerated semantic search
- ✅ 50-100x faster than SQLite
- ✅ NativeAOT compatible
- ✅ Zero breaking changes

## 🚀 Key Features

- **HNSW Indexing**: Hierarchical Navigable Small World graphs
- **SIMD Acceleration**: Vectorized distance calculations
- **Semantic Search**: Find similar embeddings efficiently
- **Scalability**: Millions of vectors, sub-millisecond queries
- **Production Ready**: 1,468+ tests, enterprise reliability

## 🎯 Use Cases

- **RAG Systems**: Knowledge base semantic search
- **Recommendation Engines**: Find similar products/content
- **Duplicate Detection**: Identify similar records
- **Clustering**: Group similar embeddings
- **AI Applications**: LLM-powered semantic search

## 📊 Performance

- **Search Latency**: Sub-millisecond for millions of vectors
- **Index Size**: 10-20% of raw vector data
- **Build Time**: Efficient incremental indexing
- **Memory**: Low-memory HNSW implementation

## 📚 Documentation

- [Vector Search Overview](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/vectors/README.md)
- [Implementation Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/vectors/IMPLEMENTATION.md)
- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.VectorSearch --version 1.9.5
```

**Requires:** SharpCoreDB v1.9.5+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready | **Phase:** 8 Complete




