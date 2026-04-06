# SharpCoreDB Server Multi-Tenant Operations Runbook v1.6.0

## Scope
Operational guidance for tenant lifecycle, encryption, quotas, auditing, and incident response.

## Daily Operations
- review `GET /api/v1/tenant-security/audit`
- review `GET /api/v1/tenant-access/audit`
- monitor quota throttle metrics
- monitor tenant provisioning failures

## Tenant Onboarding
1. verify quota tier and encryption key reference
2. provision tenant database through tenant API
3. validate tenant-scoped login
4. run isolation validation script
5. archive onboarding evidence in operations logs

## Key Rotation
1. resolve new tenant key reference
2. call tenant key rotation endpoint
3. verify rotation completion event
4. validate post-rotation connectivity

## Quota Tuning
1. inspect effective quota policy
2. update tenant-specific overrides when justified
3. re-check throttle metrics after change

## Audit Retention
- in-memory stores are bounded and intended for operational diagnostics
- attach custom sinks for SIEM/archive retention
- keep exported security events immutable

## Backup and Restore Notes
- back up `master` together with all tenant databases
- preserve tenant catalog consistency during restore
- verify encryption key references are available before reattaching tenant databases

## Incident Response
### Cross-tenant access suspicion
1. pull recent security audit events
2. pull recent access audit events
3. identify tenant/database/principal involved
4. rotate credentials or revoke grants
5. validate isolation again before reopening access

### Provisioning failure
1. inspect lifecycle events
2. inspect server logs for key/quota/auth failures
3. verify tenant database mapping consistency in `master`
4. retry only with a new idempotency key after root cause is understood
