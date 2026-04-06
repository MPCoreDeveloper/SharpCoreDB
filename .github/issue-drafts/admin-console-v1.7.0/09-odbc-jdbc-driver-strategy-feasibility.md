## Summary
Produce a concrete strategy for ODBC/JDBC enablement and external ecosystem integration.

## Status
**State:** RESOLVED

## Implementation Notes

Completed for v1.7.0. Deliverable: `docs/server/ODBC_JDBC_STRATEGY.md`.

**Decision:** Adapter-first strategy — direct users to existing PostgreSQL ODBC/JDBC drivers (psqlODBC, pgjdbc) against the SharpCoreDB binary protocol endpoint (port 5433, TLS). No bespoke driver development required for v1.7.0.

**Key findings:**
- Binary protocol endpoint is live and PostgreSQL wire-compatible (Protocol v3, TLS 1.2+).
- Option A (adapter) satisfies >90% of reported enterprise use cases with zero development cost.
- Option B (native ODBC) and Option C (native JDBC) deferred to v2.x pending confirmed enterprise demand.
- Option D (JDBC proxy bridge) rejected — marginal benefit over Option A.
- Phased roadmap defined: Phase 1 (adapter, now) → Phase 2 (catalog gap closure, v1.7.0) → Phase 3/4 (native drivers, v2.x).
- ODBC quick-start and JDBC quick-start examples published in the strategy document.
- Dependencies to binary protocol, pg_catalog, and security confirmed as satisfied by issues 01–03.

## Why
Some teams require standard connectivity layers for governance and tool compatibility.

## Scope
- Evaluate direct driver development vs adapter approach.
- Define phased roadmap (proof-of-concept to production support).
- Identify protocol/metadata prerequisites.
- Provide cost/benefit and maintenance analysis.

## Implementation Plan
1. Document architecture options and constraints.
2. Prototype minimal path for one target driver path.
3. Assess compatibility/performance/security impact.
4. Publish recommendation and decision record.

## Acceptance Criteria
- Decision record with selected path and rationale. ✅
- Clear dependency map to protocol/catalog work. ✅
- Estimated implementation effort for next phases. ✅

## Dependencies
- Informed by compatibility matrix and protocol maturity.
