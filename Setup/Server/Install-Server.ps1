#Requires -RunAsAdministrator
# ============================================================
# Kiosk Monitoring Solution - Server Setup Installer
# ============================================================
# Run as Administrator: Right-click PowerShell > Run as Administrator
# Usage: .\Install-Server.ps1
# ============================================================

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Kiosk Monitoring - Server Installer"

# ============================================================
# CONFIGURATION - EDIT THESE VALUES BEFORE RUNNING
# ============================================================

$CONFIG = @{
    # Server Network
    ServerIP              = "192.168.1.24"
    DomainName            = ""                              # Leave empty if no domain

    # SQL Server
    SqlServerInstance     = "localhost"
    SqlDatabaseName       = "MonitoringKiosk"
    SqlAppUser            = "MonitoringAppUser"
    SqlAppPassword        = "StrongP@ssw0rd!2026"

    # MQTT
    MqttUser              = "MonitoringBackend"
    MqttPassword          = "Mqtt@Secure2026!"
    MqttBranchUser        = "BranchService"
    MqttBranchPassword    = "BranchMqtt@2026!"
    MqttPort              = 8883
    MqttPlainPort         = 1883                            # Internal only

    # SFTP
    SftpUser              = "sftpuser"
    SftpPassword          = "Sftp@Secure2026!"
    SftpRootPath          = "C:\SFTP"

    # IIS - API
    ApiSiteName           = "MonitoringAPI"
    ApiPort               = 5155
    ApiAppPoolName        = "MonitoringAPIPool"
    ApiPhysicalPath       = "C:\inetpub\wwwroot\MonitoringAPI"

    # IIS - Frontend
    UiSiteName            = "MonitoringUI"
    UiPort                = 5235
    UiAppPoolName         = "MonitoringUIPool"
    UiPhysicalPath        = "C:\inetpub\wwwroot\MonitoringUI"

    # IIS - VNC Proxy
    VncSiteName           = "VNCProxy"
    VncPort               = 7128
    VncAppPoolName        = "VNCProxyPool"
    VncPhysicalPath       = "C:\inetpub\wwwroot\VNCProxy"

    # JWT
    JwtKey                = ""                               # Auto-generated if empty
    JwtIssuer             = ""                               # Auto-set from ServerIP
    JwtAudience           = ""                               # Auto-set from ServerIP

    # Paths
    MosquittoInstallPath  = "C:\Program Files\mosquitto"
    MosquittoDataPath     = "C:\Mosquitto"
    CertsPath             = "C:\Monitoring\Certs"
    LogsPath              = "C:\Monitoring\Logs"
    BackupsPath           = "C:\Monitoring\Backups"
    ScriptsPath           = "C:\Monitoring\Scripts"
    TerminalsPath         = "C:\Monitoring\Terminals"

    # Project Source Path
    SolutionPath          = ""                               # Auto-detected
}

# ============================================================
# HELPER FUNCTIONS
# ============================================================

function Write-Banner {
    param([string]$Text)
    $line = "=" * 60
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor White
    Write-Host $line -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([string]$Text)
    Write-Host "  [*] $Text" -ForegroundColor Green
}

function Write-SubStep {
    param([string]$Text)
    Write-Host "      $Text" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Text)
    Write-Host "  [!] $Text" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Text)
    Write-Host "  [X] $Text" -ForegroundColor Red
}

function Write-Success {
    param([string]$Text)
    Write-Host "  [OK] $Text" -ForegroundColor Green
}

function Ask-YesNo {
    param([string]$Question)
    $answer = Read-Host "  $Question (Y/N)"
    return $answer -match "^[Yy]"
}

function Ask-Input {
    param([string]$Prompt, [string]$Default)
    if ($Default) {
        $result = Read-Host "  $Prompt [$Default]"
        if ([string]::IsNullOrWhiteSpace($result)) { return $Default }
        return $result
    }
    return Read-Host "  $Prompt"
}

function Ask-SecureInput {
    param([string]$Prompt)
    $secure = Read-Host "  $Prompt" -AsSecureString
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
}

function Test-CommandExists {
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function New-RandomKey {
    param([int]$Length = 64)
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Write-LogFile {
    param([string]$Message)
    $logFile = Join-Path $CONFIG.LogsPath "server-install-$(Get-Date -Format 'yyyyMMdd').log"
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp | $Message" | Add-Content -Path $logFile -ErrorAction SilentlyContinue
}

# ============================================================
# MAIN INSTALLER
# ============================================================

Clear-Host

Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║                                                      ║" -ForegroundColor Cyan
Write-Host "  ║     KIOSK MONITORING SOLUTION                        ║" -ForegroundColor Cyan
Write-Host "  ║     Server Setup Installer v1.0                      ║" -ForegroundColor Cyan
Write-Host "  ║                                                      ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  This installer will configure:" -ForegroundColor White
Write-Host "    1. Folder Structure" -ForegroundColor Gray
Write-Host "    2. MQTT Broker (Mosquitto)" -ForegroundColor Gray
Write-Host "    3. OpenSSH SFTP Server" -ForegroundColor Gray
Write-Host "    4. SQL Server Database" -ForegroundColor Gray
Write-Host "    5. IIS Web Server" -ForegroundColor Gray
Write-Host "    6. API Backend Deployment" -ForegroundColor Gray
Write-Host "    7. Frontend Deployment" -ForegroundColor Gray
Write-Host "    8. VNC Proxy Deployment" -ForegroundColor Gray
Write-Host "    9. SSL Certificates" -ForegroundColor Gray
Write-Host "   10. Firewall Rules" -ForegroundColor Gray
Write-Host "   11. Health Check Scheduled Task" -ForegroundColor Gray
Write-Host ""

if (-not (Ask-YesNo "Do you want to continue?")) {
    Write-Host "  Setup cancelled." -ForegroundColor Yellow
    exit
}

# ============================================================
# STEP 0: COLLECT CONFIGURATION
# ============================================================

Write-Banner "STEP 0: Configuration"

# Auto-detect solution path
$scriptDir = Split-Path -Parent $PSScriptRoot
$CONFIG.SolutionPath = Split-Path -Parent $scriptDir
if (-not (Test-Path (Join-Path $CONFIG.SolutionPath "KioskMonitoringSolution.sln") -ErrorAction SilentlyContinue)) {
    $CONFIG.SolutionPath = Ask-Input "Enter Solution folder path" "D:\KioskMonitoringSolution"
}
Write-Step "Solution Path: $($CONFIG.SolutionPath)"

# Server IP
$CONFIG.ServerIP = Ask-Input "Server IP Address" $CONFIG.ServerIP

# SQL Server
Write-Host ""
Write-Step "SQL Server Configuration:"
$CONFIG.SqlServerInstance = Ask-Input "  SQL Server Instance" $CONFIG.SqlServerInstance
$CONFIG.SqlDatabaseName = Ask-Input "  Database Name" $CONFIG.SqlDatabaseName
$CONFIG.SqlAppUser = Ask-Input "  App User Name" $CONFIG.SqlAppUser
$CONFIG.SqlAppPassword = Ask-Input "  App User Password" $CONFIG.SqlAppPassword

# MQTT
Write-Host ""
Write-Step "MQTT Configuration:"
$CONFIG.MqttUser = Ask-Input "  MQTT Backend User" $CONFIG.MqttUser
$CONFIG.MqttPassword = Ask-Input "  MQTT Backend Password" $CONFIG.MqttPassword
$CONFIG.MqttBranchUser = Ask-Input "  MQTT Branch User" $CONFIG.MqttBranchUser
$CONFIG.MqttBranchPassword = Ask-Input "  MQTT Branch Password" $CONFIG.MqttBranchPassword

# SFTP
Write-Host ""
Write-Step "SFTP Configuration:"
$CONFIG.SftpUser = Ask-Input "  SFTP Username" $CONFIG.SftpUser
$CONFIG.SftpPassword = Ask-Input "  SFTP Password" $CONFIG.SftpPassword

# JWT Key
if ([string]::IsNullOrWhiteSpace($CONFIG.JwtKey)) {
    $CONFIG.JwtKey = New-RandomKey -Length 64
    Write-Step "JWT Key auto-generated"
}
$CONFIG.JwtIssuer = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)/"
$CONFIG.JwtAudience = "http://$($CONFIG.ServerIP):$($CONFIG.UiPort)/"

Write-Host ""
Write-Success "Configuration collected"

# Create base directories
New-Item -ItemType Directory -Path $CONFIG.LogsPath -Force | Out-Null
Write-LogFile "Server installation started"

# ============================================================
# STEP 1: CREATE FOLDER STRUCTURE
# ============================================================

Write-Banner "STEP 1: Creating Folder Structure"

$folders = @(
    $CONFIG.MosquittoDataPath,
    "$($CONFIG.MosquittoDataPath)\config",
    "$($CONFIG.MosquittoDataPath)\data",
    "$($CONFIG.MosquittoDataPath)\log",
    "$($CONFIG.MosquittoDataPath)\certs",
    "$($CONFIG.MosquittoDataPath)\passwords",
    $CONFIG.CertsPath,
    $CONFIG.LogsPath,
    "$($CONFIG.LogsPath)\API",
    "$($CONFIG.LogsPath)\UI",
    "$($CONFIG.LogsPath)\VNC",
    "$($CONFIG.LogsPath)\MQTT",
    "$($CONFIG.LogsPath)\SSH",
    "$($CONFIG.LogsPath)\HealthCheck",
    $CONFIG.BackupsPath,
    "$($CONFIG.BackupsPath)\Database",
    "$($CONFIG.BackupsPath)\Config",
    $CONFIG.ScriptsPath,
    $CONFIG.TerminalsPath,
    $CONFIG.SftpRootPath,
    "$($CONFIG.SftpRootPath)\Patches",
    "$($CONFIG.SftpRootPath)\Terminals",
    "$($CONFIG.SftpRootPath)\Logs",
    $CONFIG.ApiPhysicalPath,
    $CONFIG.UiPhysicalPath,
    $CONFIG.VncPhysicalPath
)

foreach ($folder in $folders) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Write-SubStep "Created: $folder"
}

Write-Success "Folder structure created"
Write-LogFile "Folder structure created"

# ============================================================
# STEP 2: INSTALL & CONFIGURE MOSQUITTO MQTT
# ============================================================

Write-Banner "STEP 2: MQTT Broker (Mosquitto)"

$mosquittoExe = Join-Path $CONFIG.MosquittoInstallPath "mosquitto.exe"

if (-not (Test-Path $mosquittoExe)) {
    Write-Warn "Mosquitto not found at: $($CONFIG.MosquittoInstallPath)"
    Write-Host "  Download from: https://mosquitto.org/download/" -ForegroundColor Yellow
    Write-Host "  Install to: $($CONFIG.MosquittoInstallPath)" -ForegroundColor Yellow
    Write-Host ""
    if (Ask-YesNo "Have you installed Mosquitto? Continue?") {
        if (-not (Test-Path $mosquittoExe)) {
            Write-Err "Mosquitto still not found. Skipping MQTT setup."
            $skipMqtt = $true
        }
    } else {
        $skipMqtt = $true
    }
}

if (-not $skipMqtt) {
    # Create password file
    Write-Step "Creating MQTT password file..."
    $passwdFile = "$($CONFIG.MosquittoDataPath)\passwords\passwd"
    $passwdExe = Join-Path $CONFIG.MosquittoInstallPath "mosquitto_passwd.exe"

    # Create password file with backend user
    $tempPassFile = [System.IO.Path]::GetTempFileName()
    "$($CONFIG.MqttPassword)" | Set-Content $tempPassFile -NoNewline
    & $passwdExe -c -b $passwdFile $CONFIG.MqttUser $CONFIG.MqttPassword 2>$null
    & $passwdExe -b $passwdFile $CONFIG.MqttBranchUser $CONFIG.MqttBranchPassword 2>$null
    Remove-Item $tempPassFile -ErrorAction SilentlyContinue
    Write-SubStep "Password file created"

    # Create ACL file
    Write-Step "Creating MQTT ACL file..."
    $aclContent = @"
# MonitoringBackend - Full access
user $($CONFIG.MqttUser)
topic readwrite #

# BranchService - Branch-specific topics
user $($CONFIG.MqttBranchUser)
pattern readwrite branch/%u/#
pattern readwrite server/%u/#
pattern read broadcast/#
pattern read config/#

# System topics read only
topic read `$SYS/#
"@
    Set-Content -Path "$($CONFIG.MosquittoDataPath)\config\acl.conf" -Value $aclContent
    Write-SubStep "ACL file created"

    # Create Mosquitto config
    Write-Step "Creating Mosquitto configuration..."
    $mqttConfig = @"
# ============================================================
# Mosquitto Configuration - Kiosk Monitoring Solution
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# ============================================================

# Persistence
persistence true
persistence_location $($CONFIG.MosquittoDataPath)\data\

# Logging
log_dest file $($CONFIG.MosquittoDataPath)\log\mosquitto.log
log_type error
log_type warning
log_type notice
log_timestamp true
log_timestamp_format %Y-%m-%d %H:%M:%S

# ---- Plain TCP Listener (localhost only) ----
listener $($CONFIG.MqttPlainPort) 127.0.0.1
protocol mqtt

# ---- TLS Listener (all interfaces) ----
# Uncomment after generating certificates:
# listener $($CONFIG.MqttPort)
# protocol mqtt
# cafile $($CONFIG.MosquittoDataPath)\certs\ca.crt
# certfile $($CONFIG.MosquittoDataPath)\certs\mqtt-server.crt
# keyfile $($CONFIG.MosquittoDataPath)\certs\mqtt-server.key
# tls_version tlsv1.2

# ---- Authentication ----
allow_anonymous false
password_file $($CONFIG.MosquittoDataPath)\passwords\passwd
acl_file $($CONFIG.MosquittoDataPath)\config\acl.conf

# ---- Connection Limits ----
max_connections 1000
max_inflight_messages 20
max_queued_messages 1000
message_size_limit 1048576

# ---- Keep Alive ----
max_keepalive 300
persistent_client_expiration 7d

set_tcp_nodelay true
"@
    Set-Content -Path "$($CONFIG.MosquittoDataPath)\config\mosquitto.conf" -Value $mqttConfig
    Write-SubStep "Configuration file created"

    # Install/Configure Windows Service
    Write-Step "Configuring Mosquitto Windows Service..."
    Stop-Service mosquitto -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    # Try to update service path
    $svcPath = "`"$mosquittoExe`" -c `"$($CONFIG.MosquittoDataPath)\config\mosquitto.conf`""
    try {
        sc.exe config mosquitto binPath= $svcPath | Out-Null
        Write-SubStep "Service path updated"
    } catch {
        Write-Warn "Could not update service path. Manual update may be needed."
    }

    Set-Service -Name mosquitto -StartupType Automatic -ErrorAction SilentlyContinue
    Start-Service mosquitto -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $mqttService = Get-Service mosquitto -ErrorAction SilentlyContinue
    if ($mqttService -and $mqttService.Status -eq "Running") {
        Write-Success "Mosquitto MQTT is running on port $($CONFIG.MqttPlainPort)"
    } else {
        Write-Warn "Mosquitto service may need manual start"
    }

    Write-LogFile "MQTT Broker configured"
}

# ============================================================
# STEP 3: INSTALL & CONFIGURE OPENSSH SFTP
# ============================================================

Write-Banner "STEP 3: OpenSSH SFTP Server"

# Check if OpenSSH is installed
$sshCapability = Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH.Server*'

if ($sshCapability.State -ne "Installed") {
    Write-Step "Installing OpenSSH Server..."
    Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Out-Null
    Write-SubStep "OpenSSH Server installed"
} else {
    Write-Step "OpenSSH Server already installed"
}

# Create SFTP user
Write-Step "Creating SFTP user account..."
$existingUser = Get-LocalUser -Name $CONFIG.SftpUser -ErrorAction SilentlyContinue
if (-not $existingUser) {
    $securePass = ConvertTo-SecureString $CONFIG.SftpPassword -AsPlainText -Force
    New-LocalUser -Name $CONFIG.SftpUser -Password $securePass -FullName "SFTP Service User" -Description "Kiosk Monitoring SFTP" -PasswordNeverExpires | Out-Null
    Add-LocalGroupMember -Group "Users" -Member $CONFIG.SftpUser -ErrorAction SilentlyContinue
    Write-SubStep "User '$($CONFIG.SftpUser)' created"
} else {
    Write-SubStep "User '$($CONFIG.SftpUser)' already exists"
}

# Create .ssh directory for user
$sshUserDir = "C:\Users\$($CONFIG.SftpUser)\.ssh"
New-Item -ItemType Directory -Path $sshUserDir -Force | Out-Null

# Configure sshd_config
Write-Step "Configuring OpenSSH Server..."
$sshdConfig = @"
# ============================================================
# OpenSSH Server Configuration - Kiosk Monitoring Solution
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# ============================================================

Port 22
AddressFamily inet
ListenAddress 0.0.0.0

# Host Keys
HostKey __PROGRAMDATA__/ssh/ssh_host_ed25519_key
HostKey __PROGRAMDATA__/ssh/ssh_host_rsa_key

# Authentication
PermitRootLogin no
PubkeyAuthentication yes
AuthorizedKeysFile .ssh/authorized_keys
PasswordAuthentication yes
PermitEmptyPasswords no
ChallengeResponseAuthentication no

# Security
MaxAuthTries 5
LoginGraceTime 60
ClientAliveInterval 300
ClientAliveCountMax 2
MaxSessions 100

# Disable unnecessary features
AllowTcpForwarding no
GatewayPorts no
X11Forwarding no
AllowAgentForwarding no
PermitTunnel no

# Logging
SyslogFacility LOCAL0
LogLevel INFO

# SFTP subsystem
Subsystem sftp sftp-server.exe

# Allow only SFTP user
AllowUsers $($CONFIG.SftpUser)

# SFTP-only restriction
Match User $($CONFIG.SftpUser)
    ForceCommand internal-sftp
    ChrootDirectory $($CONFIG.SftpRootPath)
    PermitTunnel no
    AllowAgentForwarding no
    AllowTcpForwarding no
    X11Forwarding no
"@

$sshdConfigPath = "C:\ProgramData\ssh\sshd_config"
Copy-Item $sshdConfigPath "$sshdConfigPath.backup.$(Get-Date -Format 'yyyyMMddHHmmss')" -ErrorAction SilentlyContinue
Set-Content -Path $sshdConfigPath -Value $sshdConfig
Write-SubStep "sshd_config updated"

# Set SFTP directory permissions
Write-Step "Setting SFTP directory permissions..."
icacls $CONFIG.SftpRootPath /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" /grant:r "Administrators:(OI)(CI)F" /grant:r "$($CONFIG.SftpUser):(RX)" | Out-Null
icacls "$($CONFIG.SftpRootPath)\Patches" /grant:r "$($CONFIG.SftpUser):(OI)(CI)F" | Out-Null
icacls "$($CONFIG.SftpRootPath)\Terminals" /grant:r "$($CONFIG.SftpUser):(OI)(CI)F" | Out-Null
icacls "$($CONFIG.SftpRootPath)\Logs" /grant:r "$($CONFIG.SftpUser):(OI)(CI)F" | Out-Null
Write-SubStep "Permissions set"

# Start SSH service
Write-Step "Starting SSH service..."
Set-Service -Name sshd -StartupType Automatic
Restart-Service sshd -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$sshService = Get-Service sshd -ErrorAction SilentlyContinue
if ($sshService -and $sshService.Status -eq "Running") {
    Write-Success "OpenSSH SFTP Server is running on port 22"
} else {
    Write-Warn "SSH service may need manual start"
}

Write-LogFile "OpenSSH SFTP configured"

# ============================================================
# STEP 4: SQL SERVER DATABASE
# ============================================================

Write-Banner "STEP 4: SQL Server Database"

$sqlService = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
if (-not $sqlService) {
    $sqlService = Get-Service "MSSQL`$*" -ErrorAction SilentlyContinue | Select-Object -First 1
}

if ($sqlService) {
    Write-Step "SQL Server found: $($sqlService.Name)"

    if (Ask-YesNo "Create database and user?") {
        Write-Step "Creating database and user..."

        $sqlScript = @"
-- Create Database
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '$($CONFIG.SqlDatabaseName)')
BEGIN
    CREATE DATABASE [$($CONFIG.SqlDatabaseName)];
    PRINT 'Database created';
END
ELSE
    PRINT 'Database already exists';
GO

USE [$($CONFIG.SqlDatabaseName)];
GO

-- Create Login
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = '$($CONFIG.SqlAppUser)')
BEGIN
    CREATE LOGIN [$($CONFIG.SqlAppUser)] WITH PASSWORD = '$($CONFIG.SqlAppPassword)';
    PRINT 'Login created';
END
GO

-- Create User
IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '$($CONFIG.SqlAppUser)')
BEGIN
    CREATE USER [$($CONFIG.SqlAppUser)] FOR LOGIN [$($CONFIG.SqlAppUser)];
    ALTER ROLE db_datareader ADD MEMBER [$($CONFIG.SqlAppUser)];
    ALTER ROLE db_datawriter ADD MEMBER [$($CONFIG.SqlAppUser)];
    ALTER ROLE db_ddladmin ADD MEMBER [$($CONFIG.SqlAppUser)];
    GRANT EXECUTE TO [$($CONFIG.SqlAppUser)];
    PRINT 'User created with permissions';
END
GO
"@

        $sqlFile = Join-Path $CONFIG.ScriptsPath "setup-database.sql"
        Set-Content -Path $sqlFile -Value $sqlScript

        try {
            sqlcmd -S $CONFIG.SqlServerInstance -E -i $sqlFile 2>&1 | ForEach-Object { Write-SubStep $_ }
            Write-Success "Database and user created"
        } catch {
            Write-Warn "Could not run SQL script automatically."
            Write-Host "  Run manually: sqlcmd -S $($CONFIG.SqlServerInstance) -E -i `"$sqlFile`"" -ForegroundColor Yellow
        }
    }
} else {
    Write-Warn "SQL Server not found. Please install SQL Server first."
}

Write-LogFile "SQL Server configured"

# ============================================================
# STEP 5: IIS INSTALLATION
# ============================================================

Write-Banner "STEP 5: IIS Web Server"

# Check if IIS is installed
$iisService = Get-Service W3SVC -ErrorAction SilentlyContinue

if (-not $iisService) {
    Write-Step "Installing IIS features..."

    $features = @(
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-HttpErrors",
        "IIS-HttpRedirect",
        "IIS-ApplicationDevelopment",
        "IIS-NetFxExtensibility45",
        "IIS-HealthAndDiagnostics",
        "IIS-HttpLogging",
        "IIS-Security",
        "IIS-RequestFiltering",
        "IIS-Performance",
        "IIS-WebServerManagementTools",
        "IIS-ManagementConsole",
        "IIS-StaticContent",
        "IIS-DefaultDocument",
        "IIS-WebSockets",
        "IIS-ASPNET45",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-HttpCompressionStatic",
        "IIS-HttpCompressionDynamic"
    )

    foreach ($feature in $features) {
        try {
            Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart -ErrorAction SilentlyContinue | Out-Null
            Write-SubStep "Installed: $feature"
        } catch {
            Write-SubStep "Skipped: $feature (may already be installed)"
        }
    }

    Write-Success "IIS features installed"
} else {
    Write-Step "IIS already installed"
}

# Check for .NET Hosting Bundle
Write-Step "Checking .NET 8.0 Hosting Bundle..."
$aspnetModule = Get-WebGlobalModule -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*AspNetCore*" }

if (-not $aspnetModule) {
    Write-Warn ".NET 8.0 Hosting Bundle NOT found!"
    Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "  Install the 'Hosting Bundle' and run this script again." -ForegroundColor Yellow
    Write-Host ""
    Read-Host "  Press Enter after installing the Hosting Bundle"
    iisreset /restart 2>$null | Out-Null
} else {
    Write-SubStep ".NET Hosting Bundle found"
}

Write-LogFile "IIS configured"

# ============================================================
# STEP 6: BUILD & DEPLOY API
# ============================================================

Write-Banner "STEP 6: MonitoringBackend API Deployment"

$apiProjectPath = Join-Path $CONFIG.SolutionPath "MonitoringBackend"

if (Test-Path $apiProjectPath) {
    Write-Step "Building MonitoringBackend..."

    # Build and publish
    try {
        Push-Location $apiProjectPath
        dotnet restore 2>&1 | Out-Null
        dotnet publish -c Release -o $CONFIG.ApiPhysicalPath --no-restore 2>&1 | ForEach-Object { Write-SubStep $_ }
        Pop-Location
        Write-SubStep "Build completed"
    } catch {
        Pop-Location
        Write-Warn "Build failed. Please build manually."
    }

    # Create production appsettings
    Write-Step "Creating production configuration..."
    $connectionString = "Server=$($CONFIG.SqlServerInstance);Database=$($CONFIG.SqlDatabaseName);User Id=$($CONFIG.SqlAppUser);Password=$($CONFIG.SqlAppPassword);TrustServerCertificate=True;Encrypt=True;"

    $apiSettings = @{
        ConnectionStrings = @{
            DefaultConnection = $connectionString
        }
        Jwt = @{
            Key      = $CONFIG.JwtKey
            Issuer   = $CONFIG.JwtIssuer
            Audience = $CONFIG.JwtAudience
        }
        Logging = @{
            LogLevel = @{
                Default               = "Warning"
                "Microsoft.AspNetCore" = "Warning"
            }
        }
        AllowedOrigins = @("http://$($CONFIG.ServerIP):$($CONFIG.UiPort)")
        MQTT = @{
            Host     = "127.0.0.1"
            Port     = "$($CONFIG.MqttPlainPort)"
            Username = $CONFIG.MqttUser
            Password = $CONFIG.MqttPassword
        }
        ServerConfig = @{
            ServerTerminalsPath = "$($CONFIG.TerminalsPath)\"
        }
        AllowedHosts = "*"
    } | ConvertTo-Json -Depth 4

    Set-Content -Path (Join-Path $CONFIG.ApiPhysicalPath "appsettings.Production.json") -Value $apiSettings
    Write-SubStep "appsettings.Production.json created"

    # Create web.config
    Write-Step "Creating web.config..."
    $apiWebConfig = @"
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
                  stdoutLogFile="$($CONFIG.LogsPath)\API\stdout"
                  hostingModel="InProcess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
      <httpProtocol>
        <customHeaders>
          <add name="X-Frame-Options" value="DENY" />
          <add name="X-Content-Type-Options" value="nosniff" />
          <add name="X-XSS-Protection" value="1; mode=block" />
          <remove name="X-Powered-By" />
        </customHeaders>
      </httpProtocol>
      <webSocket enabled="true" />
    </system.webServer>
  </location>
</configuration>
"@
    Set-Content -Path (Join-Path $CONFIG.ApiPhysicalPath "web.config") -Value $apiWebConfig
    Write-SubStep "web.config created"

    # Create IIS Application Pool and Site
    Write-Step "Creating IIS Application Pool & Site..."
    Import-Module WebAdministration -ErrorAction SilentlyContinue

    # Application Pool
    if (-not (Test-Path "IIS:\AppPools\$($CONFIG.ApiAppPoolName)" -ErrorAction SilentlyContinue)) {
        New-WebAppPool -Name $CONFIG.ApiAppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$($CONFIG.ApiAppPoolName)" -Name "managedRuntimeVersion" -Value ""
    Set-ItemProperty "IIS:\AppPools\$($CONFIG.ApiAppPoolName)" -Name "startMode" -Value "AlwaysRunning"
    Set-ItemProperty "IIS:\AppPools\$($CONFIG.ApiAppPoolName)" -Name "processModel.idleTimeout" -Value "00:00:00"
    Write-SubStep "App Pool '$($CONFIG.ApiAppPoolName)' configured"

    # Website
    $existingSite = Get-Website -Name $CONFIG.ApiSiteName -ErrorAction SilentlyContinue
    if (-not $existingSite) {
        New-Website -Name $CONFIG.ApiSiteName -PhysicalPath $CONFIG.ApiPhysicalPath -ApplicationPool $CONFIG.ApiAppPoolName -Port $CONFIG.ApiPort | Out-Null
    }
    Write-SubStep "Website '$($CONFIG.ApiSiteName)' created on port $($CONFIG.ApiPort)"

    # Set permissions
    $identity = "IIS AppPool\$($CONFIG.ApiAppPoolName)"
    $acl = Get-Acl $CONFIG.ApiPhysicalPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl $CONFIG.ApiPhysicalPath $acl
    Write-SubStep "Permissions set"

    # Start site
    Start-Website -Name $CONFIG.ApiSiteName -ErrorAction SilentlyContinue
    Write-Success "API deployed at http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)"

} else {
    Write-Warn "MonitoringBackend project not found at: $apiProjectPath"
}

Write-LogFile "API deployed"

# ============================================================
# STEP 7: BUILD & DEPLOY FRONTEND
# ============================================================

Write-Banner "STEP 7: BranchMonitorFrontEnd Deployment"

$uiProjectPath = Join-Path $CONFIG.SolutionPath "BranchMonitorFrontEnd\BranchMonitorFrontEnd"

if (Test-Path $uiProjectPath) {
    Write-Step "Building BranchMonitorFrontEnd..."

    try {
        Push-Location $uiProjectPath
        dotnet restore 2>&1 | Out-Null
        dotnet publish -c Release -o $CONFIG.UiPhysicalPath --no-restore 2>&1 | ForEach-Object { Write-SubStep $_ }
        Pop-Location
        Write-SubStep "Build completed"
    } catch {
        Pop-Location
        Write-Warn "Build failed. Please build manually."
    }

    # Update client configuration
    Write-Step "Updating client configuration..."
    $uiAppsettingsPath = Join-Path $CONFIG.UiPhysicalPath "wwwroot\appsettings.json"
    if (Test-Path $uiAppsettingsPath) {
        $uiSettings = @{
            MainApiBaseUrl = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)/"
            VNCApiBaseUrl  = "ws://$($CONFIG.ServerIP):$($CONFIG.VncPort)/"
            SignlRHubUrl   = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)/"
        } | ConvertTo-Json -Depth 2
        Set-Content -Path $uiAppsettingsPath -Value $uiSettings
        Write-SubStep "Client config updated"
    }

    # Create web.config for Blazor WASM
    Write-Step "Creating Blazor WASM web.config..."
    $uiWebConfig = @"
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <staticContent>
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
    <rewrite>
      <rules>
        <rule name="Serve subdir">
          <match url=".*" />
          <action type="Rewrite" url="wwwroot\{R:0}" />
        </rule>
        <rule name="SPA fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="wwwroot\index.html" />
        </rule>
      </rules>
    </rewrite>
    <httpProtocol>
      <customHeaders>
        <add name="X-Frame-Options" value="DENY" />
        <add name="X-Content-Type-Options" value="nosniff" />
        <remove name="X-Powered-By" />
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
"@
    Set-Content -Path (Join-Path $CONFIG.UiPhysicalPath "web.config") -Value $uiWebConfig
    Write-SubStep "web.config created"

    # Create IIS site
    Write-Step "Creating IIS Application Pool & Site..."
    Import-Module WebAdministration -ErrorAction SilentlyContinue

    if (-not (Test-Path "IIS:\AppPools\$($CONFIG.UiAppPoolName)" -ErrorAction SilentlyContinue)) {
        New-WebAppPool -Name $CONFIG.UiAppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$($CONFIG.UiAppPoolName)" -Name "managedRuntimeVersion" -Value ""

    $existingSite = Get-Website -Name $CONFIG.UiSiteName -ErrorAction SilentlyContinue
    if (-not $existingSite) {
        New-Website -Name $CONFIG.UiSiteName -PhysicalPath $CONFIG.UiPhysicalPath -ApplicationPool $CONFIG.UiAppPoolName -Port $CONFIG.UiPort | Out-Null
    }

    # Set permissions
    $identity = "IIS AppPool\$($CONFIG.UiAppPoolName)"
    $acl = Get-Acl $CONFIG.UiPhysicalPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl $CONFIG.UiPhysicalPath $acl

    Start-Website -Name $CONFIG.UiSiteName -ErrorAction SilentlyContinue
    Write-Success "Frontend deployed at http://$($CONFIG.ServerIP):$($CONFIG.UiPort)"
} else {
    Write-Warn "BranchMonitorFrontEnd project not found at: $uiProjectPath"
}

Write-LogFile "Frontend deployed"

# ============================================================
# STEP 8: BUILD & DEPLOY VNC PROXY
# ============================================================

Write-Banner "STEP 8: VNC Proxy API Deployment"

$vncProjectPath = Join-Path $CONFIG.SolutionPath "BranchConnectVNCProxyAPI\BranchConnectVNCProxyAPI"

if (Test-Path $vncProjectPath) {
    Write-Step "Building VNC Proxy..."

    try {
        Push-Location $vncProjectPath
        dotnet restore 2>&1 | Out-Null
        dotnet publish -c Release -o $CONFIG.VncPhysicalPath --no-restore 2>&1 | ForEach-Object { Write-SubStep $_ }
        Pop-Location
        Write-SubStep "Build completed"
    } catch {
        Pop-Location
        Write-Warn "Build failed. Please build manually."
    }

    # web.config for VNC Proxy
    Write-Step "Creating VNC Proxy web.config..."
    $vncWebConfig = @"
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
                  stdoutLogFile="$($CONFIG.LogsPath)\VNC\stdout"
                  hostingModel="InProcess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
      <webSocket enabled="true" receiveBufferLimit="67108864" />
    </system.webServer>
  </location>
</configuration>
"@
    Set-Content -Path (Join-Path $CONFIG.VncPhysicalPath "web.config") -Value $vncWebConfig

    # Create IIS site
    Write-Step "Creating IIS Application Pool & Site..."
    Import-Module WebAdministration -ErrorAction SilentlyContinue

    if (-not (Test-Path "IIS:\AppPools\$($CONFIG.VncAppPoolName)" -ErrorAction SilentlyContinue)) {
        New-WebAppPool -Name $CONFIG.VncAppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$($CONFIG.VncAppPoolName)" -Name "managedRuntimeVersion" -Value ""

    $existingSite = Get-Website -Name $CONFIG.VncSiteName -ErrorAction SilentlyContinue
    if (-not $existingSite) {
        New-Website -Name $CONFIG.VncSiteName -PhysicalPath $CONFIG.VncPhysicalPath -ApplicationPool $CONFIG.VncAppPoolName -Port $CONFIG.VncPort | Out-Null
    }

    Start-Website -Name $CONFIG.VncSiteName -ErrorAction SilentlyContinue
    Write-Success "VNC Proxy deployed at http://$($CONFIG.ServerIP):$($CONFIG.VncPort)"
} else {
    Write-Warn "VNC Proxy project not found at: $vncProjectPath"
}

Write-LogFile "VNC Proxy deployed"

# ============================================================
# STEP 9: FIREWALL RULES
# ============================================================

Write-Banner "STEP 9: Firewall Rules"

if (Ask-YesNo "Configure firewall rules?") {
    $rules = @(
        @{ Name = "Kiosk Monitor - API HTTP";     Port = $CONFIG.ApiPort;       Protocol = "TCP" },
        @{ Name = "Kiosk Monitor - UI HTTP";      Port = $CONFIG.UiPort;        Protocol = "TCP" },
        @{ Name = "Kiosk Monitor - VNC Proxy";    Port = $CONFIG.VncPort;       Protocol = "TCP" },
        @{ Name = "Kiosk Monitor - MQTT Plain";   Port = $CONFIG.MqttPlainPort; Protocol = "TCP" },
        @{ Name = "Kiosk Monitor - MQTT TLS";     Port = $CONFIG.MqttPort;      Protocol = "TCP" },
        @{ Name = "Kiosk Monitor - SSH/SFTP";     Port = 22;                    Protocol = "TCP" }
    )

    foreach ($rule in $rules) {
        $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        if (-not $existing) {
            New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Protocol $rule.Protocol -LocalPort $rule.Port -Action Allow | Out-Null
            Write-SubStep "Created: $($rule.Name) (Port $($rule.Port))"
        } else {
            Write-SubStep "Exists: $($rule.Name)"
        }
    }
    Write-Success "Firewall rules configured"
}

Write-LogFile "Firewall configured"

# ============================================================
# STEP 10: HEALTH CHECK SCRIPT
# ============================================================

Write-Banner "STEP 10: Health Check Script"

$healthScript = @"
# Health Check - Auto-generated $(Get-Date -Format "yyyy-MM-dd")
`$logFile = "$($CONFIG.LogsPath)\HealthCheck\health_`$(Get-Date -Format 'yyyyMMdd').log"
function Log(`$msg, `$lvl="INFO") { "`$(Get-Date -Format 'HH:mm:ss') [`$lvl] `$msg" | Add-Content `$logFile }

Log "Health check started"
@("W3SVC","mosquitto","sshd") | ForEach-Object {
    `$svc = Get-Service `$_ -ErrorAction SilentlyContinue
    if (`$svc -and `$svc.Status -eq "Running") { Log "`$_ is running" }
    else { Log "`$_ is NOT running!" "ERROR"; Start-Service `$_ -ErrorAction SilentlyContinue }
}
@(22,$($CONFIG.MqttPlainPort),$($CONFIG.ApiPort),$($CONFIG.UiPort),$($CONFIG.VncPort)) | ForEach-Object {
    try { `$c = New-Object Net.Sockets.TcpClient("127.0.0.1",`$_); `$c.Close(); Log "Port `$_ open" }
    catch { Log "Port `$_ CLOSED!" "ERROR" }
}
Log "Health check completed"
"@

$healthScriptPath = Join-Path $CONFIG.ScriptsPath "HealthCheck.ps1"
Set-Content -Path $healthScriptPath -Value $healthScript
Write-Step "Health check script created"

# Create scheduled task
if (Ask-YesNo "Create scheduled health check (every 5 min)?") {
    $existingTask = Get-ScheduledTask -TaskName "KioskMonitor-HealthCheck" -ErrorAction SilentlyContinue
    if (-not $existingTask) {
        $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-ExecutionPolicy Bypass -File `"$healthScriptPath`""
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 9999)
        $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
        Register-ScheduledTask -TaskName "KioskMonitor-HealthCheck" -Action $action -Trigger $trigger -Principal $principal | Out-Null
        Write-SubStep "Scheduled task created"
    }
}

Write-LogFile "Health check configured"

# ============================================================
# STEP 11: GENERATE BRANCH CONFIG
# ============================================================

Write-Banner "STEP 11: Branch Configuration Template"

Write-Step "Generating branch configuration template..."

$branchConfig = @{
    BranchId          = "BRANCH_CHANGE_THIS"
    ServiceName       = "Branch_monitoring_service"
    ConnectionStrings = @{
        BranchDb = "Server=localhost;Database=CDK_BRN;User Id=sa;Password=CHANGE_THIS;TrustServerCertificate=True;"
    }
    API = @{
        Host = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)"
    }
    Sftp = @{
        Host     = $CONFIG.ServerIP
        Username = $CONFIG.SftpUser
        Password = $CONFIG.SftpPassword
        Port     = 22
    }
    MQTT = @{
        Host     = $CONFIG.ServerIP
        Port     = $CONFIG.MqttPlainPort
        Username = $CONFIG.MqttBranchUser
        Password = $CONFIG.MqttBranchPassword
    }
    MainPath = @{
        PathUrl = "C:\CDK_Monitoring"
    }
    SubPath = @{
        Log   = "Log"
        Patch = "Patch"
        Db    = "DB_Data"
    }
    Log = @{
        DelayLog      = "DelayLog"
        InitialLog    = "InitialLog"
        ExceptionLog  = "ExceptionLog"
        ConnectionLog = "ConnectionLog"
    }
    Patch = @{
        ApplicationPatch = @{
            MainAppName     = "Bank_Cheque_printer.exe"
            SecondAppName   = "Bank_Cheque_printer.exe"
            AppFolder       = "C:\Patch\Application\App"
            BackupRoot      = "Application\Backups"
            UpdateRoot      = "Application\NewVersion"
            DownloadsPath   = "Application\Downloads"
            MaxBackupsToKeep = 4
        }
    }
} | ConvertTo-Json -Depth 4

$branchConfigPath = Join-Path $CONFIG.SolutionPath "Setup\Branch\branch-appsettings-template.json"
New-Item -ItemType Directory -Path (Split-Path $branchConfigPath) -Force | Out-Null
Set-Content -Path $branchConfigPath -Value $branchConfig
Write-SubStep "Branch config template: $branchConfigPath"

Write-Success "Branch configuration template generated"
Write-LogFile "Branch config template generated"

# ============================================================
# FINAL SUMMARY
# ============================================================

Write-Banner "INSTALLATION COMPLETE"

$summary = @"

  Server Configuration Summary
  =============================

  Server IP:         $($CONFIG.ServerIP)

  SERVICES:
    MQTT Broker:     Port $($CONFIG.MqttPlainPort) (Plain) / $($CONFIG.MqttPort) (TLS)
    SFTP Server:     Port 22
    API Backend:     http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)
    Frontend UI:     http://$($CONFIG.ServerIP):$($CONFIG.UiPort)
    VNC Proxy:       http://$($CONFIG.ServerIP):$($CONFIG.VncPort)
    SignalR Hub:     http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)/branchHub

  DATABASE:
    Server:          $($CONFIG.SqlServerInstance)
    Database:        $($CONFIG.SqlDatabaseName)
    User:            $($CONFIG.SqlAppUser)

  MQTT ACCOUNTS:
    Backend:         $($CONFIG.MqttUser)
    Branch:          $($CONFIG.MqttBranchUser)

  SFTP:
    User:            $($CONFIG.SftpUser)
    Root:            $($CONFIG.SftpRootPath)

  PATHS:
    API:             $($CONFIG.ApiPhysicalPath)
    Frontend:        $($CONFIG.UiPhysicalPath)
    VNC Proxy:       $($CONFIG.VncPhysicalPath)
    Certificates:    $($CONFIG.CertsPath)
    Logs:            $($CONFIG.LogsPath)
    Backups:         $($CONFIG.BackupsPath)
    Scripts:         $($CONFIG.ScriptsPath)

  NEXT STEPS:
    1. Run EF Core migrations:
       cd "$apiProjectPath"
       dotnet ef database update

    2. Open browser: http://$($CONFIG.ServerIP):$($CONFIG.UiPort)

    3. For branch setup, copy: Setup\Branch\

"@

Write-Host $summary -ForegroundColor White

# Save config summary
$summary | Set-Content (Join-Path $CONFIG.LogsPath "install-summary.txt")

Write-LogFile "Installation completed successfully"
Write-Host ""
Write-Host "  Log file: $($CONFIG.LogsPath)\server-install-$(Get-Date -Format 'yyyyMMdd').log" -ForegroundColor Gray
Write-Host ""
