<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img src="https://img.shields.io/badge/Blazor-Server-512BD4?style=for-the-badge&logo=blazor&logoColor=white" />
  <img src="https://img.shields.io/badge/MQTT-Mosquitto-3C5280?style=for-the-badge&logo=eclipse-mosquitto&logoColor=white" />
  <img src="https://img.shields.io/badge/SignalR-Real--Time-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img src="https://img.shields.io/badge/SQL%20Server-Database-CC2927?style=for-the-badge&logo=microsoft-sql-server&logoColor=white" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" />
</p>

# 🖥️ Kiosk Monitoring Solution

> **A real-time, enterprise-grade branch kiosk monitoring and remote management platform** built with .NET 8, designed to centrally monitor, manage, and update CDK banking kiosk terminals across multiple branches through a modern web-based dashboard.

---

## 📋 Table of Contents

- [Overview](#-overview)
- [Key Features](#-key-features)
- [System Architecture](#-system-architecture)
- [Tech Stack](#-tech-stack)
- [Project Structure](#-project-structure)
- [Getting Started](#-getting-started)
- [Configuration](#%EF%B8%8F-configuration)
- [API Reference](#-api-reference)
- [MQTT Topics](#-mqtt-topic-structure)
- [Deployment](#-deployment)
- [Security](#-security)
- [Documentation](#-documentation)
- [Screenshots](#-screenshots)
- [Contributing](#-contributing)
- [License](#-license)

---

## 🔍 Overview

The **Kiosk Monitoring Solution** is a full-stack distributed system designed for **banking and financial institutions** to centrally monitor and manage CDK (Cash Deposit Kiosk) terminals deployed across multiple branches. It provides real-time device health tracking, remote desktop access, SFTP-based file management, automated patch deployment, and a beautiful Blazor-powered web dashboard.

### Problem It Solves

Managing hundreds of kiosk terminals across geographically distributed bank branches is challenging:

- **No real-time visibility** into terminal health and status
- **Manual software updates** requiring on-site visits
- **No centralized file management** for terminal file systems
- **Inability to remotely troubleshoot** terminal issues
- **No scheduling** for off-hours maintenance windows

### How It Works

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CENTRAL MONITORING SERVER                        │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │
│  │ Monitoring   │  │ Blazor Web   │  │ VNC Proxy API            │  │
│  │ Backend API  │  │ Dashboard    │  │ (Remote Desktop)         │  │
│  │ (ASP.NET)    │  │ (Server)     │  │                          │  │
│  └──────┬───────┘  └──────┬───────┘  └────────────┬─────────────┘  │
│         │                 │                        │                │
│         │     ┌───────────┴────────────┐           │                │
│         │     │   SignalR Hub          │           │                │
│         │     │   (Real-time Events)   │           │                │
│         │     └───────────┬────────────┘           │                │
│         │                 │                        │                │
│  ┌──────┴─────────────────┴────────────────────────┴──────────┐    │
│  │                  MQTT Broker (Mosquitto)                    │    │
│  │              Bi-directional Message Bus                     │    │
│  └──────┬─────────────────┬────────────────────────┬──────────┘    │
│         │                 │                        │                │
│  ┌──────┴──────┐   ┌──────┴──────┐   ┌────────────┴─────────┐     │
│  │ SQL Server  │   │ SFTP Server │   │ Scheduler Service     │     │
│  │ Database    │   │ (OpenSSH)   │   │ (Background Worker)   │     │
│  └─────────────┘   └─────────────┘   └──────────────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
                              │
                    ┌─────────┴─────────┐
                    │   MQTT + SFTP     │
                    │   (Encrypted)     │
                    └─────────┬─────────┘
                              │
         ┌────────────────────┼────────────────────┐
         │                    │                    │
  ┌──────┴──────┐     ┌──────┴──────┐     ┌──────┴──────┐
  │  Branch 1   │     │  Branch 2   │     │  Branch N   │
  │  SFTP Agent │     │  SFTP Agent │     │  SFTP Agent │
  │  + CDK      │     │  + CDK      │     │  + CDK      │
  └─────────────┘     └─────────────┘     └─────────────┘
```

---

## ✨ Key Features

### 🟢 Real-Time Branch Monitoring

- **Live terminal status tracking** (Online / Offline) with SignalR push notifications
- **MQTT-based bi-directional communication** between server and branch agents
- **Automatic status detection** — terminals report their CDK error status, MQTT connection, service status, and database connectivity in real-time
- **Connection health monitoring** with auto-reconnection and stale connection cleanup

### 📊 Hardware Performance Analytics

- **Real-time system metrics** — CPU usage, process/thread counts, uptime, logical processors
- **Memory monitoring** — Total/In-use/Available RAM (GB), committed memory, slot usage
- **Disk health** — Per-drive capacity, free space, drive type (HDD/SSD classification)
- **Network statistics** — Adapter info, SSID, IPv4/IPv6, send/receive speed (Kbps)
- **Throttled health updates** to prevent message flooding while maintaining responsiveness

### 🔧 Patch Management & Deployment

- **Full patch lifecycle management** — Upload → Validate → Deploy → Monitor → Rollback
- **Multi-step deployment pipeline:**
  1. `START` → `DOWNLOAD` → `VALIDATE` → `EXTRACT` → `STOP_APP` → `BACKUP` → `UPDATE` → `START_APP` → `VERIFY` → `CLEANUP` → `COMPLETE`
- **Single branch and bulk branch deployment** — Push patches to one terminal or all at once
- **Chunked file upload** with server-side chunk merging and checksum verification (SHA-256)
- **Scheduled patch deployment** — Schedule patches for off-hours with automatic execution
- **Rollback support** — Built-in automatic rollback on failures
- **Patch types** — Extensible patch type system with configurable deployment strategies
- **Real-time progress tracking** via SignalR with per-step status updates

### 📁 Remote File Management (SFTP)

- **Browse branch file systems** remotely with tree-view folder structure
- **Upload files** from server to branch terminals
- **Download files** from branch terminals to server
- **Delete files** remotely on branch terminals
- **Transfer progress tracking** — Real-time upload/download progress via SignalR
- **Job-based file operations** with status tracking and audit trail

### 🖥️ Remote Desktop Access (VNC)

- **Browser-based VNC remote desktop** — Access any branch terminal directly from the web dashboard
- **WebSocket proxy architecture** — Bridges browser WebSocket to backend TCP/VNC connection
- **Optimized for performance** — 128KB buffer sizes, TCP NoDelay, large send/receive buffers
- **TCP KeepAlive enabled** for long-running remote sessions
- **Bidirectional data forwarding** between browser and VNC server

### 🔐 Authentication & User Management

- **JWT-based authentication** with configurable token expiration
- **ASP.NET Core Identity integration** for user management
- **Role-based access control** — Admin, Branch, and custom roles
- **User registration and management** through the admin panel

### ⏰ Background Job Scheduler

- **Automated scheduled patch deployment** with configurable time windows
- **Parallel job execution** with configurable concurrency limits (up to 10 parallel jobs)
- **30-minute timeout protection** for expired scheduled jobs
- **Continuous polling** with 2-second check intervals for near-instant execution

### 📈 Dashboard & Administration

- **Central monitoring dashboard** with at-a-glance branch status overview
- **Branch management** — Add, configure, and manage branch terminals
- **Per-branch dashboard** — Detailed view with health, files, patches, and remote access
- **Server file management** (File Server page) for managing patches and uploads
- **Settings panel** for system configuration

---

## � Scalability — Built for 500+ Branches

This system is **architected from the ground up** to support **500+ concurrent branch terminals** without performance degradation. Every layer of the stack is designed with high-concurrency, low-latency patterns:

```
┌─────────────────────────────────────────────────────────────────────┐
│                   SCALABILITY ARCHITECTURE                          │
│                                                                     │
│  500+ Branches ──► MQTT Broker (1000 conn limit) ──► Server        │
│                     • Lightweight pub/sub protocol                  │
│                     • ~1KB per status message                       │
│                     • Topic-based routing (no broadcasting)         │
│                                                                     │
│  Server Processing:                                                 │
│  • SemaphoreSlim(100) ──► Controlled DB write concurrency          │
│  • ConcurrentDictionary ──► Lock-free connection tracking          │
│  • Health Throttling ──► Prevents message flooding                 │
│  • SignalR Groups ──► Targeted push (not broadcast-all)            │
│                                                                     │
│  Scheduler Engine:                                                  │
│  • 10 parallel patch jobs ──► Prevents server overload             │
│  • 2-second polling ──► Near-instant scheduled execution           │
│  • 30-min timeout ──► Auto-fail for stale jobs                     │
│                                                                     │
│  Branch Agent:                                                      │
│  • Auto-reconnect with 100 retries ──► Self-healing connections    │
│  • SQLite local storage ──► Offline resilience                     │
│  • Fire-and-forget with exception isolation ──► No cascading fails │
└─────────────────────────────────────────────────────────────────────┘
```

| Design Pattern            | Purpose                            | Impact on 500+ Branches                        |
| ------------------------- | ---------------------------------- | ---------------------------------------------- |
| **MQTT Pub/Sub**          | Lightweight message bus (~1KB/msg) | 500 branches × 1 msg/sec = easily handled      |
| **SemaphoreSlim(100)**    | Throttled DB writes                | Prevents SQL Server connection pool exhaustion |
| **ConcurrentDictionary**  | Lock-free thread-safe collections  | Zero contention on connection tracking         |
| **SignalR Groups**        | Targeted message delivery          | Only relevant clients receive updates          |
| **Health Throttle**       | Rate-limited performance updates   | Prevents 500 × 1/sec flooding                  |
| **Chunked File Transfer** | Large file upload/download         | SHA-256 verified, no memory overflow           |
| **Background Workers**    | Isolated service execution         | Failures don't cascade across branches         |
| **Auto-Reconnection**     | Self-healing MQTT connections      | Handles network interruptions gracefully       |

> **✅ Production-Tested Architecture** — Designed for real banking environments with 500+ CDK terminals operating 24/7 across geographically distributed branches.

---

## �🛠️ Tech Stack

| Layer                       | Technology                                         |
| --------------------------- | -------------------------------------------------- |
| **Backend API**             | ASP.NET Core 8.0 Web API                           |
| **Frontend**                | Blazor Server (.NET 8)                             |
| **Real-time Communication** | SignalR (Server → Browser), MQTT (Server ↔ Branch) |
| **Message Broker**          | Mosquitto MQTT Broker (TLS supported)              |
| **Database**                | SQL Server 2019/2022 with Entity Framework Core    |
| **File Transfer**           | SFTP via OpenSSH with SSH.NET library              |
| **Remote Desktop**          | VNC over WebSocket Proxy (noVNC compatible)        |
| **Branch Agent**            | .NET Worker Service (Windows Service)              |
| **Local Storage**           | SQLite (Branch-side offline storage)               |
| **Authentication**          | JWT Bearer Tokens + ASP.NET Core Identity          |
| **Deployment**              | IIS 10+ / Windows Server 2019/2022                 |
| **Installer**               | PowerShell-based automated installer scripts       |

---

## 📁 Project Structure

```
KioskMonitoringSolution/
│
├── KioskMonitoringSolution.sln          # Visual Studio Solution (5 projects)
│
├── MonitoringBackend/                   # 🔧 Central Server API
│   ├── Controllers/
│   │   ├── AuthController.cs            # JWT login/registration
│   │   ├── BranchController.cs          # Branch CRUD & status management
│   │   ├── HealthController.cs          # Health check endpoints
│   │   ├── PatchesController.cs         # Patch upload, deploy, manage
│   │   ├── RemoteController.cs          # VNC remote access control
│   │   ├── SFTPFilesController.cs       # File operations (browse/upload/download/delete)
│   │   └── UserMangmentController.cs    # User management API
│   ├── Service/
│   │   ├── MqttWorker.cs               # MQTT message handler (BackgroundService)
│   │   ├── SchedulerService.cs          # Scheduled patch deployment engine
│   │   └── LoggerService.cs             # Centralized logging
│   ├── SRHub/
│   │   └── BranchHub.cs                # SignalR Hub for real-time events
│   ├── Data/
│   │   ├── AppDbContext.cs              # Entity Framework DbContext
│   │   └── AppUser.cs                   # Identity user model
│   ├── Helper/
│   │   ├── MQTTHelper.cs               # MQTT client wrapper with reconnection
│   │   ├── SftpStorageService.cs        # SFTP storage operations
│   │   ├── GetFolderStructure.cs        # Folder tree builder
│   │   └── CreateUniqId.cs             # Unique ID generator for jobs
│   ├── DTO/                             # Data Transfer Objects
│   ├── Migrations/                      # EF Core database migrations
│   ├── Program.cs                       # App startup & middleware
│   └── appsettings.json                 # Configuration
│
├── BranchMonitorFrontEnd/               # 🖥️ Blazor Server Web Dashboard
│   └── BranchMonitorFrontEnd/
│       ├── Pages/
│       │   ├── Dashboard.razor          # Main monitoring dashboard
│       │   ├── Branches.razor           # Branch list & management
│       │   ├── SignIn.razor             # Authentication page
│       │   ├── BranchPages/
│       │   │   ├── BranchDashboard.razor        # Per-branch detail view
│       │   │   ├── Branch_HealthAnalytics.razor  # Performance monitoring
│       │   │   ├── BranchFileTranfer.razor       # Remote file management
│       │   │   ├── BranchFileInstalition.razor   # File installation
│       │   │   └── BranchRemoteLogin.razor       # VNC remote access
│       │   ├── PatchUpdatePages/
│       │   │   ├── PatchEvent.razor              # Patch creation & upload
│       │   │   ├── PatchPushAllBranch.razor       # Bulk patch deployment
│       │   │   └── Fileserver.razor              # Server file browser
│       │   └── Setting/
│       │       ├── AddNewBranch.razor            # Branch registration
│       │       └── AddNewUser.razor              # User management
│       ├── Components/                  # Reusable UI components
│       ├── Layout/                      # App layout & navigation
│       ├── Service/                     # Frontend service layer
│       └── wwwroot/                     # Static assets
│
├── SFTPService/                         # 🔄 Branch Agent (Worker Service)
│   ├── Worker.cs                        # Main worker — MQTT handler & CDK monitoring
│   ├── Service/
│   │   ├── MQTTHelper.cs               # MQTT client for branch
│   │   ├── SftpFileService.cs          # SFTP file transfer operations
│   │   ├── PatchService.cs             # Patch download & application logic
│   │   ├── PerformanceMonitor.cs       # System health metrics collector
│   │   ├── CDKApplctionStatusService.cs # CDK terminal status monitor
│   │   ├── GetFolderStructure.cs       # Local folder structure builder
│   │   ├── LoggerService.cs            # Branch-side logging
│   │   ├── SqliteService.cs            # Local SQLite storage
│   │   └── GracefulStartup.cs          # Service startup orchestration
│   └── appsettings.json                # Branch configuration
│
├── BranchConnectVNCProxyAPI/            # 🖥️ VNC WebSocket Proxy
│   └── BranchConnectVNCProxyAPI/
│       ├── Program.cs                   # WebSocket middleware setup
│       ├── VncWebSocketProxy.cs         # WebSocket ↔ TCP VNC bridge
│       └── VncProxyFileLogger.cs        # VNC session logging
│
├── Monitoring.Shared/                   # 📦 Shared Library
│   ├── Models/                          # Entity Framework models
│   │   ├── Branch.cs                    # Branch/terminal entity
│   │   ├── Job.cs                       # Job tracking entity
│   │   ├── NewPatch.cs                  # Patch metadata entity
│   │   ├── PatchAssignBranch.cs         # Patch-to-branch assignment
│   │   ├── JobAssignBranch.cs           # Job-to-branch assignment
│   │   ├── JobStatus.cs / JobType.cs    # Job status & type lookups
│   │   ├── PatcheType.cs               # Patch type definitions
│   │   ├── BranchRemot.cs              # Branch remote access config
│   │   ├── SFTPFolderPath.cs            # SFTP path configuration
│   │   └── ServerFolderPath.cs          # Server folder configuration
│   ├── DTO/                             # Shared DTOs
│   │   ├── PerformanceInfo.cs           # CPU, RAM, Disk, Network DTOs
│   │   ├── BranchJobRequest.cs          # Job request/response models
│   │   ├── FolderNode.cs                # File tree node DTO
│   │   └── APIResponse.cs              # Standard API response wrapper
│   └── Enum/                            # Shared enumerations
│       ├── BranchStatusEnum.cs          # CDK, MQTT, Service, DB status
│       ├── PatchEnum.cs                 # Patch lifecycle states & steps
│       ├── TerminalActiveEnum.cs        # Terminal online/offline
│       └── WorkerServiceEnum.cs         # Worker service states
│
├── Documentation/                       # 📖 Project Documentation
│   ├── Security-Proposal-Client.md      # 4-level security implementation guide
│   └── Server-Setup-Guide.md           # Production deployment manual
│
├── Installer/                           # 📦 Installation Scripts
│   ├── Build-Installers.ps1             # Automated build & installer script
│   ├── README.md                        # Installer documentation
│   ├── Branch/                          # Branch agent installer
│   └── Server/                          # Server-side installer
│
└── Setup/                               # ⚙️ Setup Configuration
    ├── README.md                        # Setup documentation
    ├── Branch/                          # Branch setup scripts
    └── Server/                          # Server setup scripts
```

---

## 🚀 Getting Started

### Prerequisites

| Software       | Version                   | Required           |
| -------------- | ------------------------- | ------------------ |
| .NET SDK       | 8.0+                      | ✅                 |
| SQL Server     | 2019+ (Express OK)        | ✅                 |
| Mosquitto MQTT | 2.0+                      | ✅                 |
| Visual Studio  | 2022+                     | Recommended        |
| OpenSSH Server | Built-in on Windows       | For SFTP           |
| VNC Server     | Any (on target terminals) | For Remote Desktop |

### Quick Start

#### 1. Clone the Repository

```bash
git clone https://github.com/Thimeee/KioskMonitoringSolutionProduction.git
cd KioskMonitoringSolutionProduction
```

#### 2. Set Up the Database

```sql
-- Connect to SQL Server
CREATE DATABASE MonitoringKiosk;
```

#### 3. Configure appsettings.json

Update `MonitoringBackend/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=MonitoringKiosk;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  },
  "MQTT": {
    "Host": "YOUR_MQTT_HOST",
    "Port": "1883",
    "Username": "MQTTUser",
    "Password": "YOUR_MQTT_PASSWORD"
  }
}
```

#### 4. Apply Database Migrations

```bash
cd MonitoringBackend
dotnet ef database update
```

#### 5. Run the Solution

```bash
# Option 1: Visual Studio — Set multiple startup projects
# Option 2: Command line
dotnet run --project MonitoringBackend
dotnet run --project BranchMonitorFrontEnd/BranchMonitorFrontEnd
dotnet run --project BranchConnectVNCProxyAPI/BranchConnectVNCProxyAPI
```

#### 6. Access Dashboard

Open your browser and navigate to:

```
http://localhost:5235
```

---

## ⚙️ Configuration

### Server Configuration (`MonitoringBackend/appsettings.json`)

| Key                                   | Description                    | Example                                         |
| ------------------------------------- | ------------------------------ | ----------------------------------------------- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string   | `Server=localhost;Database=MonitoringKiosk;...` |
| `Jwt:Key`                             | JWT signing key (64+ chars)    | Base64 encoded key                              |
| `Jwt:Issuer`                          | Token issuer URL               | `http://localhost:5155/`                        |
| `Jwt:Audience`                        | Token audience URL             | `http://localhost:5235/`                        |
| `MQTT:Host`                           | MQTT broker hostname           | `192.168.1.24`                                  |
| `MQTT:Port`                           | MQTT broker port               | `1883` (plain) / `8883` (TLS)                   |
| `MQTT:Username`                       | MQTT authentication username   | `MQTTUser`                                      |
| `MQTT:Password`                       | MQTT authentication password   | `mcs@1234`                                      |
| `ServerConfig:ServerTerminalsPath`    | Path for terminal data storage | `C:\Monitoring\Terminals\`                      |
| `AllowedOrigins`                      | CORS allowed origins           | `["http://localhost:5235"]`                     |

### Branch Agent Configuration (`SFTPService/appsettings.json`)

| Key         | Description                                |
| ----------- | ------------------------------------------ |
| `BranchId`  | Unique identifier for this branch terminal |
| `MQTT:Host` | Central server MQTT broker address         |
| `MQTT:Port` | MQTT broker port                           |
| `SFTP:*`    | SFTP server connection details             |

---

## 📡 API Reference

### Authentication

| Method | Endpoint             | Description                        |
| ------ | -------------------- | ---------------------------------- |
| POST   | `/api/Auth/login`    | Authenticate and receive JWT token |
| POST   | `/api/Auth/register` | Register new user account          |

### Branch Management

| Method | Endpoint           | Description                  |
| ------ | ------------------ | ---------------------------- |
| GET    | `/api/Branch`      | Get all branches with status |
| GET    | `/api/Branch/{id}` | Get branch details           |
| POST   | `/api/Branch`      | Register new branch          |
| PUT    | `/api/Branch/{id}` | Update branch configuration  |
| DELETE | `/api/Branch/{id}` | Remove a branch              |

### Patch Management

| Method | Endpoint                   | Description                  |
| ------ | -------------------------- | ---------------------------- |
| GET    | `/api/Patches`             | List all available patches   |
| POST   | `/api/Patches/upload`      | Upload new patch (chunked)   |
| POST   | `/api/Patches/deploy`      | Deploy patch to branch       |
| POST   | `/api/Patches/deploy-all`  | Deploy patch to all branches |
| POST   | `/api/Patches/schedule`    | Schedule patch deployment    |
| GET    | `/api/Patches/status/{id}` | Get patch deployment status  |

### File Operations (SFTP)

| Method | Endpoint                  | Description               |
| ------ | ------------------------- | ------------------------- |
| POST   | `/api/SFTPFiles/browse`   | Browse branch file system |
| POST   | `/api/SFTPFiles/upload`   | Upload file to branch     |
| POST   | `/api/SFTPFiles/download` | Download file from branch |
| POST   | `/api/SFTPFiles/delete`   | Delete file on branch     |

### Remote Access

| Method | Endpoint                 | Description                |
| ------ | ------------------------ | -------------------------- |
| GET    | `/api/Remote/{branchId}` | Get VNC connection details |

### Health Check

| Method | Endpoint                         | Description                     |
| ------ | -------------------------------- | ------------------------------- |
| GET    | `/api/Health`                    | Server health status            |
| POST   | `/api/Health/request/{branchId}` | Request branch performance data |

---

## 📬 MQTT Topic Structure

### Server → Branch (Commands)

```
branch/{terminalId}/SFTP/FolderStucher       # Request folder structure
branch/{terminalId}/SFTP/Upload              # Initiate file upload
branch/{terminalId}/SFTP/Download            # Initiate file download
branch/{terminalId}/SFTP/Delete              # Delete file command
branch/{terminalId}/HEALTH/PerformanceReq    # Request performance data
branch/{terminalId}/PATCH/Application        # Deploy patch command
```

### Branch → Server (Responses & Status)

```
server/{terminalId}/STATUS/MQTTStatus        # MQTT connection status (Online/Offline)
server/{terminalId}/STATUS/ServiceStatus     # Worker service status
server/{terminalId}/STATUS/CDKErrorStatus    # CDK application error status
server/{terminalId}/STATUS/DBStatus          # Database connection status
server/{terminalId}/SFTP/FolderStucherResponse   # Folder structure response
server/{terminalId}/SFTP/UploadResponse      # Upload completion response
server/{terminalId}/SFTP/DownloadResponse    # Download completion response
server/{terminalId}/SFTP/DownloadProgress    # Download progress updates
server/{terminalId}/SFTP/UploadProgress      # Upload progress updates
server/{terminalId}/SFTP/DeleteResponse      # Delete completion response
server/{terminalId}/HEALTH/PerformanceRespo  # Performance metrics payload
server/{terminalId}/PATCH/*                  # Patch deployment status updates
server/mainServer/PATCHPROCESS               # Server-side patch processing
```

### SignalR Events (Server → Dashboard)

| Event                                         | Description                         |
| --------------------------------------------- | ----------------------------------- |
| `TerminalStatus`                              | Branch online/offline status change |
| `PerformanceUpdate`                           | Real-time performance metrics       |
| `BranchUpdate`                                | Folder structure response           |
| `UploadFile` / `DownloadFile`                 | File transfer completion            |
| `UploadFileProgress` / `DownloadFileProgress` | Transfer progress                   |
| `DeleteResponse`                              | File deletion result                |
| `SingleBranchPatchResponse`                   | Single branch patch progress        |
| `AllBranchPatchResponse`                      | Bulk patch deployment progress      |
| `PatchDeploymentComplete`                     | Patch upload finalized              |
| `PatchDeploymentFailed`                       | Patch upload failure                |

---

## 🚢 Deployment

### Production Architecture

```
┌──────────────────────────────────────────────────────────┐
│              WINDOWS SERVER (IIS 10+)                    │
│                                                          │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ IIS App Pool:    │  │ IIS App Pool:                │ │
│  │ MonitoringAPI    │  │ MonitoringUI                 │ │
│  │ (Port 443/API)   │  │ (Port 443/UI)                │ │
│  └──────────────────┘  └──────────────────────────────┘ │
│                                                          │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ IIS App Pool:    │  │ Windows Services:            │ │
│  │ VNCProxy         │  │ • Mosquitto MQTT (8883)      │ │
│  │ (Port 7128)      │  │ • OpenSSH SFTP (22)          │ │
│  └──────────────────┘  │ • SQL Server (1433)          │ │
│                         └──────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

Detailed deployment instructions are available in:

- 📖 [`Documentation/Server-Setup-Guide.md`](Documentation/Server-Setup-Guide.md) — Step-by-step server setup
- 📦 [`Installer/README.md`](Installer/README.md) — Automated installer documentation
- ⚙️ [`Setup/README.md`](Setup/README.md) — Initial setup guide

### Quick Deploy

```powershell
# Build all projects for release
dotnet publish MonitoringBackend -c Release -o C:\inetpub\wwwroot\MonitoringAPI
dotnet publish BranchMonitorFrontEnd\BranchMonitorFrontEnd -c Release -o C:\inetpub\wwwroot\MonitoringUI
dotnet publish BranchConnectVNCProxyAPI\BranchConnectVNCProxyAPI -c Release -o C:\inetpub\wwwroot\VNCProxy

# Install branch agent as Windows Service
dotnet publish SFTPService -c Release -o C:\MonitoringAgent
sc.exe create "BranchMonitorAgent" binPath="C:\MonitoringAgent\SFTPService.exe"
sc.exe start "BranchMonitorAgent"
```

---

## 🔐 Security

This project includes a comprehensive **4-level security model** documented in [`Documentation/Security-Proposal-Client.md`](Documentation/Security-Proposal-Client.md):

| Level                   | Target              | Features                                  |
| ----------------------- | ------------------- | ----------------------------------------- |
| **Level 1: Basic**      | Development/Testing | Plain text, anonymous access              |
| **Level 2: Standard**   | Small Business      | TLS encryption, JWT auth, RBAC            |
| **Level 3: Advanced**   | Enterprise          | mTLS, certificate auth, IP whitelisting   |
| **Level 4: Enterprise** | Banking/Financial   | HSM, zero-trust, SIEM, PCI-DSS compliance |

### Security features implemented:

- ✅ JWT Bearer Token authentication
- ✅ Role-based authorization (Admin, Branch roles)
- ✅ CORS policy restrictions
- ✅ MQTT username/password with ACL
- ✅ SFTP with SSH key authentication support
- ✅ TLS/SSL support for all communication channels
- ✅ Chunked file transfer with SHA-256 checksum verification
- ✅ Configurable connection limits and throttling

---

## 📖 Documentation

| Document                                                       | Description                                                                  |
| -------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| [Server Setup Guide](Documentation/Server-Setup-Guide.md)      | Complete production deployment manual with MQTT, SFTP, SQL Server, IIS setup |
| [Security Proposal](Documentation/Security-Proposal-Client.md) | 4-level security implementation options for MQTT, SFTP, API, DB, and Network |
| [Installer Guide](Installer/README.md)                         | Automated build & installer script documentation                             |
| [Setup Guide](Setup/README.md)                                 | Initial setup and configuration guide                                        |

---

## 🤝 Contributing

Contributions are welcome! Please follow these steps:

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 👤 Author

**Thimeee**

- GitHub: [@Thimeee](https://github.com/Thimeee)

---

<p align="center">
  <b>⭐ If you found this project helpful, please give it a star! ⭐</b>
</p>
