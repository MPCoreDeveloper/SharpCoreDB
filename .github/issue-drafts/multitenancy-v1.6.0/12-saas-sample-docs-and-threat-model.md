## Summary
Publish a complete SaaS reference sample and documentation set for multi-tenant deployments.

## Why
Feature completeness must be paired with clear adoption guidance.

## Scope
- End-to-end sample: tenant onboarding, routing, secure connection, isolation checks.
- Docs: recommended patterns (DB-per-tenant default, shared DB optional), migration path, ops runbook.
- Threat model: cross-tenant access risks and mitigations.

## Implementation Plan
1. Build reference sample with provisioning and scoped access.
2. Add validation scripts for isolation tests.
3. Publish docs under `docs/server` and link from package readmes.
4. Add architecture and threat model diagrams.

## Acceptance Criteria
- New users can implement multi-tenant setup using docs/sample.
- Threat model and mitigations are explicit.
- Docs are versioned and aligned with released behavior.

## Dependencies
- Finalization issue; depends on most technical tracks.
