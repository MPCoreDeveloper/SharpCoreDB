# GraphRAG EF Core - Test Documentation

## Overview

Unit and integration tests for GraphRAG EF Core integration. Tests cover LINQ extensions, SQL translation, error handling, and parameter validation.

**Status:** Tests exist (run `dotnet test` to verify)  
**Test Framework:** xUnit  
**Last Updated:** 2025-02-15

---

## Test Files

### 1. GraphTraversalEFCoreTests.cs
**Purpose:** High-level integration tests for LINQ-to-SQL translation

Coverage includes:
- BFS/DFS/Bidirectional/Dijkstra SQL generation
- WHERE clause integration
- Ordering and limiting
- Complex query composition

### 2. GraphTraversalQueryableExtensionsTests.cs
**Purpose:** Unit tests for extension method behavior and validation

Coverage includes:
- Null/empty parameter handling
- Argument validation
- Return type verification
- Chainable method composition

---

## Core Scenarios Covered

### Traverse Method
- BFS strategy generates strategy value 0
- DFS strategy generates strategy value 1
- Bidirectional strategy generates strategy value 2
- Dijkstra strategy generates strategy value 3
- Zero max depth handling
- Large depth values
- Node ID variants
- Column name handling

### WhereIn Method
- IN expression generation
- Empty collection handling
- Chained filters

### TraverseWhere Method
- Combined traversal with predicates
- Parameter validation
- Complex predicate handling

### Distinct & Take
- DISTINCT generation
- LIMIT clause generation
- Chainable method behavior

---

## Running Tests

Use `dotnet test` from the repo root to validate status in your environment.
