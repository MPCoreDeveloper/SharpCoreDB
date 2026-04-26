<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Serilog.Sinks

  **High-Performance Serilog Sink for SharpCoreDB**

  **Version:** 1.7.1 (Phase 9.2)  
  **Status:** Production Ready ✅

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.7.1-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Serilog.Sinks)
  [![Serilog](https://img.shields.io/badge/Serilog-4.x-purple.svg)](https://serilog.net/)

</div>

---

Serilog sink package for `SharpCoreDB`.

**Version:** `v1.7.1`  
**Package:** `SharpCoreDB.Serilog.Sinks`


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Structured log persistence into SharpCoreDB tables
- Batch-oriented sink design for efficient write throughput
- Supports async logging pipelines
- Can benefit from SharpCoreDB encryption at rest
- Integrates with standard Serilog configuration flows

## Changes in v1.7.1

- Package/docs aligned to `v1.7.1`
- Documentation cleaned up for production sink usage
- Inherits SharpCoreDB core metadata/parser reliability fixes
- No intended breaking changes from v1.5.0

## Installation

```bash
dotnet add package SharpCoreDB.Serilog.Sinks --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- Root README: `README.md`

