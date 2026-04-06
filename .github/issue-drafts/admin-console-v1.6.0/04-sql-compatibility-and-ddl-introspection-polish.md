## Summary
Improve SQL compatibility and DDL/introspection behavior required by admin tools.

## Why
Many tools execute introspection SQL and DDL probes beyond regular CRUD workloads.

## Scope
- Identify unsupported or partially supported SQL patterns from tool traces.
- Improve compatibility paths for common introspection statements.
- Normalize command tags and result metadata where needed.

## Implementation Plan
1. Capture failing SQL traces from target tools.
2. Group failures by parser, execution, metadata, or result-shape category.
3. Implement incremental fixes with regression tests.
4. Re-run tool compatibility scripts.

## Acceptance Criteria
- Reduced introspection/DDL failures in target tools.
- Regression test set for known query patterns.
- Release notes document improved compatibility.

## Dependencies
- Depends on matrix data and protocol hardening.
