# Contributors

SharpCoreDB is an open-source project — every contribution counts, and we are grateful for the
community members who invest their time and expertise in making it better. 🙏

This file acknowledges community contributors whose work has improved the project beyond the
core maintainers.

## saltus7

- **Issue #341 + PR #342 — Single-file (`.scdb`) encryption bypass**: root-caused why
  `DatabaseOptions.EncryptionKey` was silently ignored by `SingleFileStorageProvider` and provided a
  complete proposed fix — AES-256-GCM at rest on every block I/O path plus a 12-test regression
  suite (plaintext-at-rest, correct-key round-trip, wrong-key rejection, file integrity).
- saltus7 opened [PR #342](https://github.com/MPCoreDeveloper/SharpCoreDB/pull/342) at
  `2026-08-29T05:20Z` — *before* the fix shipped — and their root-cause analysis was accurate:
  the shipped fix in **v1.9.7** (commit `16ed7e22`) follows exactly the integration points
  saltus7 identified.
- We merged our own implementation rather than the PR to keep the release moving, but this
  contributor deserves full credit for the diagnosis and the reference implementation.

---

*Want to see your name here? Open a pull request — every contribution, big or small, is welcome.*
