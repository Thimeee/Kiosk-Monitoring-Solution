# Kiosk Monitoring Solution - Server Setup Guide

## Production Deployment Manual

**Version:** 1.0
**Last Updated:** February 2026
**Target OS:** Windows Server 2019/2022

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [MQTT Broker Setup (Mosquitto)](#2-mqtt-broker-setup-mosquitto)
3. [OpenSSH SFTP Server Setup](#3-openssh-sftp-server-setup)
4. [SQL Server Database Setup](#4-sql-server-database-setup)
5. [IIS Installation & Configuration](#5-iis-installation--configuration)
6. [MonitoringBackend API Deployment](#6-monitoringbackend-api-deployment)
7. [BranchMonitorFrontEnd Deployment](#7-branchmonitorfrontend-deployment)
8. [VNC Proxy API Deployment](#8-vnc-proxy-api-deployment)
9. [SSL/TLS Certificate Configuration](#9-ssltls-certificate-configuration)
10. [Firewall Configuration](#10-firewall-configuration)
11. [Service Monitoring & Health Checks](#11-service-monitoring--health-checks)
12. [Backup & Recovery](#12-backup--recovery)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Prerequisites

### 1.1 Hardware Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| CPU | 4 Cores | 8 Cores |
| RAM | 8 GB | 16 GB |
| Storage | 100 GB SSD | 500 GB SSD |
| Network | 100 Mbps | 1 Gbps |

### 1.2 Software Requirements

- Windows Server 2019/2022
- .NET 8.0 Runtime & Hosting Bundle
- SQL Server 2019/2022
- IIS 10.0+
- Mosquitto MQTT Broker
- OpenSSH Server

### 1.3 Network Requirements

| Service | Port | Protocol | Purpose |
|---------|------|----------|---------|
| MQTT (TLS) | 8883 | TCP | Secure MQTT communication |
| MQTT (Plain) | 1883 | TCP | Internal MQTT (optional) |
| SFTP | 22 | TCP | Secure file transfer |
| HTTPS API | 443 | TCP | Backend API |
| HTTP API | 80 | TCP | Redirect to HTTPS |
| SignalR | 443 | TCP/WebSocket | Real-time updates |
| VNC Proxy | 7128 | TCP/WebSocket | Remote desktop |

### 1.4 Download Required Software

```powershell
# Create downloads folder
New-Item -ItemType Directory -Path "C:\ServerSetup\Downloads" -Force
cd C:\ServerSetup\Downloads

# Download .NET 8.0 Hosting Bundle
Invoke-WebRequest -Uri "https://download.visualstudio.microsoft.com/download/pr/hosting-bundle-8.0.latest.exe" -OutFile "dotnet-hosting-8.0.exe"

# Download Mosquitto
Invoke-WebRequest -Uri "https://mosquitto.org/files/binary/win64/mosquitto-2.0.18-install-windows-x64.exe" -OutFile "mosquitto-setup.exe"
```

---

## 2. MQTT Broker Setup (Mosquitto)

### 2.1 Install Mosquitto

1. **Run the installer:**
   ```powershell
   # Run as Administrator
   Start-Process -FilePath "C:\ServerSetup\Downloads\mosquitto-setup.exe" -Wait
   ```

2. **Default installation path:** `C:\Program Files\mosquitto`

3. **Verify installation:**
   ```powershell
   & "C:\Program Files\mosquitto\mosquitto.exe" -h
   ```

### 2.2 Create Directory Structure

```powershell
# Create directories
New-Item -ItemType Directory -Path "C:\Mosquitto\config" -Force
New-Item -ItemType Directory -Path "C:\Mosquitto\data" -Force
New-Item -ItemType Directory -Path "C:\Mosquitto\log" -Force
New-Item -ItemType Directory -Path "C:\Mosquitto\certs" -Force
New-Item -ItemType Directory -Path "C:\Mosquitto\passwords" -Force
```

### 2.3 Generate SSL Certificates

```powershell
# Navigate to certs directory
cd C:\Mosquitto\certs

# Install OpenSSL if not available (via chocolatey)
# choco install openssl -y

# Or download OpenSSL from: https://slproweb.com/products/Win32OpenSSL.html
# Add to PATH: C:\Program Files\OpenSSL-Win64\bin

# Generate CA Certificate
openssl genrsa -out ca.key 4096
openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 -out ca.crt -subj "/C=LK/ST=Western/L=Colombo/O=YourCompany/CN=Monitoring-CA"

# Generate Server Certificate
openssl genrsa -out mqtt-server.key 2048
openssl req -new -key mqtt-server.key -out mqtt-server.csr -subj "/C=LK/ST=Western/L=Colombo/O=YourCompany/CN=mqtt.yourdomain.com"
openssl x509 -req -in mqtt-server.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out mqtt-server.crt -days 365 -sha256

# Generate Client Certificate for Backend
openssl genrsa -out backend-client.key 2048
openssl req -new -key backend-client.key -out backend-client.csr -subj "/C=LK/ST=Western/L=Colombo/O=YourCompany/CN=MonitoringBackend"
openssl x509 -req -in backend-client.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out backend-client.crt -days 365 -sha256

# Set permissions (restrict access to keys)
icacls "C:\Mosquitto\certs\*.key" /inheritance:r /grant:r "SYSTEM:(R)" /grant:r "Administrators:(R)"
```

### 2.4 Create Password File

```powershell
# Navigate to Mosquitto installation
cd "C:\Program Files\mosquitto"

# Create password file (will prompt for password)
.\mosquitto_passwd.exe -c "C:\Mosquitto\passwords\passwd" MonitoringBackend
.\mosquitto_passwd.exe "C:\Mosquitto\passwords\passwd" BranchService

# Verify password file created
Get-Content "C:\Mosquitto\passwords\passwd"
```

### 2.5 Create ACL File

Create file: `C:\Mosquitto\config\acl.conf`

```
# MonitoringBackend - Full access to all topics
user MonitoringBackend
topic readwrite #

# BranchService - Access only to branch-specific topics
user BranchService
pattern readwrite branch/%u/#
pattern read broadcast/#
pattern read config/#

# Deny all other access by default
topic read $SYS/#
```

### 2.6 Configure Mosquitto

Create file: `C:\Mosquitto\config\mosquitto.conf`

```conf
# ===========================================
# Mosquitto Configuration - Production
# ===========================================

# Persistence
persistence true
persistence_location C:\Mosquitto\data\

# Logging
log_dest file C:\Mosquitto\log\mosquitto.log
log_type error
log_type warning
log_type notice
log_type information
log_timestamp true
log_timestamp_format %Y-%m-%d %H:%M:%S

# ===========================================
# Listener Configuration
# ===========================================

# TLS Listener (Primary - Production)
listener 8883
protocol mqtt
cafile C:\Mosquitto\certs\ca.crt
certfile C:\Mosquitto\certs\mqtt-server.crt
keyfile C:\Mosquitto\certs\mqtt-server.key
require_certificate false
tls_version tlsv1.2

# Plain Listener (Internal only - Optional)
# listener 1883 127.0.0.1
# protocol mqtt

# WebSocket Listener (For web clients)
listener 8884
protocol websockets
cafile C:\Mosquitto\certs\ca.crt
certfile C:\Mosquitto\certs\mqtt-server.crt
keyfile C:\Mosquitto\certs\mqtt-server.key

# ===========================================
# Authentication & Authorization
# ===========================================

# Disable anonymous access
allow_anonymous false

# Password file
password_file C:\Mosquitto\passwords\passwd

# Access Control List
acl_file C:\Mosquitto\config\acl.conf

# ===========================================
# Connection Limits
# ===========================================

max_connections 1000
max_inflight_messages 20
max_queued_messages 1000
max_queued_bytes 0

# Message size limit (1MB)
message_size_limit 1048576

# ===========================================
# Keep Alive & Timeouts
# ===========================================

# Maximum keepalive interval
max_keepalive 300

# Persistent client expiration (7 days)
persistent_client_expiration 7d

# ===========================================
# Security Hardening
# ===========================================

# Retry interval for connections
retry_interval 20

# Store clean session clients
set_tcp_nodelay true
```

### 2.7 Install & Configure Windows Service

```powershell
# Stop existing service if running
Stop-Service mosquitto -ErrorAction SilentlyContinue

# Remove existing service
& "C:\Program Files\mosquitto\mosquitto.exe" uninstall

# Install as Windows Service with custom config
& "C:\Program Files\mosquitto\mosquitto.exe" install

# Modify service to use custom config
# Open Services (services.msc) and modify the service path to:
# "C:\Program Files\mosquitto\mosquitto.exe" -c "C:\Mosquitto\config\mosquitto.conf"

# Or use sc command
sc.exe config mosquitto binPath= "\"C:\Program Files\mosquitto\mosquitto.exe\" -c \"C:\Mosquitto\config\mosquitto.conf\""

# Set service to auto-start
Set-Service -Name mosquitto -StartupType Automatic

# Start service
Start-Service mosquitto

# Verify service status
Get-Service mosquitto
```

### 2.8 Test MQTT Connection

```powershell
# Test without TLS (if enabled)
& "C:\Program Files\mosquitto\mosquitto_pub.exe" -h localhost -p 1883 -u MonitoringBackend -P "YourPassword" -t "test/topic" -m "Hello MQTT"

# Test with TLS
& "C:\Program Files\mosquitto\mosquitto_pub.exe" -h localhost -p 8883 -u MonitoringBackend -P "YourPassword" --cafile "C:\Mosquitto\certs\ca.crt" -t "test/topic" -m "Hello Secure MQTT"

# Subscribe to test
& "C:\Program Files\mosquitto\mosquitto_sub.exe" -h localhost -p 8883 -u MonitoringBackend -P "YourPassword" --cafile "C:\Mosquitto\certs\ca.crt" -t "test/#"
```

### 2.9 MQTT Monitoring Dashboard (Optional)

```powershell
# Install MQTT Explorer for monitoring
# Download from: https://mqtt-explorer.com/

# Or use command line monitoring
& "C:\Program Files\mosquitto\mosquitto_sub.exe" -h localhost -p 8883 -u MonitoringBackend -P "YourPassword" --cafile "C:\Mosquitto\certs\ca.crt" -t "#" -v
```

---

## 3. OpenSSH SFTP Server Setup

### 3.1 Install OpenSSH Server

```powershell
# Check if OpenSSH is available
Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH*'

# Install OpenSSH Server
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0

# Install OpenSSH Client (for testing)
Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0

# Verify installation
Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH*'
```

### 3.2 Create SFTP Directory Structure

```powershell
# Create SFTP root directory
New-Item -ItemType Directory -Path "C:\SFTP" -Force
New-Item -ItemType Directory -Path "C:\SFTP\Patches" -Force
New-Item -ItemType Directory -Path "C:\SFTP\Patches\Downloads" -Force
New-Item -ItemType Directory -Path "C:\SFTP\Patches\Uploads" -Force
New-Item -ItemType Directory -Path "C:\SFTP\Logs" -Force
New-Item -ItemType Directory -Path "C:\SFTP\Terminals" -Force

# Create SSH keys directory
New-Item -ItemType Directory -Path "C:\ProgramData\ssh\keys" -Force
```

### 3.3 Create SFTP User Account

```powershell
# Create local user for SFTP access
$Password = Read-Host -AsSecureString "Enter password for sftpuser"
New-LocalUser -Name "sftpuser" -Password $Password -FullName "SFTP Service User" -Description "SFTP access for Monitoring System" -PasswordNeverExpires

# Add to Users group (not Administrators)
Add-LocalGroupMember -Group "Users" -Member "sftpuser"

# Create user's home directory
New-Item -ItemType Directory -Path "C:\Users\sftpuser\.ssh" -Force
```

### 3.4 Generate SSH Keys for Authentication

```powershell
# Generate server host keys (if not exists)
cd "C:\ProgramData\ssh"

# Generate ED25519 key (recommended)
ssh-keygen -t ed25519 -f ssh_host_ed25519_key -N '""'

# Generate RSA key (for compatibility)
ssh-keygen -t rsa -b 4096 -f ssh_host_rsa_key -N '""'

# Generate client key for branch authentication
ssh-keygen -t ed25519 -f "C:\ProgramData\ssh\keys\branch_client_key" -N '""' -C "branch-client@monitoring"

# Display public key (distribute to branches)
Get-Content "C:\ProgramData\ssh\keys\branch_client_key.pub"
```

### 3.5 Configure OpenSSH Server

Edit file: `C:\ProgramData\ssh\sshd_config`

```conf
# ===========================================
# OpenSSH Server Configuration - Production
# ===========================================

# Port and Protocol
Port 22
AddressFamily inet
ListenAddress 0.0.0.0

# Host Keys
HostKey __PROGRAMDATA__/ssh/ssh_host_ed25519_key
HostKey __PROGRAMDATA__/ssh/ssh_host_rsa_key

# ===========================================
# Authentication
# ===========================================

# Disable root login
PermitRootLogin no

# Enable public key authentication
PubkeyAuthentication yes
AuthorizedKeysFile .ssh/authorized_keys

# Disable password authentication (after setting up keys)
# PasswordAuthentication no
# For initial setup, keep password enabled
PasswordAuthentication yes

# Disable empty passwords
PermitEmptyPasswords no

# Disable challenge-response authentication
ChallengeResponseAuthentication no

# ===========================================
# Security Hardening
# ===========================================

# Maximum authentication attempts
MaxAuthTries 3

# Login grace time
LoginGraceTime 60

# Client alive interval (detect disconnected clients)
ClientAliveInterval 300
ClientAliveCountMax 2

# Maximum sessions
MaxSessions 100

# Maximum startups (prevent DoS)
MaxStartups 10:30:100

# Disable TCP forwarding
AllowTcpForwarding no
GatewayPorts no

# Disable X11 forwarding
X11Forwarding no

# Disable agent forwarding
AllowAgentForwarding no

# Disable tunneling
PermitTunnel no

# ===========================================
# Logging
# ===========================================

SyslogFacility LOCAL0
LogLevel INFO

# ===========================================
# SFTP Configuration
# ===========================================

# Internal SFTP server (more secure)
Subsystem sftp sftp-server.exe

# ===========================================
# User/Group Restrictions
# ===========================================

# Allow only specific users
AllowUsers sftpuser

# SFTP-only configuration for sftpuser
Match User sftpuser
    ForceCommand internal-sftp
    ChrootDirectory C:\SFTP
    PermitTunnel no
    AllowAgentForwarding no
    AllowTcpForwarding no
    X11Forwarding no

# ===========================================
# Ciphers and MACs (Strong only)
# ===========================================

Ciphers aes256-gcm@openssh.com,chacha20-poly1305@openssh.com,aes256-ctr
MACs hmac-sha2-512-etm@openssh.com,hmac-sha2-256-etm@openssh.com,hmac-sha2-512,hmac-sha2-256
KexAlgorithms curve25519-sha256,curve25519-sha256@libssh.org,diffie-hellman-group16-sha512,diffie-hellman-group18-sha512
```

### 3.6 Set SFTP Directory Permissions

```powershell
# Set ownership and permissions for SFTP root
# The ChrootDirectory must be owned by root (SYSTEM) and not writable by others

# Take ownership
takeown /F "C:\SFTP" /R /A

# Reset permissions
icacls "C:\SFTP" /reset /T

# Set permissions for Chroot directory (must be owned by SYSTEM/Administrators only)
icacls "C:\SFTP" /inheritance:r
icacls "C:\SFTP" /grant:r "SYSTEM:(OI)(CI)F"
icacls "C:\SFTP" /grant:r "Administrators:(OI)(CI)F"

# Allow sftpuser to read the root
icacls "C:\SFTP" /grant:r "sftpuser:(RX)"

# Allow sftpuser full control of subdirectories
icacls "C:\SFTP\Patches" /grant:r "sftpuser:(OI)(CI)F"
icacls "C:\SFTP\Terminals" /grant:r "sftpuser:(OI)(CI)F"
icacls "C:\SFTP\Logs" /grant:r "sftpuser:(OI)(CI)F"
```

### 3.7 Configure Authorized Keys

```powershell
# Create authorized_keys file for sftpuser
$authorizedKeysPath = "C:\Users\sftpuser\.ssh\authorized_keys"

# Add the branch client public key
$publicKey = Get-Content "C:\ProgramData\ssh\keys\branch_client_key.pub"
Set-Content -Path $authorizedKeysPath -Value $publicKey

# Set correct permissions
icacls $authorizedKeysPath /inheritance:r
icacls $authorizedKeysPath /grant:r "sftpuser:(R)"
icacls $authorizedKeysPath /grant:r "SYSTEM:(F)"
icacls $authorizedKeysPath /grant:r "Administrators:(F)"
```

### 3.8 Start and Configure SSH Service

```powershell
# Set service to automatic start
Set-Service -Name sshd -StartupType Automatic

# Start the service
Start-Service sshd

# Verify service is running
Get-Service sshd

# Check SSH is listening
netstat -an | findstr ":22"
```

### 3.9 Test SFTP Connection

```powershell
# Test with password (initial)
sftp sftpuser@localhost

# Test with key
sftp -i "C:\ProgramData\ssh\keys\branch_client_key" sftpuser@localhost

# List files
# sftp> ls
# sftp> cd Patches
# sftp> ls
# sftp> exit
```

### 3.10 Get Server Host Key Fingerprint

```powershell
# Get the server's host key fingerprint (distribute to branches for verification)
ssh-keygen -l -f "C:\ProgramData\ssh\ssh_host_ed25519_key.pub"

# Example output: SHA256:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
# Save this fingerprint for branch configuration
```

---

## 4. SQL Server Database Setup

### 4.1 Install SQL Server (if not installed)

Download SQL Server 2022 Express or Standard from Microsoft.

### 4.2 Create Database and User

```sql
-- Connect to SQL Server as SA or admin

-- Create Database
CREATE DATABASE MonitoringKiosk;
GO

-- Create Login for application
CREATE LOGIN MonitoringAppUser WITH PASSWORD = 'StrongPassword@2024!';
GO

-- Use the database
USE MonitoringKiosk;
GO

-- Create User
CREATE USER MonitoringAppUser FOR LOGIN MonitoringAppUser;
GO

-- Grant permissions
ALTER ROLE db_datareader ADD MEMBER MonitoringAppUser;
ALTER ROLE db_datawriter ADD MEMBER MonitoringAppUser;
GRANT EXECUTE TO MonitoringAppUser;
GO

-- For EF Core migrations (development only)
-- ALTER ROLE db_ddladmin ADD MEMBER MonitoringAppUser;
```

### 4.3 Configure SQL Server for Remote Connections

```powershell
# Enable TCP/IP protocol
# Open SQL Server Configuration Manager
# SQL Server Network Configuration > Protocols for MSSQLSERVER
# Enable TCP/IP

# Set TCP Port (default 1433)
# TCP/IP Properties > IP Addresses > IPAll > TCP Port = 1433

# Restart SQL Server service
Restart-Service MSSQLSERVER
```

### 4.4 Run EF Core Migrations

```powershell
# Navigate to MonitoringBackend project
cd "D:\Self Stady\CDK Monitring System\gthubProject\KioskMonitoringSolution\MonitoringBackend"

# Apply migrations
dotnet ef database update --connection "Server=localhost;Database=MonitoringKiosk;User Id=MonitoringAppUser;Password=StrongPassword@2024!;TrustServerCertificate=True;"
```

---

## 5. IIS Installation & Configuration

### 5.1 Install IIS Features

```powershell
# Install IIS with required features
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServer
Enable-WindowsOptionalFeature -Online -FeatureName IIS-CommonHttpFeatures
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpErrors
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpRedirect
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationDevelopment
Enable-WindowsOptionalFeature -Online -FeatureName IIS-NetFxExtensibility45
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HealthAndDiagnostics
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpLogging
Enable-WindowsOptionalFeature -Online -FeatureName IIS-LoggingLibraries
Enable-WindowsOptionalFeature -Online -FeatureName IIS-RequestMonitor
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpTracing
Enable-WindowsOptionalFeature -Online -FeatureName IIS-Security
Enable-WindowsOptionalFeature -Online -FeatureName IIS-RequestFiltering
Enable-WindowsOptionalFeature -Online -FeatureName IIS-Performance
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerManagementTools
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ManagementConsole
Enable-WindowsOptionalFeature -Online -FeatureName IIS-IIS6ManagementCompatibility
Enable-WindowsOptionalFeature -Online -FeatureName IIS-Metabase
Enable-WindowsOptionalFeature -Online -FeatureName IIS-StaticContent
Enable-WindowsOptionalFeature -Online -FeatureName IIS-DefaultDocument
Enable-WindowsOptionalFeature -Online -FeatureName IIS-DirectoryBrowsing
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ASPNET45
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ISAPIExtensions
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ISAPIFilter
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpCompressionStatic
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpCompressionDynamic

# Restart computer if required
# Restart-Computer
```

### 5.2 Install .NET 8.0 Hosting Bundle

```powershell
# Download and install .NET 8.0 Hosting Bundle
# This includes ASP.NET Core Runtime and IIS Module

Start-Process -FilePath "C:\ServerSetup\Downloads\dotnet-hosting-8.0.exe" -ArgumentList "/quiet" -Wait

# Restart IIS
iisreset /restart

# Verify .NET Core Module is installed
Get-WebGlobalModule | Where-Object { $_.Name -like "*AspNetCore*" }
```

### 5.3 Create IIS Directory Structure

```powershell
# Create web application directories
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\MonitoringAPI" -Force
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\MonitoringUI" -Force
New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\VNCProxy" -Force

# Create logs directory
New-Item -ItemType Directory -Path "C:\inetpub\logs\MonitoringAPI" -Force
New-Item -ItemType Directory -Path "C:\inetpub\logs\MonitoringUI" -Force
New-Item -ItemType Directory -Path "C:\inetpub\logs\VNCProxy" -Force
```

### 5.4 Create Application Pool

```powershell
Import-Module WebAdministration

# Create Application Pool for API
New-WebAppPool -Name "MonitoringAPIPool"
Set-ItemProperty "IIS:\AppPools\MonitoringAPIPool" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\MonitoringAPIPool" -Name "startMode" -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\MonitoringAPIPool" -Name "processModel.idleTimeout" -Value "00:00:00"
Set-ItemProperty "IIS:\AppPools\MonitoringAPIPool" -Name "recycling.periodicRestart.time" -Value "00:00:00"

# Create Application Pool for UI
New-WebAppPool -Name "MonitoringUIPool"
Set-ItemProperty "IIS:\AppPools\MonitoringUIPool" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\MonitoringUIPool" -Name "startMode" -Value "AlwaysRunning"

# Create Application Pool for VNC Proxy
New-WebAppPool -Name "VNCProxyPool"
Set-ItemProperty "IIS:\AppPools\VNCProxyPool" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\VNCProxyPool" -Name "startMode" -Value "AlwaysRunning"
```

---

## 6. MonitoringBackend API Deployment

### 6.1 Build and Publish

```powershell
# Navigate to project directory
cd "D:\Self Stady\CDK Monitring System\gthubProject\KioskMonitoringSolution\MonitoringBackend"

# Restore packages
dotnet restore

# Publish for production
dotnet publish -c Release -o "C:\inetpub\wwwroot\MonitoringAPI" --self-contained false
```

### 6.2 Create Production Configuration

Create file: `C:\inetpub\wwwroot\MonitoringAPI\appsettings.Production.json`

```json
{
    "ConnectionStrings": {
        "DefaultConnection": "Server=localhost;Database=MonitoringKiosk;User Id=MonitoringAppUser;Password=StrongPassword@2024!;TrustServerCertificate=True;Encrypt=True;"
    },
    "Jwt": {
        "Key": "YOUR-PRODUCTION-SECRET-KEY-MIN-64-CHARACTERS-LONG-CHANGE-THIS!!!",
        "Issuer": "https://api.yourdomain.com/",
        "Audience": "https://monitoring.yourdomain.com/"
    },
    "Logging": {
        "LogLevel": {
            "Default": "Warning",
            "Microsoft.AspNetCore": "Warning",
            "Microsoft.EntityFrameworkCore": "Warning"
        }
    },
    "AllowedOrigins": [
        "https://monitoring.yourdomain.com"
    ],
    "MQTT": {
        "Host": "localhost",
        "Port": "8883",
        "UseTls": true,
        "Username": "MonitoringBackend",
        "CaCertPath": "C:\\Mosquitto\\certs\\ca.crt",
        "ClientCertPath": "C:\\Mosquitto\\certs\\backend-client.crt",
        "ClientKeyPath": "C:\\Mosquitto\\certs\\backend-client.key"
    },
    "ServerConfig": {
        "ServerTerminalsPath": "C:\\SFTP\\Terminals\\"
    },
    "AllowedHosts": "api.yourdomain.com;localhost"
}
```

### 6.3 Create web.config

Create/Update file: `C:\inetpub\wwwroot\MonitoringAPI\web.config`

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet"
                  arguments=".\MonitoringBackend.dll"
                  stdoutLogEnabled="true"
                  stdoutLogFile="C:\inetpub\logs\MonitoringAPI\stdout"
                  hostingModel="InProcess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>

      <!-- Security Headers -->
      <httpProtocol>
        <customHeaders>
          <add name="X-Frame-Options" value="DENY" />
          <add name="X-Content-Type-Options" value="nosniff" />
          <add name="X-XSS-Protection" value="1; mode=block" />
          <add name="Referrer-Policy" value="strict-origin-when-cross-origin" />
          <add name="Content-Security-Policy" value="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';" />
          <remove name="X-Powered-By" />
        </customHeaders>
      </httpProtocol>

      <!-- Request Filtering -->
      <security>
        <requestFiltering>
          <requestLimits maxAllowedContentLength="104857600" />
          <verbs>
            <add verb="TRACE" allowed="false" />
            <add verb="OPTIONS" allowed="true" />
          </verbs>
        </requestFiltering>
      </security>

      <!-- WebSocket Support -->
      <webSocket enabled="true" />
    </system.webServer>
  </location>
</configuration>
```

### 6.4 Create IIS Website

```powershell
Import-Module WebAdministration

# Remove default website (optional)
# Remove-Website -Name "Default Web Site"

# Create API Website
New-Website -Name "MonitoringAPI" `
    -PhysicalPath "C:\inetpub\wwwroot\MonitoringAPI" `
    -ApplicationPool "MonitoringAPIPool" `
    -Port 5155 `
    -HostHeader "api.yourdomain.com"

# Add HTTPS binding (after certificate is installed)
# New-WebBinding -Name "MonitoringAPI" -Protocol "https" -Port 443 -HostHeader "api.yourdomain.com" -SslFlags 1

# Start website
Start-Website -Name "MonitoringAPI"
```

### 6.5 Set Folder Permissions

```powershell
# Grant IIS AppPool identity permissions
$acl = Get-Acl "C:\inetpub\wwwroot\MonitoringAPI"
$identity = "IIS AppPool\MonitoringAPIPool"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\wwwroot\MonitoringAPI" $acl

# Grant write permissions to logs directory
$acl = Get-Acl "C:\inetpub\logs\MonitoringAPI"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\logs\MonitoringAPI" $acl

# Grant access to SFTP directories if needed
$acl = Get-Acl "C:\SFTP\Terminals"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\SFTP\Terminals" $acl
```

### 6.6 Test API

```powershell
# Test API endpoint
Invoke-RestMethod -Uri "http://localhost:5155/weatherforecast" -Method Get

# Test with curl
curl http://localhost:5155/api/health
```

---

## 7. BranchMonitorFrontEnd Deployment

### 7.1 Build and Publish

```powershell
# Navigate to project directory
cd "D:\Self Stady\CDK Monitring System\gthubProject\KioskMonitoringSolution\BranchMonitorFrontEnd\BranchMonitorFrontEnd"

# Restore packages
dotnet restore

# Publish for production
dotnet publish -c Release -o "C:\inetpub\wwwroot\MonitoringUI"
```

### 7.2 Update Client Configuration

Edit file: `C:\inetpub\wwwroot\MonitoringUI\wwwroot\appsettings.json`

```json
{
    "MainApiBaseUrl": "https://api.yourdomain.com/",
    "VNCApiBaseUrl": "wss://vnc.yourdomain.com/",
    "SignlRHubUrl": "https://api.yourdomain.com/"
}
```

### 7.3 Create web.config for Blazor WASM

Create file: `C:\inetpub\wwwroot\MonitoringUI\web.config`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <staticContent>
      <!-- Blazor WASM specific MIME types -->
      <remove fileExtension=".blat" />
      <remove fileExtension=".dat" />
      <remove fileExtension=".dll" />
      <remove fileExtension=".json" />
      <remove fileExtension=".wasm" />
      <remove fileExtension=".woff" />
      <remove fileExtension=".woff2" />
      <mimeMap fileExtension=".blat" mimeType="application/octet-stream" />
      <mimeMap fileExtension=".dat" mimeType="application/octet-stream" />
      <mimeMap fileExtension=".dll" mimeType="application/octet-stream" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <mimeMap fileExtension=".wasm" mimeType="application/wasm" />
      <mimeMap fileExtension=".woff" mimeType="font/woff" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
    </staticContent>

    <!-- Compression -->
    <httpCompression>
      <dynamicTypes>
        <add mimeType="application/wasm" enabled="true" />
      </dynamicTypes>
    </httpCompression>

    <!-- URL Rewrite for SPA routing -->
    <rewrite>
      <rules>
        <rule name="Serve subdir">
          <match url=".*" />
          <action type="Rewrite" url="wwwroot\{R:0}" />
        </rule>
        <rule name="SPA fallback routing" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="wwwroot\index.html" />
        </rule>
      </rules>
    </rewrite>

    <!-- Security Headers -->
    <httpProtocol>
      <customHeaders>
        <add name="X-Frame-Options" value="DENY" />
        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="X-XSS-Protection" value="1; mode=block" />
        <remove name="X-Powered-By" />
      </customHeaders>
    </httpProtocol>

    <!-- Caching -->
    <caching>
      <profiles>
        <add extension=".wasm" policy="CacheUntilChange" kernelCachePolicy="CacheUntilChange" />
        <add extension=".dll" policy="CacheUntilChange" kernelCachePolicy="CacheUntilChange" />
      </profiles>
    </caching>
  </system.webServer>
</configuration>
```

### 7.4 Create IIS Website

```powershell
Import-Module WebAdministration

# Create UI Website
New-Website -Name "MonitoringUI" `
    -PhysicalPath "C:\inetpub\wwwroot\MonitoringUI" `
    -ApplicationPool "MonitoringUIPool" `
    -Port 5235 `
    -HostHeader "monitoring.yourdomain.com"

# Start website
Start-Website -Name "MonitoringUI"
```

### 7.5 Install URL Rewrite Module

```powershell
# Download URL Rewrite Module
Invoke-WebRequest -Uri "https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi" -OutFile "C:\ServerSetup\Downloads\urlrewrite.msi"

# Install
Start-Process msiexec.exe -ArgumentList "/i C:\ServerSetup\Downloads\urlrewrite.msi /quiet" -Wait

# Restart IIS
iisreset /restart
```

### 7.6 Set Folder Permissions

```powershell
$acl = Get-Acl "C:\inetpub\wwwroot\MonitoringUI"
$identity = "IIS AppPool\MonitoringUIPool"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\wwwroot\MonitoringUI" $acl
```

---

## 8. VNC Proxy API Deployment

### 8.1 Build and Publish

```powershell
# Navigate to project directory
cd "D:\Self Stady\CDK Monitring System\gthubProject\KioskMonitoringSolution\BranchConnectVNCProxyAPI\BranchConnectVNCProxyAPI"

# Restore packages
dotnet restore

# Publish for production
dotnet publish -c Release -o "C:\inetpub\wwwroot\VNCProxy"
```

### 8.2 Create Production Configuration

Create file: `C:\inetpub\wwwroot\VNCProxy\appsettings.Production.json`

```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Warning",
            "Microsoft.AspNetCore": "Warning"
        }
    },
    "AllowedHosts": "vnc.yourdomain.com;localhost",
    "AllowedOrigins": [
        "https://monitoring.yourdomain.com"
    ]
}
```

### 8.3 Create web.config

Create file: `C:\inetpub\wwwroot\VNCProxy\web.config`

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet"
                  arguments=".\BranchConnectVNCProxyAPI.dll"
                  stdoutLogEnabled="true"
                  stdoutLogFile="C:\inetpub\logs\VNCProxy\stdout"
                  hostingModel="InProcess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>

      <!-- WebSocket Support (Critical for VNC) -->
      <webSocket enabled="true" receiveBufferLimit="67108864" />

      <!-- Security Headers -->
      <httpProtocol>
        <customHeaders>
          <add name="X-Frame-Options" value="SAMEORIGIN" />
          <add name="X-Content-Type-Options" value="nosniff" />
          <remove name="X-Powered-By" />
        </customHeaders>
      </httpProtocol>
    </system.webServer>
  </location>
</configuration>
```

### 8.4 Create IIS Website

```powershell
Import-Module WebAdministration

# Create VNC Proxy Website
New-Website -Name "VNCProxy" `
    -PhysicalPath "C:\inetpub\wwwroot\VNCProxy" `
    -ApplicationPool "VNCProxyPool" `
    -Port 7128 `
    -HostHeader "vnc.yourdomain.com"

# Start website
Start-Website -Name "VNCProxy"
```

### 8.5 Set Folder Permissions

```powershell
$acl = Get-Acl "C:\inetpub\wwwroot\VNCProxy"
$identity = "IIS AppPool\VNCProxyPool"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\wwwroot\VNCProxy" $acl

$acl = Get-Acl "C:\inetpub\logs\VNCProxy"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\logs\VNCProxy" $acl
```

---

## 9. SSL/TLS Certificate Configuration

### 9.1 Option A: Self-Signed Certificate (Development/Testing)

```powershell
# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -DnsName "api.yourdomain.com", "monitoring.yourdomain.com", "vnc.yourdomain.com", "localhost" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -NotAfter (Get-Date).AddYears(2) `
    -KeyAlgorithm RSA `
    -KeyLength 2048

# Export certificate
$password = ConvertTo-SecureString -String "CertPassword123!" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\Certs\monitoring-cert.pfx" -Password $password

# Display thumbprint
Write-Host "Certificate Thumbprint: $($cert.Thumbprint)"
```

### 9.2 Option B: Let's Encrypt Certificate (Production)

```powershell
# Install win-acme
Invoke-WebRequest -Uri "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.pluggable.zip" -OutFile "C:\ServerSetup\Downloads\win-acme.zip"

# Extract
Expand-Archive -Path "C:\ServerSetup\Downloads\win-acme.zip" -DestinationPath "C:\Tools\win-acme"

# Run win-acme
cd C:\Tools\win-acme
.\wacs.exe

# Follow prompts to:
# 1. Create certificate for api.yourdomain.com
# 2. Create certificate for monitoring.yourdomain.com
# 3. Create certificate for vnc.yourdomain.com
```

### 9.3 Bind SSL Certificate to IIS Sites

```powershell
Import-Module WebAdministration

# Get certificate thumbprint
$thumbprint = (Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*yourdomain.com*" }).Thumbprint

# Add HTTPS binding for API
New-WebBinding -Name "MonitoringAPI" -Protocol "https" -Port 443 -HostHeader "api.yourdomain.com" -SslFlags 1

# Bind certificate
$binding = Get-WebBinding -Name "MonitoringAPI" -Protocol "https"
$binding.AddSslCertificate($thumbprint, "My")

# Add HTTPS binding for UI
New-WebBinding -Name "MonitoringUI" -Protocol "https" -Port 443 -HostHeader "monitoring.yourdomain.com" -SslFlags 1
$binding = Get-WebBinding -Name "MonitoringUI" -Protocol "https"
$binding.AddSslCertificate($thumbprint, "My")

# Add HTTPS binding for VNC Proxy
New-WebBinding -Name "VNCProxy" -Protocol "https" -Port 443 -HostHeader "vnc.yourdomain.com" -SslFlags 1
$binding = Get-WebBinding -Name "VNCProxy" -Protocol "https"
$binding.AddSslCertificate($thumbprint, "My")
```

### 9.4 Configure HTTP to HTTPS Redirect

Add to each site's web.config:

```xml
<rewrite>
  <rules>
    <rule name="HTTP to HTTPS redirect" stopProcessing="true">
      <match url="(.*)" />
      <conditions>
        <add input="{HTTPS}" pattern="off" ignoreCase="true" />
      </conditions>
      <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
    </rule>
  </rules>
</rewrite>
```

---

## 10. Firewall Configuration

### 10.1 Configure Windows Firewall

```powershell
# Allow MQTT TLS (8883)
New-NetFirewallRule -DisplayName "MQTT TLS" -Direction Inbound -Protocol TCP -LocalPort 8883 -Action Allow

# Allow MQTT WebSocket (8884) - if needed
New-NetFirewallRule -DisplayName "MQTT WebSocket" -Direction Inbound -Protocol TCP -LocalPort 8884 -Action Allow

# Allow SSH/SFTP (22)
New-NetFirewallRule -DisplayName "SSH/SFTP" -Direction Inbound -Protocol TCP -LocalPort 22 -Action Allow

# Allow HTTP (80)
New-NetFirewallRule -DisplayName "HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow

# Allow HTTPS (443)
New-NetFirewallRule -DisplayName "HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow

# Allow SQL Server (1433) - Only from trusted IPs
New-NetFirewallRule -DisplayName "SQL Server" -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow -RemoteAddress "192.168.1.0/24"

# Block plain MQTT (1883) from external
New-NetFirewallRule -DisplayName "Block Plain MQTT External" -Direction Inbound -Protocol TCP -LocalPort 1883 -Action Block -RemoteAddress "!127.0.0.1"

# View all rules
Get-NetFirewallRule | Where-Object { $_.Enabled -eq $true } | Format-Table DisplayName, Direction, Action, Profile
```

### 10.2 IP Restrictions for SFTP (Optional)

```powershell
# Restrict SFTP to specific IP ranges (branch networks)
New-NetFirewallRule -DisplayName "SFTP - Branch Networks Only" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 22 `
    -Action Allow `
    -RemoteAddress "192.168.0.0/16", "10.0.0.0/8"

# Block all other SFTP
New-NetFirewallRule -DisplayName "SFTP - Block Others" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 22 `
    -Action Block
```

---

## 11. Service Monitoring & Health Checks

### 11.1 Create Health Check Script

Create file: `C:\Scripts\HealthCheck.ps1`

```powershell
# Health Check Script for Kiosk Monitoring Solution
# Run as scheduled task every 5 minutes

$ErrorActionPreference = "Stop"
$logFile = "C:\Logs\HealthCheck\health_$(Get-Date -Format 'yyyyMMdd').log"

function Write-Log {
    param($Message, $Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [$Level] $Message" | Add-Content $logFile
}

function Test-ServiceHealth {
    param($ServiceName)
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq "Running") {
        return $true
    }
    return $false
}

function Test-WebEndpoint {
    param($Url, $ExpectedStatus = 200)
    try {
        $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 10
        return $response.StatusCode -eq $ExpectedStatus
    }
    catch {
        return $false
    }
}

function Test-TcpPort {
    param($Host, $Port)
    try {
        $connection = New-Object System.Net.Sockets.TcpClient($Host, $Port)
        $connection.Close()
        return $true
    }
    catch {
        return $false
    }
}

# Create log directory
New-Item -ItemType Directory -Path (Split-Path $logFile) -Force | Out-Null

Write-Log "========== Health Check Started =========="

# Check Windows Services
$services = @("W3SVC", "mosquitto", "sshd", "MSSQLSERVER")
foreach ($svc in $services) {
    if (Test-ServiceHealth $svc) {
        Write-Log "Service $svc is running"
    }
    else {
        Write-Log "Service $svc is NOT running!" "ERROR"
        # Attempt restart
        Start-Service -Name $svc -ErrorAction SilentlyContinue
    }
}

# Check TCP Ports
$ports = @(
    @{Host="localhost"; Port=22; Name="SSH"},
    @{Host="localhost"; Port=8883; Name="MQTT TLS"},
    @{Host="localhost"; Port=1433; Name="SQL Server"},
    @{Host="localhost"; Port=443; Name="HTTPS"}
)

foreach ($port in $ports) {
    if (Test-TcpPort $port.Host $port.Port) {
        Write-Log "$($port.Name) port $($port.Port) is open"
    }
    else {
        Write-Log "$($port.Name) port $($port.Port) is CLOSED!" "ERROR"
    }
}

# Check Web Endpoints
$endpoints = @(
    @{Url="https://localhost/api/health"; Name="API Health"},
    @{Url="https://localhost/"; Name="Frontend"}
)

foreach ($ep in $endpoints) {
    if (Test-WebEndpoint $ep.Url) {
        Write-Log "$($ep.Name) endpoint is healthy"
    }
    else {
        Write-Log "$($ep.Name) endpoint is UNHEALTHY!" "ERROR"
    }
}

Write-Log "========== Health Check Completed =========="
```

### 11.2 Create Scheduled Task

```powershell
# Create scheduled task for health check
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-ExecutionPolicy Bypass -File C:\Scripts\HealthCheck.ps1"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 9999)
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName "Monitoring Health Check" -Action $action -Trigger $trigger -Principal $principal -Settings $settings
```

### 11.3 IIS Application Initialization

```powershell
# Enable Application Initialization
Install-WindowsFeature Web-AppInit

# Configure application pools for preload
Set-ItemProperty "IIS:\AppPools\MonitoringAPIPool" -Name "startMode" -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\MonitoringUIPool" -Name "startMode" -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\VNCProxyPool" -Name "startMode" -Value "AlwaysRunning"

# Configure sites for preload
Set-ItemProperty "IIS:\Sites\MonitoringAPI" -Name "applicationDefaults.preloadEnabled" -Value $true
Set-ItemProperty "IIS:\Sites\MonitoringUI" -Name "applicationDefaults.preloadEnabled" -Value $true
Set-ItemProperty "IIS:\Sites\VNCProxy" -Name "applicationDefaults.preloadEnabled" -Value $true
```

---

## 12. Backup & Recovery

### 12.1 Database Backup Script

Create file: `C:\Scripts\Backup-Database.ps1`

```powershell
# Database Backup Script
$backupPath = "C:\Backups\Database"
$retentionDays = 30

# Create backup directory
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null

# Backup filename
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFile = "$backupPath\MonitoringKiosk_$timestamp.bak"

# Execute backup
$query = @"
BACKUP DATABASE [MonitoringKiosk]
TO DISK = N'$backupFile'
WITH NOFORMAT, NOINIT,
NAME = N'MonitoringKiosk-Full Database Backup',
SKIP, NOREWIND, NOUNLOAD, STATS = 10
"@

Invoke-Sqlcmd -Query $query -ServerInstance "localhost"

Write-Host "Backup completed: $backupFile"

# Clean old backups
Get-ChildItem -Path $backupPath -Filter "*.bak" |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$retentionDays) } |
    Remove-Item -Force
```

### 12.2 Configuration Backup Script

Create file: `C:\Scripts\Backup-Configs.ps1`

```powershell
# Configuration Backup Script
$backupPath = "C:\Backups\Configs"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFolder = "$backupPath\config_$timestamp"

# Create backup directory
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null

# Backup IIS configuration
& "$env:windir\system32\inetsrv\appcmd.exe" list config /text:* > "$backupFolder\iis_config.txt"

# Backup Mosquitto configuration
Copy-Item "C:\Mosquitto\config\*" -Destination "$backupFolder\mosquitto\" -Recurse -Force

# Backup SSH configuration
Copy-Item "C:\ProgramData\ssh\sshd_config" -Destination "$backupFolder\sshd_config" -Force

# Backup application configurations
Copy-Item "C:\inetpub\wwwroot\MonitoringAPI\appsettings*.json" -Destination "$backupFolder\api\" -Force
Copy-Item "C:\inetpub\wwwroot\MonitoringUI\wwwroot\appsettings.json" -Destination "$backupFolder\ui\" -Force
Copy-Item "C:\inetpub\wwwroot\VNCProxy\appsettings*.json" -Destination "$backupFolder\vnc\" -Force

# Compress backup
Compress-Archive -Path $backupFolder -DestinationPath "$backupFolder.zip"
Remove-Item -Path $backupFolder -Recurse -Force

Write-Host "Configuration backup completed: $backupFolder.zip"
```

### 12.3 Schedule Backups

```powershell
# Schedule daily database backup at 2 AM
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-ExecutionPolicy Bypass -File C:\Scripts\Backup-Database.ps1"
$trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
Register-ScheduledTask -TaskName "Database Backup" -Action $action -Trigger $trigger -User "SYSTEM"

# Schedule weekly config backup on Sunday at 3 AM
$action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-ExecutionPolicy Bypass -File C:\Scripts\Backup-Configs.ps1"
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At "03:00"
Register-ScheduledTask -TaskName "Config Backup" -Action $action -Trigger $trigger -User "SYSTEM"
```

---

## 13. Troubleshooting

### 13.1 Common Issues and Solutions

#### IIS Application Not Starting

```powershell
# Check application pool status
Get-WebAppPoolState -Name "MonitoringAPIPool"

# Check event logs
Get-EventLog -LogName Application -Source "IIS*" -Newest 20

# Check stdout logs
Get-Content "C:\inetpub\logs\MonitoringAPI\stdout*.log" -Tail 50

# Reset application pool
Restart-WebAppPool -Name "MonitoringAPIPool"
```

#### MQTT Connection Issues

```powershell
# Check Mosquitto service
Get-Service mosquitto

# Check Mosquitto logs
Get-Content "C:\Mosquitto\log\mosquitto.log" -Tail 100

# Test connection
& "C:\Program Files\mosquitto\mosquitto_sub.exe" -h localhost -p 8883 --cafile "C:\Mosquitto\certs\ca.crt" -u MonitoringBackend -P "password" -t "#" -v

# Restart Mosquitto
Restart-Service mosquitto
```

#### SSH/SFTP Connection Issues

```powershell
# Check SSH service
Get-Service sshd

# Check SSH logs (Event Viewer)
Get-WinEvent -LogName "OpenSSH/Operational" -MaxEvents 20

# Test local connection
ssh sftpuser@localhost

# Check configuration syntax
sshd -t

# Restart SSH
Restart-Service sshd
```

#### Database Connection Issues

```powershell
# Check SQL Server service
Get-Service MSSQLSERVER

# Test connection
Invoke-Sqlcmd -Query "SELECT 1" -ServerInstance "localhost"

# Check SQL Server error log
Get-Content "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Log\ERRORLOG" -Tail 50
```

### 13.2 Useful Diagnostic Commands

```powershell
# Check all listening ports
netstat -an | findstr "LISTENING"

# Check specific port
Test-NetConnection -ComputerName localhost -Port 443

# Check certificate binding
netsh http show sslcert

# Check IIS sites status
Get-Website | Format-Table Name, State, PhysicalPath

# Check application pools
Get-WebAppPoolState

# Check Windows Firewall rules
Get-NetFirewallRule | Where-Object {$_.Enabled -eq $true -and $_.Direction -eq "Inbound"} | Format-Table DisplayName, Action

# Check disk space
Get-PSDrive -PSProvider FileSystem

# Check memory usage
Get-Process | Sort-Object -Property WorkingSet -Descending | Select-Object -First 10
```

### 13.3 Log File Locations

| Service | Log Location |
|---------|--------------|
| IIS | `C:\inetpub\logs\LogFiles\` |
| API stdout | `C:\inetpub\logs\MonitoringAPI\` |
| Mosquitto | `C:\Mosquitto\log\mosquitto.log` |
| SSH | Event Viewer > OpenSSH |
| SQL Server | `C:\Program Files\Microsoft SQL Server\...\MSSQL\Log\` |
| Health Check | `C:\Logs\HealthCheck\` |

---

## Quick Reference Card

### Service URLs (Production)

| Service | URL |
|---------|-----|
| API | `https://api.yourdomain.com/` |
| Frontend | `https://monitoring.yourdomain.com/` |
| VNC Proxy | `wss://vnc.yourdomain.com/` |
| SignalR Hub | `https://api.yourdomain.com/branchHub` |
| Swagger (Dev) | `https://api.yourdomain.com/swagger` |

### Important Paths

| Item | Path |
|------|------|
| API Application | `C:\inetpub\wwwroot\MonitoringAPI\` |
| UI Application | `C:\inetpub\wwwroot\MonitoringUI\` |
| VNC Proxy | `C:\inetpub\wwwroot\VNCProxy\` |
| Mosquitto Config | `C:\Mosquitto\config\` |
| SSH Config | `C:\ProgramData\ssh\` |
| SFTP Root | `C:\SFTP\` |
| SSL Certificates | `C:\Certs\` |
| Backups | `C:\Backups\` |

### Service Management Commands

```powershell
# IIS
iisreset /restart
Start-Website -Name "MonitoringAPI"
Stop-Website -Name "MonitoringAPI"

# Mosquitto
Start-Service mosquitto
Stop-Service mosquitto
Restart-Service mosquitto

# SSH
Start-Service sshd
Stop-Service sshd
Restart-Service sshd

# SQL Server
Start-Service MSSQLSERVER
Stop-Service MSSQLSERVER
Restart-Service MSSQLSERVER
```

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | Feb 2026 | - | Initial release |

---

**End of Document**
