# SharpCoreDB Server Tool Compatibility Limitations v1.6.0

## Overview
This document records known limitations and operator guidance when using external PostgreSQL-oriented database tools with SharpCoreDB Server.

## Current Known Limitations
### Metadata coverage
Advanced PostgreSQL GUI panels may expect richer `pg_catalog` and `information_schema` coverage than the current server build exposes.

### DDL reconstruction
Some tools may show incomplete reconstructed DDL for constraints, indexes, or advanced object metadata until later metadata-compatibility phases are completed.

### Object browser fidelity
If a GUI browser pane looks incomplete, prefer direct SQL inspection before concluding the object is missing.

## Recommended Workarounds
- use `psql` or SQL editors inside GUI tools for authoritative checks
- rely on the compatibility smoke assets before certifying a tool version internally
- document exact tool version and driver combination when reporting issues

## Issue Tracking Guidance
When a limitation is confirmed during certification, capture:
- tool name and version
- driver version
- exact failing workflow
- sample query or action
- screenshot/log excerpt when available

These findings should feed follow-up admin-console roadmap issues focused on metadata and protocol maturity.
