# SharpCoreDB.Identity

Lightweight identity services for SharpCoreDB encrypted single-file databases.

## Patch updates in v1.9.3

- ✅ Version bump and documentation alignment to the synchronized 1.9.3 release line.
- ✅ Current test count (2,223) and backwards compatibility status published in release documentation.

## Backwards Compatibility (Identity package)

SharpCoreDB.Identity 1.9.3 is **fully backwards compatible** with 1.9.1 (and the 1.9.x line) when used with the matching SharpCoreDB core package version.

- This 1.9.3 release was a version synchronization + documentation preparation release only. No public API, behavior, or data model changes were introduced in the Identity package.
- Public surface (SharpCoreDbIdentityService, SharpCoreUser/Role/Claim/Login entities, SharpCoreDbPasswordHasher, SharpCoreDbTokenProvider, SharpCoreIdentityOptions, SharpCoreSignInResult) remains stable and unchanged from 1.9.1.
- The package declares a conditional NuGet dependency on SharpCoreDB at the exact matching version (1.9.3). When consumed as a NuGet package, always pair with the same-version core (recommended for all optional packages such as Identity, EventSourcing, Projections, CQRS, etc.).
- All Identity tests pass (ConcurrencyTests, LockoutTests, PasswordHasherTests, PersistenceTests, SharpCoreDbIdentityServiceTests).
- No [Obsolete] or removal of previously public members in this release.
- Users on 1.9.1 can safely upgrade both core + Identity to 1.9.3 with no code changes required.

See `tests/SharpCoreDB.Identity.Tests/STABILITY_REPORT.md` and the service implementation for the full stable surface.

## Patch updates in v1.9.1 (historical)

- ✅ Aligned package metadata and version references to the synchronized 1.9.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.




