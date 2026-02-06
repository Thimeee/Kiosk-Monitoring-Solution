# Kiosk Monitoring Solution - Setup Scripts

## Quick Start

### Server Setup

```powershell
# 1. Open PowerShell as Administrator
# 2. Navigate to Setup folder
cd "Setup\Server"

# 3. Run installer (interactive wizard)
.\Install-Server.ps1

# 4. Verify installation
.\Verify-Server.ps1
```

### Branch Setup

```powershell
# 1. Copy Setup\Branch folder to the branch PC
# 2. Open PowerShell as Administrator
cd "Setup\Branch"

# 3. Run installer (interactive wizard)
.\Install-Branch.ps1

# 4. Verify installation
.\Verify-Branch.ps1 -ServerIP "192.168.1.24"
```

---

## Files Overview

```
Setup\
├── README.md                           <- This file
├── Server\
│   ├── Install-Server.ps1             <- Server automated installer
│   └── Verify-Server.ps1             <- Server health check
└── Branch\
    ├── Install-Branch.ps1             <- Branch automated installer
    ├── Verify-Branch.ps1             <- Branch connectivity check
    └── branch-appsettings-template.json  <- Generated after server setup
```

---

## Server Installer Steps

The `Install-Server.ps1` script performs these steps automatically:

| Step | Action | Details |
|------|--------|---------|
| 0 | Configuration | Collects all settings via interactive prompts |
| 1 | Folders | Creates all required directory structures |
| 2 | MQTT | Configures Mosquitto broker, passwords, ACL |
| 3 | SSH/SFTP | Installs OpenSSH, creates SFTP user, configures sshd |
| 4 | Database | Creates SQL Server database and application user |
| 5 | IIS | Installs IIS features, checks .NET Hosting Bundle |
| 6 | API | Builds and deploys MonitoringBackend to IIS |
| 7 | Frontend | Builds and deploys Blazor WASM to IIS |
| 8 | VNC Proxy | Builds and deploys VNC proxy to IIS |
| 9 | Firewall | Creates inbound firewall rules |
| 10 | Health | Creates health check scheduled task |
| 11 | Branch Config | Generates branch configuration template |

---

## Branch Installer Steps

The `Install-Branch.ps1` script performs these steps automatically:

| Step | Action | Details |
|------|--------|---------|
| 0 | Configuration | Collects branch ID and server connection details |
| 1 | Folders | Creates monitoring directory structure |
| 2 | Config | Generates appsettings.json for the branch |
| 3 | Build | Builds and deploys SFTPService |
| 4 | Service | Installs as Windows Service with auto-restart |
| 5 | Connectivity | Tests API, MQTT, SFTP, Database connections |
| 6 | Firewall | Creates outbound firewall rules |

---

## Prerequisites

### Server
- Windows Server 2019/2022
- .NET 8.0 SDK (for building) or Hosting Bundle (for running)
- SQL Server 2019/2022
- Mosquitto MQTT Broker (download from https://mosquitto.org)
- Administrator access

### Branch
- Windows 10/11 or Windows Server
- .NET 8.0 Runtime
- SQL Server (local instance for CDK database)
- Network access to central server
- Administrator access

---

## Configuration Values

### Values you need to know BEFORE running Server Setup:

| Value | Example | Description |
|-------|---------|-------------|
| Server IP | 192.168.1.24 | IP address of the server |
| SQL Instance | localhost | SQL Server instance name |
| SQL Password | StrongP@ss! | Password for app database user |

### Values you need to know BEFORE running Branch Setup:

| Value | Example | Description |
|-------|---------|-------------|
| Branch ID | BRANCH001 | Unique ID for this branch |
| Server IP | 192.168.1.24 | IP of the central server |
| MQTT Password | BranchMqtt@2026! | Given by server admin |
| SFTP Password | Sftp@Secure2026! | Given by server admin |
| Local SQL Password | - | Local database password |

---

## After Installation

### Server
1. Run EF Core migrations: `dotnet ef database update` in MonitoringBackend folder
2. Open browser: `http://<ServerIP>:5235`
3. Register admin user via API: `POST http://<ServerIP>:5155/api/auth/register`

### Branch
1. Check service status: `Get-Service BranchMonitoringService`
2. Check logs: `C:\CDK_Monitoring\Log\ConnectionLog\`
3. Run verify: `.\Verify-Branch.ps1 -ServerIP "192.168.1.24"`

---

## Troubleshooting

### Server: IIS site not starting
```powershell
# Check event log
Get-EventLog -LogName Application -Source "IIS*" -Newest 10
# Check stdout logs
Get-Content "C:\Monitoring\Logs\API\stdout*.log" -Tail 50
# Restart app pool
Restart-WebAppPool MonitoringAPIPool
```

### Server: MQTT not connecting
```powershell
# Check service
Get-Service mosquitto
# Check log
Get-Content "C:\Mosquitto\log\mosquitto.log" -Tail 50
# Test publish
& "C:\Program Files\mosquitto\mosquitto_pub.exe" -h localhost -p 1883 -u MonitoringBackend -P "password" -t "test" -m "hello"
```

### Branch: Service not starting
```powershell
# Check service status
Get-Service BranchMonitoringService
# Check event viewer
Get-EventLog -LogName Application -Newest 20 | Where-Object { $_.Source -like "*SFTPService*" -or $_.Source -like "*Branch*" }
# Check logs
Get-Content "C:\CDK_Monitoring\Log\ExceptionLog\*" -Tail 50
```

### Branch: Cannot connect to server
```powershell
# Test connectivity
Test-Connection 192.168.1.24
Test-NetConnection -ComputerName 192.168.1.24 -Port 5155
Test-NetConnection -ComputerName 192.168.1.24 -Port 1883
Test-NetConnection -ComputerName 192.168.1.24 -Port 22
```
