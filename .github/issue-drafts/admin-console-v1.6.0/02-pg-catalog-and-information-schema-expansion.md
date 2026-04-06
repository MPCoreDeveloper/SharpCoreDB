## Summary
Expand PostgreSQL metadata compatibility (`pg_catalog`, `information_schema`) to improve GUI introspection.

## Why
GUI tools rely on catalog views to discover tables, indexes, constraints, triggers, and relationships.

## Scope
- Implement/expand required catalog views and columns used by major clients.
- Ensure object discovery works for indexes, PK/FK, constraints, triggers.
- Improve metadata consistency for DDL round-trip scenarios.

## Implementation Plan
1. Trace metadata queries emitted by target tools.
2. Prioritize missing views/columns by impact.
3. Implement metadata mapping layer and tests.
4. Validate with compatibility matrix scripts.

## Acceptance Criteria
- External tools can introspect core schema objects without major fallback failures.
- Metadata query test suite added.
- Documentation updated with supported catalog coverage.

## Dependencies
- Depends on compatibility matrix findings.
