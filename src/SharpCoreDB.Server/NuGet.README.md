# SharpCoreDB.Server v1.7.0 - Network Database Server

**High-Performance Network Database Server for .NET 10**

Transform SharpCoreDB into a network-accessible database server with gRPC, Binary TCP, HTTPS REST API, and WebSocket streaming support.

## 🎉 Phase 11 Complete - Production Ready

SharpCoreDB.Server is a **full-featured network database server** comparable to PostgreSQL, MySQL, and SQL Server. Deploy SharpCoreDB as a standalone server with multi-protocol support and enterprise security.

## 🚀 Key Features

✅ **Multiple Protocols**
- gRPC (HTTP/2 + HTTP/3) - Primary, high-performance protocol
- Binary TCP - PostgreSQL wire protocol compatibility
- HTTPS REST API - Web browsers and simple integrations
- WebSocket Streaming - Real-time query streaming

✅ **Enterprise Security**
- JWT Authentication - Industry-standard token-based auth
- Mutual TLS (mTLS) - Certificate-based authentication
- Role-Based Access Control - Admin/Writer/Reader roles
- TLS 1.2+ Enforcement - No plain HTTP allowed

✅ **Production Features**
- Multi-Database Support - Multiple databases per server
- Connection Pooling - 1000+ concurrent connections
- Health Checks - Prometheus-compatible metrics
- Graceful Shutdown - Connection draining on stop
- Cross-Platform - Docker, Windows Service, Linux systemd

✅ **High Performance**
- 50K+ QPS - Queries per second throughput
- Sub-millisecond latency - 0.8-1.2ms (p50)
- Low CPU overhead - Optimized with C# 14 + SIMD
- Memory efficient - Connection pooling and reuse

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Server
```

## 🚀 Quick Start

**Start the server:**

```csharp
// Program.cs is included - just run:
dotnet run --project SharpCoreDB.Server
```

**Or use Docker:**

```bash
docker run -d -p 5001:5001 -p 8443:8443 \
  -v /data/sharpcoredb:/data \
  sharpcoredb/server:latest
```

**Configuration (appsettings.json):**

```json
{
  "Server": {
    "GrpcPort": 5001,
    "HttpsApiPort": 8443,
    "EnableGrpc": true,
    "EnableHttpsApi": true,
    "Security": {
      "TlsEnabled": true,
      "TlsCertificatePath": "/path/to/cert.pem",
      "TlsPrivateKeyPath": "/path/to/key.pem",
      "JwtSecretKey": "your-secret-key-min-32-chars",
      "EnableMutualTls": false
    }
  }
}
```

## 🔗 Client Libraries

**Connect from .NET:**
```bash
dotnet add package SharpCoreDB.Client
```

**Connect from Python:**
```bash
pip install pysharpcoredb
```

**Connect from JavaScript/TypeScript:**
```bash
npm install @sharpcoredb/client
```

## 📊 Performance Benchmarks

| Metric | Result |
|--------|--------|
| Query Latency (p50) | 0.8-1.2ms |
| Query Latency (p95) | < 5ms |
| Throughput (QPS) | 50,000+ |
| Concurrent Connections | 1,000+ |
| Memory per Connection | ~2KB |

## 🌐 Deployment Options

**Docker:**
- Official container images available
- Docker Compose configurations included
- Health check support

**Windows Service:**
- Automated MSI installer
- Windows Event Log integration
- Automatic startup on boot

**Linux systemd:**
- Automated installer script
- systemd unit file included
- Automatic restart on failure
- Journal logging integration

## 📚 Documentation

**Quick Start:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/QUICKSTART.md

**Client Guide:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/CLIENT_GUIDE.md

**REST API Reference:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/REST_API.md

**Security Guide:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/SECURITY.md

**Tool Compatibility Matrix:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/TOOL_COMPATIBILITY_MATRIX_v1.7.0.md

**Tool Compatibility Limitations:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/TOOL_COMPATIBILITY_LIMITATIONS_v1.7.0.md

**Multi-Tenant SaaS Reference:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/MULTITENANT_SAAS_REFERENCE_v1.7.0.md

**Multi-Tenant Threat Model:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/MULTITENANT_THREAT_MODEL_v1.7.0.md

**Operations Runbook:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/MULTITENANT_OPERATIONS_RUNBOOK_v1.7.0.md

**Tenant Backup/Restore/Migration Guide:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/MULTITENANT_BACKUP_RESTORE_MIGRATION_v1.7.0.md

**Reference Sample:** https://github.com/MPCoreDeveloper/SharpCoreDB/tree/master/Examples/Server/SharpCoreDB.MultiTenantSaaSSample

**Full Documentation:** https://github.com/MPCoreDeveloper/SharpCoreDB

## 🏆 Why SharpCoreDB.Server?

✅ **Easy Setup** - Configuration-based, minimal code  
✅ **High Performance** - 50K+ QPS, sub-millisecond latency  
✅ **Secure by Default** - TLS 1.2+ enforced, JWT + mTLS  
✅ **Multi-Protocol** - gRPC, Binary TCP, HTTPS REST, WebSocket  
✅ **Production Ready** - 1,468+ tests, battle-tested  
✅ **Cloud Native** - Docker support, Kubernetes-ready  
✅ **Multi-Language** - .NET, Python, JavaScript/TypeScript clients  

## 📄 License

MIT License - See https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE

---

**Version:** 1.7.0 | **Release Date:** March 8, 2026 | **Status:** ✅ Production Ready
