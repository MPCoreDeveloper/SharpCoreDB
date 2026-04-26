# SharpCoreDB.Analytics

Advanced analytics extension for `SharpCoreDB`.

**Version:** `v1.7.1`  
**Package:** `SharpCoreDB.Analytics`


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- 100+ aggregate functions (`COUNT`, `SUM`, `AVG`, `STDDEV`, `VARIANCE`, `PERCENTILE`, `CORRELATION`)
- Window functions (`ROW_NUMBER`, `RANK`, `DENSE_RANK`, `LAG`, `LEAD`)
- Statistical and bivariate analysis helpers
- Time-series and OLAP-oriented helpers
- SIMD-friendly execution for high-throughput analytics workloads

## Changes in v1.7.1

- Package version synchronized to `v1.7.1`
- Analytics docs aligned with production feature set
- Inherits core durability/parser improvements from SharpCoreDB v1.7.1
- No intended breaking changes from v1.5.0

## Installation

```bash
dotnet add package SharpCoreDB.Analytics --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- `docs/analytics/README.md`

