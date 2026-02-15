# GraphRAG — Lightweight Graph Capabilities for SharpCoreDB

**Status:** Proposed (Feasibility: ✅ Highly Feasible)  
**Target Release:** v1.4.0 (Q3 2026) → v2.0.0 (Q2 2027)  
**Last Updated:** 2026-02-15

---

## Overview

GraphRAG adds lightweight graph traversal capabilities to SharpCoreDB, enabling hybrid **Vector + Graph** queries in a single embedded .NET DLL. This positions SharpCoreDB as the definitive memory backend for .NET AI Agents, local LLMs, and knowledge-graph applications.

### The Problem

Vector search (HNSW) is ideal for *"fuzzy"* semantic retrieval, but it lacks structural precision. Real-world AI agent workloads need both:

| Capability | Answers | Example |
|---|---|---|
| **Vector Search** | *"What is semantically similar?"* | Find code snippets about async patterns |
| **Graph Traversal** | *"What is structurally connected?"* | Find all classes implementing `IRepository` within 2 hops |
| **GraphRAG (hybrid)** | *Both, combined* | Find similar code **only if connected** to `ClassX` within N hops |

### Key Differentiator

No other .NET embedded database combines vectors and graphs in a single zero-dependency DLL.

---

## Documentation Index

| Document | Purpose | Audience |
|---|---|---|
| [GRAPHRAG_PROPOSAL_ANALYSIS.md](./GRAPHRAG_PROPOSAL_ANALYSIS.md) | Feasibility analysis, architecture alignment, competitive analysis | Technical architects, product managers |
| [GRAPHRAG_IMPLEMENTATION_PLAN.md](./GRAPHRAG_IMPLEMENTATION_PLAN.md) | Comprehensive phased implementation plan with file-level detail | Engineers, tech leads |
| [GRAPHRAG_IMPLEMENTATION_STARTPOINT.md](./GRAPHRAG_IMPLEMENTATION_STARTPOINT.md) | Engineering startpoint and architecture decision record | Engineers, tech leads |
| [ROADMAP_V2_GRAPHRAG_SYNC.md](./ROADMAP_V2_GRAPHRAG_SYNC.md) | Integrated product roadmap (GraphRAG + Sync + Encryption) | Executive team, product managers |
| [STRATEGIC_RECOMMENDATIONS.md](./STRATEGIC_RECOMMENDATIONS.md) | Executive decision document for v2 roadmap approval | C-level executives |

---

## Quick Links

- **Implementation Plan →** [GRAPHRAG_IMPLEMENTATION_PLAN.md](./GRAPHRAG_IMPLEMENTATION_PLAN.md)
- **Implementation Startpoint →** [GRAPHRAG_IMPLEMENTATION_STARTPOINT.md](./GRAPHRAG_IMPLEMENTATION_STARTPOINT.md)
- **Proposal Analysis →** [GRAPHRAG_PROPOSAL_ANALYSIS.md](./GRAPHRAG_PROPOSAL_ANALYSIS.md)
- **v2 Roadmap →** [ROADMAP_V2_GRAPHRAG_SYNC.md](./ROADMAP_V2_GRAPHRAG_SYNC.md)
- **Vector Search Docs →** [../Vectors/README.md](../Vectors/README.md)

---

## Architecture at a Glance

```
SharpCoreDB Engine
├── Storage Layer
│   ├── IStorageEngine (returns long row IDs — graph pointers)
│   ├── B-Tree Index Manager (batch deferred updates)
│   └── ROWREF Column Type ← NEW (Phase 1)
│
├── Query Layer
│   ├── SQL Parser (extensible)
│   ├── Query Optimizer (cost-based, plan cache)
│   ├── GRAPH_TRAVERSE() SQL function ← NEW (Phase 2)
│   └── Hybrid Vector+Graph optimizer ← NEW (Phase 3)
│
├── Graph Layer ← NEW
│   ├── GraphTraversalEngine (BFS/DFS)
│   ├── GraphTraversalOptimizer
│   ├── AdjacencyListIndex
│   └── Cycle detection + path finding
│
└── Vector Layer (existing)
    ├── HNSW Index
    └── Vector similarity search
