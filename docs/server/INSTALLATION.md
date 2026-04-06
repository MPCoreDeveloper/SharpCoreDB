# SharpCoreDB Server — Installation Guide

**Version:** 1.7.0  
**Platforms:** Windows, Linux, macOS, Docker  
**Requirements:** .NET 10 Runtime

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Docker (Recommended)](#docker-recommended)
3. [Linux (systemd)](#linux-systemd)
4. [Windows (Service)](#windows-service)
5. [macOS (launchd)](#macos-launchd)
6. [Build from Source](#build-from-source)
7. [Post-Install Verification](#post-install-verification)

---

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET Runtime | 10.0+ | `dotnet --version` to verify |
| TLS Certificate | X.509 PFX or PEM | Self-signed OK for dev |
| Disk Space | 500 MB+ | Plus data storage |
| RAM | 512 MB minimum | 4 GB recommended for production |

### Generate a Self-Signed TLS Certificate (Development)

```bash
# Linux / macOS
openssl req -x509 -newkey rsa:4096 -keyout server.key -out server.crt \
    -days 365 -nodes -subj "/CN=localhost"

# Create PFX bundle
openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt -password pass:devonly

# Windows (PowerShell)
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "Cert:\LocalMachine\My"
Export-PfxCertificate -Cert $cert -FilePath server.pfx -Password (ConvertTo-SecureString -String "devonly" -Force -AsPlainText)
```

---

## Docker (Recommended)

The fastest way to run SharpCoreDB Server in production.

### Quick Start

```bash
# Clone the repository
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB

# Create certificate and secrets directories
mkdir -p src/SharpCoreDB.Server/certs src/SharpCoreDB.Server/secrets

# Copy your TLS certificate
cp server.pfx src/SharpCoreDB.Server/certs/

# Start the server
docker compose -f src/SharpCoreDB.Server/docker-compose.yml up -d
```

### Verify

```bash
# Check container status
docker compose -f src/SharpCoreDB.Server/docker-compose.yml ps

# Check health
curl -fsk https://localhost:8443/health

# View logs
docker compose -f src/SharpCoreDB.Server/docker-compose.yml logs -f
```

### Docker Compose Configuration

The `docker-compose.yml` exposes two ports:

| Port | Protocol | Description |
|------|----------|-------------|
| 5001 | gRPC (HTTP/2 + HTTP/3) | Primary protocol — high-performance streaming |
| 8443 | HTTPS REST API | Secondary — JSON/HTTP for web clients |
| 8443 | WebSocket (`/ws`) | Tertiary — real-time JSON streaming |

#### Environment Variables

Override configuration via environment variables:

```yaml
environment:
  - Server__Security__JwtSecretKey=your-secret-key-at-least-32-characters
  - Server__Security__TlsCertificatePath=/app/certs/server.pfx
  - Server__Databases__0__Name=mydb
  - Server__Databases__0__DatabasePath=/app/data/user/mydb.scdb
```

#### Volumes

| Volume | Container Path | Purpose |
|--------|---------------|---------|
| `sharpcoredb-data` | `/app/data` | Database files (persistent) |
| `sharpcoredb-logs` | `/app/logs` | Server logs |
| `./certs` | `/app/certs` | TLS certificates (read-only) |
| `./secrets` | `/app/secrets` | Encryption keys (read-only) |

### Build Custom Image

```bash
docker build -t sharpcoredb/server:1.7.0 -f src/SharpCoreDB.Server/Dockerfile .
```

---

## Linux (systemd)

### Automated Install

```bash
# Build the server
dotnet publish src/SharpCoreDB.Server -c Release -o installers/linux/publish

# Run installer (as root)
sudo bash installers/linux/install.sh
```

The installer:
1. Creates `sharpcoredb` system user
2. Installs to `/opt/sharpcoredb/`
3. Creates data/logs/certs directories
4. Registers systemd service
5. Configures UFW firewall rules (if available)

### Manual Install

```bash
# 1. Create user and directories
sudo useradd --system --no-create-home --shell /bin/false sharpcoredb
sudo mkdir -p /opt/sharpcoredb/{data/system,data/user,logs,certs,secrets}

# 2. Publish and copy
dotnet publish src/SharpCoreDB.Server -c Release -o /opt/sharpcoredb/

# 3. Set permissions
sudo chown -R sharpcoredb:sharpcoredb /opt/sharpcoredb
sudo chmod 700 /opt/sharpcoredb/secrets /opt/sharpcoredb/certs

# 4. Install systemd service
sudo cp installers/linux/sharpcoredb.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable sharpcoredb
```

### Service Management

```bash
# Start / Stop / Restart
sudo systemctl start sharpcoredb
sudo systemctl stop sharpcoredb
sudo systemctl restart sharpcoredb

# Status
sudo systemctl status sharpcoredb

# Logs (live tail)
sudo journalctl -u sharpcoredb -f

# Logs (last 100 lines)
sudo journalctl -u sharpcoredb -n 100 --no-pager
```

### systemd Security Hardening

The provided unit file includes:

| Feature | Setting |
|---------|---------|
| Read-only filesystem | `ProtectSystem=strict` |
| No home access | `ProtectHome=true` |
| Writable paths only | `ReadWritePaths=/opt/sharpcoredb/data /opt/sharpcoredb/logs` |
| No privilege escalation | `NoNewPrivileges=true` |
| Private temp | `PrivateTmp=true` |
| File descriptor limit | `LimitNOFILE=65536` |
| Memory limit | `MemoryMax=4G` |
| Auto-restart | `Restart=on-failure` (5s delay) |

---

## Windows (Service)

### Automated Install

```powershell
# Build the server
dotnet publish src\SharpCoreDB.Server -c Release -o installers\windows\publish

# Run installer (Administrator PowerShell)
.\installers\windows\install-service.ps1
```

The installer:
1. Creates `C:\Program Files\SharpCoreDB Server\`
2. Copies published files
3. Registers Windows Service with auto-start
4. Configures failure recovery (restart after 5s/10s/30s)
5. Creates firewall rules for ports 5001 and 8443

### Service Management

```powershell
# Start / Stop
Start-Service SharpCoreDB
Stop-Service SharpCoreDB

# Status
Get-Service SharpCoreDB

# Logs
Get-Content "C:\Program Files\SharpCoreDB Server\logs\sharpcoredb-server*.log" -Tail 50 -Wait
```

### Uninstall

```powershell
Stop-Service SharpCoreDB -Force
sc.exe delete SharpCoreDB
Remove-Item "C:\Program Files\SharpCoreDB Server" -Recurse -Force
```

---

## macOS (launchd)

### Automated Install

```bash
# Build the server
dotnet publish src/SharpCoreDB.Server -c Release -r osx-arm64 -o installers/macos/publish

# Run installer (as root)
sudo bash installers/macos/install.sh
```

The installer:
1. Creates `_sharpcoredb` system user
2. Installs to `/usr/local/opt/sharpcoredb/`
3. Creates data/logs/certs/secrets directories
4. Registers launchd daemon (`com.sharpcoredb.server`)

### Build .pkg Installer

```bash
# Build the .pkg installer
cd installers/macos
bash build_pkg.sh

# Install the .pkg
sudo installer -pkg SharpCoreDB-Server-1.7.0.pkg -target /
```

### Manual Install

```bash
# Build
dotnet publish src/SharpCoreDB.Server -c Release -r osx-arm64 -o /usr/local/opt/sharpcoredb/

# Create directories
mkdir -p /usr/local/opt/sharpcoredb/{data/system,data/user,logs,certs,secrets}

# Start manually
cd /usr/local/opt/sharpcoredb
dotnet sharpcoredb-server.dll
```

### Service Management

```bash
# Load (start at boot)
sudo launchctl load /Library/LaunchDaemons/com.sharpcoredb.server.plist

# Unload (stop and disable)
sudo launchctl unload /Library/LaunchDaemons/com.sharpcoredb.server.plist

# Restart
sudo launchctl unload /Library/LaunchDaemons/com.sharpcoredb.server.plist
sudo launchctl load /Library/LaunchDaemons/com.sharpcoredb.server.plist

# Status
sudo launchctl list | grep sharpcoredb

# Logs
tail -f /usr/local/opt/sharpcoredb/logs/sharpcoredb-stdout.log
```

### Uninstall

```bash
# Automated uninstall (removes everything)
sudo bash installers/macos/uninstall.sh

# Or manual uninstall
sudo launchctl unload /Library/LaunchDaemons/com.sharpcoredb.server.plist
sudo rm /Library/LaunchDaemons/com.sharpcoredb.server.plist
sudo rm -rf /usr/local/opt/sharpcoredb
sudo dscl . -delete /Users/_sharpcoredb
```

---

## Build from Source

```bash
# Clone
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB

# Restore and build
dotnet restore
dotnet build -c Release

# Run directly (development)
dotnet run --project src/SharpCoreDB.Server

# Publish self-contained (no .NET runtime needed on target)
dotnet publish src/SharpCoreDB.Server -c Release --self-contained -r linux-x64 -o dist/linux
dotnet publish src/SharpCoreDB.Server -c Release --self-contained -r win-x64 -o dist/windows
dotnet publish src/SharpCoreDB.Server -c Release --self-contained -r osx-arm64 -o dist/macos
```

---

## Post-Install Verification

After installing on any platform, verify the server is running:

```bash
# 1. Health check (no auth required)
curl -fsk https://localhost:8443/api/v1/health

# Expected response:
# {"status":"healthy","timestamp":"...","version":"1.7.0",...}

# 2. Detailed health (no auth required)
curl -fsk https://localhost:8443/api/v1/health/detailed

# 3. gRPC reflection (development mode)
grpcurl -insecure localhost:5001 list

# 4. Check ports are listening
# Linux:
ss -tlnp | grep -E '5001|8443'
# Windows:
netstat -an | findstr "5001 8443"
```

### Troubleshooting

| Problem | Solution |
|---------|----------|
| `TLS certificate not found` | Verify `Server:Security:TlsCertificatePath` in `appsettings.json` |
| `JWT secret key too short` | Must be at least 32 characters |
| `Port already in use` | Change `GrpcPort` / `HttpsApiPort` in config |
| `Permission denied` | Check file ownership matches service user |
| `Connection refused` | Verify firewall rules allow ports 5001/8443 |

---

**Next:** [Quick Start Guide](QUICKSTART.md) · [Security Guide](SECURITY.md) · [Configuration Reference](CONFIGURATION_SCHEMA.md)
