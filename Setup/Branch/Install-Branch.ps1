#Requires -RunAsAdministrator
# ============================================================
# Kiosk Monitoring Solution - Branch Setup Installer
# ============================================================
# Run as Administrator: Right-click PowerShell > Run as Administrator
# Usage: .\Install-Branch.ps1
# ============================================================

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Kiosk Monitoring - Branch Installer"

# ============================================================
# CONFIGURATION - EDIT THESE VALUES BEFORE RUNNING
# ============================================================

$CONFIG = @{
    # Branch Identity
    BranchId        = ""                                    # Set during wizard
    BranchName      = ""

    # Central Server Connection
    ServerIP        = "192.168.1.24"
    ApiPort         = 5155

    # MQTT
    MqttHost        = ""                                    # Auto-set from ServerIP
    MqttPort        = 1883
    MqttUsername    = "BranchService"
    MqttPassword    = "BranchMqtt@2026!"

    # SFTP
    SftpHost        = ""                                    # Auto-set from ServerIP
    SftpPort        = 22
    SftpUsername     = "sftpuser"
    SftpPassword    = "Sftp@Secure2026!"

    # Local Database
    SqlServer       = "localhost"
    SqlDatabase     = "CDK_BRN"
    SqlUser         = "sa"
    SqlPassword     = ""

    # Paths
    MainPath        = "C:\CDK_Monitoring"
    AppFolder       = "C:\Patch\Application\App"
    MainAppName     = "Bank_Cheque_printer.exe"
    SecondAppName   = "Bank_Cheque_printer.exe"
    MaxBackups      = 4

    # Service
    ServiceName     = "BranchMonitoringService"
    ServiceDisplay  = "Kiosk Branch Monitoring Service"

    # Source
    ServiceSourcePath = ""                                  # Auto-detected or manual
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

function Write-LogFile {
    param([string]$Message)
    $logDir = Join-Path $CONFIG.MainPath "Log\InstallLog"
    New-Item -ItemType Directory -Path $logDir -Force -ErrorAction SilentlyContinue | Out-Null
    $logFile = Join-Path $logDir "install-$(Get-Date -Format 'yyyyMMdd').log"
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp | $Message" | Add-Content -Path $logFile -ErrorAction SilentlyContinue
}

# ============================================================
# MAIN INSTALLER
# ============================================================

Clear-Host

Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║                                                      ║" -ForegroundColor Green
Write-Host "  ║     KIOSK MONITORING SOLUTION                        ║" -ForegroundColor Green
Write-Host "  ║     Branch Setup Installer v1.0                      ║" -ForegroundColor Green
Write-Host "  ║                                                      ║" -ForegroundColor Green
Write-Host "  ╚══════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  This installer will configure:" -ForegroundColor White
Write-Host "    1. Folder Structure" -ForegroundColor Gray
Write-Host "    2. Branch Configuration" -ForegroundColor Gray
Write-Host "    3. SFTPService Build & Deploy" -ForegroundColor Gray
Write-Host "    4. Windows Service Installation" -ForegroundColor Gray
Write-Host "    5. Connectivity Test" -ForegroundColor Gray
Write-Host ""

if (-not (Ask-YesNo "Do you want to continue?")) {
    Write-Host "  Setup cancelled." -ForegroundColor Yellow
    exit
}

# ============================================================
# STEP 0: COLLECT CONFIGURATION
# ============================================================

Write-Banner "STEP 0: Branch Configuration"

# Branch Identity
$CONFIG.BranchId = Ask-Input "Branch ID (e.g., BRANCH001)" "BRANCH001"
$CONFIG.BranchName = Ask-Input "Branch Name (e.g., Main Branch)" "Branch $($CONFIG.BranchId)"

# Server Connection
Write-Host ""
Write-Step "Central Server Connection:"
$CONFIG.ServerIP = Ask-Input "  Server IP Address" $CONFIG.ServerIP
$CONFIG.ApiPort = Ask-Input "  API Port" $CONFIG.ApiPort
$CONFIG.MqttHost = $CONFIG.ServerIP
$CONFIG.SftpHost = $CONFIG.ServerIP

# MQTT
Write-Host ""
Write-Step "MQTT Configuration:"
$CONFIG.MqttPort = Ask-Input "  MQTT Port" $CONFIG.MqttPort
$CONFIG.MqttUsername = Ask-Input "  MQTT Username" $CONFIG.MqttUsername
$CONFIG.MqttPassword = Ask-Input "  MQTT Password" $CONFIG.MqttPassword

# SFTP
Write-Host ""
Write-Step "SFTP Configuration:"
$CONFIG.SftpUsername = Ask-Input "  SFTP Username" $CONFIG.SftpUsername
$CONFIG.SftpPassword = Ask-Input "  SFTP Password" $CONFIG.SftpPassword

# Local Database
Write-Host ""
Write-Step "Local Database:"
$CONFIG.SqlServer = Ask-Input "  SQL Server Instance" $CONFIG.SqlServer
$CONFIG.SqlDatabase = Ask-Input "  Database Name" $CONFIG.SqlDatabase
$CONFIG.SqlUser = Ask-Input "  SQL User" $CONFIG.SqlUser
$CONFIG.SqlPassword = Ask-Input "  SQL Password" $CONFIG.SqlPassword

# Application
Write-Host ""
Write-Step "Application Configuration:"
$CONFIG.MainAppName = Ask-Input "  Main Application EXE Name" $CONFIG.MainAppName
$CONFIG.AppFolder = Ask-Input "  Application Folder" $CONFIG.AppFolder
$CONFIG.MainPath = Ask-Input "  Monitoring Root Path" $CONFIG.MainPath

# Source
Write-Host ""
$scriptDir = Split-Path -Parent $PSScriptRoot
$solutionDir = Split-Path -Parent $scriptDir
$defaultSource = Join-Path $solutionDir "SFTPService"
if (-not (Test-Path $defaultSource -ErrorAction SilentlyContinue)) {
    $defaultSource = ""
}
$CONFIG.ServiceSourcePath = Ask-Input "SFTPService project path" $defaultSource

Write-Host ""
Write-Success "Configuration collected"

# ============================================================
# STEP 1: CREATE FOLDER STRUCTURE
# ============================================================

Write-Banner "STEP 1: Creating Folder Structure"

$folders = @(
    $CONFIG.MainPath,
    "$($CONFIG.MainPath)\Log",
    "$($CONFIG.MainPath)\Log\DelayLog",
    "$($CONFIG.MainPath)\Log\InitialLog",
    "$($CONFIG.MainPath)\Log\ExceptionLog",
    "$($CONFIG.MainPath)\Log\ConnectionLog",
    "$($CONFIG.MainPath)\Log\InstallLog",
    "$($CONFIG.MainPath)\Patch",
    "$($CONFIG.MainPath)\DB_Data",
    "$($CONFIG.MainPath)\Config",
    $CONFIG.AppFolder,
    "$(Split-Path $CONFIG.AppFolder)\Backups",
    "$(Split-Path $CONFIG.AppFolder)\NewVersion",
    "$(Split-Path $CONFIG.AppFolder)\Downloads"
)

foreach ($folder in $folders) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Write-SubStep "Created: $folder"
}

Write-Success "Folder structure created"
Write-LogFile "Folder structure created"

# ============================================================
# STEP 2: CREATE CONFIGURATION FILE
# ============================================================

Write-Banner "STEP 2: Creating Configuration"

$connectionString = "Server=$($CONFIG.SqlServer);Database=$($CONFIG.SqlDatabase);User Id=$($CONFIG.SqlUser);Password=$($CONFIG.SqlPassword);TrustServerCertificate=True;"

$appSettings = @{
    BranchId = $CONFIG.BranchId
    ServiceName = $CONFIG.ServiceDisplay
    ConnectionStrings = @{
        BranchDb = $connectionString
    }
    API = @{
        Host = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)"
    }
    Sftp = @{
        Host     = $CONFIG.SftpHost
        Username = $CONFIG.SftpUsername
        Password = $CONFIG.SftpPassword
        Port     = [int]$CONFIG.SftpPort
    }
    MQTT = @{
        Host     = $CONFIG.MqttHost
        Port     = [int]$CONFIG.MqttPort
        Username = $CONFIG.MqttUsername
        Password = $CONFIG.MqttPassword
    }
    MainPath = @{
        PathUrl = $CONFIG.MainPath
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
            MainAppName      = $CONFIG.MainAppName
            SecondAppName    = $CONFIG.SecondAppName
            AppFolder        = $CONFIG.AppFolder
            BackupRoot       = "Application\Backups"
            UpdateRoot       = "Application\NewVersion"
            DownloadsPath    = "Application\Downloads"
            MaxBackupsToKeep = [int]$CONFIG.MaxBackups
        }
    }
    Logging = @{
        LogLevel = @{
            Default = "Information"
            "Microsoft.Hosting.Lifetime" = "Information"
        }
    }
} | ConvertTo-Json -Depth 5

$configPath = "$($CONFIG.MainPath)\Config\appsettings.json"
Set-Content -Path $configPath -Value $appSettings
Write-Step "Configuration saved: $configPath"

Write-Success "Configuration file created"
Write-LogFile "Configuration created"

# ============================================================
# STEP 3: BUILD & DEPLOY SERVICE
# ============================================================

Write-Banner "STEP 3: Build & Deploy SFTPService"

$serviceDeployPath = "$($CONFIG.MainPath)\Service"
New-Item -ItemType Directory -Path $serviceDeployPath -Force | Out-Null

if (Test-Path $CONFIG.ServiceSourcePath) {
    Write-Step "Building SFTPService..."

    try {
        Push-Location $CONFIG.ServiceSourcePath
        dotnet restore 2>&1 | Out-Null
        dotnet publish -c Release -o $serviceDeployPath --no-restore 2>&1 | ForEach-Object { Write-SubStep $_ }
        Pop-Location
        Write-SubStep "Build completed"

        # Copy config
        Copy-Item $configPath -Destination (Join-Path $serviceDeployPath "appsettings.json") -Force
        Write-SubStep "Configuration copied to service folder"

        Write-Success "Service built and deployed to: $serviceDeployPath"
    } catch {
        Pop-Location
        Write-Err "Build failed: $_"
        Write-Host "  Please build manually:" -ForegroundColor Yellow
        Write-Host "    cd `"$($CONFIG.ServiceSourcePath)`"" -ForegroundColor Yellow
        Write-Host "    dotnet publish -c Release -o `"$serviceDeployPath`"" -ForegroundColor Yellow
    }
} else {
    Write-Warn "SFTPService source not found."

    # Check if pre-built binaries exist
    $preBuildPath = Join-Path (Split-Path $PSScriptRoot) "Branch\Binaries"
    if (Test-Path $preBuildPath) {
        Write-Step "Copying pre-built binaries..."
        Copy-Item "$preBuildPath\*" -Destination $serviceDeployPath -Recurse -Force
        Copy-Item $configPath -Destination (Join-Path $serviceDeployPath "appsettings.json") -Force
        Write-Success "Pre-built binaries copied"
    } else {
        Write-Host "  Please copy the published SFTPService to: $serviceDeployPath" -ForegroundColor Yellow
        Write-Host "  Then copy appsettings.json from: $configPath" -ForegroundColor Yellow
    }
}

Write-LogFile "Service deployed"

# ============================================================
# STEP 4: INSTALL WINDOWS SERVICE
# ============================================================

Write-Banner "STEP 4: Install Windows Service"

$serviceDll = Join-Path $serviceDeployPath "SFTPService.dll"
$serviceExe = Join-Path $serviceDeployPath "SFTPService.exe"

# Check which executable exists
$executablePath = ""
if (Test-Path $serviceExe) {
    $executablePath = $serviceExe
} elseif (Test-Path $serviceDll) {
    $executablePath = "dotnet `"$serviceDll`""
}

if ($executablePath) {
    Write-Step "Installing Windows Service..."

    # Stop existing service
    $existingService = Get-Service -Name $CONFIG.ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-SubStep "Stopping existing service..."
        Stop-Service -Name $CONFIG.ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        sc.exe delete $CONFIG.ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-SubStep "Existing service removed"
    }

    # Install service
    if (Test-Path $serviceExe) {
        # Self-contained or framework-dependent exe
        sc.exe create $CONFIG.ServiceName binPath= "`"$serviceExe`"" start= auto DisplayName= "`"$($CONFIG.ServiceDisplay)`"" | Out-Null
    } else {
        # DLL-based (dotnet run)
        sc.exe create $CONFIG.ServiceName binPath= "dotnet `"$serviceDll`"" start= auto DisplayName= "`"$($CONFIG.ServiceDisplay)`"" | Out-Null
    }

    # Configure service
    sc.exe description $CONFIG.ServiceName "Kiosk Branch Monitoring Service - $($CONFIG.BranchId)" | Out-Null
    sc.exe failure $CONFIG.ServiceName reset= 86400 actions= restart/60000/restart/120000/restart/300000 | Out-Null

    Write-SubStep "Service installed with auto-restart on failure"

    # Start service
    if (Ask-YesNo "Start the service now?") {
        Start-Service -Name $CONFIG.ServiceName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3

        $svc = Get-Service -Name $CONFIG.ServiceName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq "Running") {
            Write-Success "Service is running"
        } else {
            Write-Warn "Service may not have started. Check Event Viewer for details."
        }
    }
} else {
    Write-Warn "Service executable not found in: $serviceDeployPath"
    Write-Host "  Please build the service first and re-run this step." -ForegroundColor Yellow
}

Write-LogFile "Windows Service installed"

# ============================================================
# STEP 5: CONNECTIVITY TEST
# ============================================================

Write-Banner "STEP 5: Connectivity Test"

Write-Step "Testing connections to central server..."

# Test API
Write-SubStep "Testing API connection..."
try {
    $apiUrl = "http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)/weatherforecast"
    $response = Invoke-WebRequest -Uri $apiUrl -Method Get -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
    if ($response.StatusCode -eq 200) {
        Write-Host "      API:  CONNECTED" -ForegroundColor Green
    }
} catch {
    Write-Host "      API:  FAILED - $($_.Exception.Message)" -ForegroundColor Red
}

# Test MQTT
Write-SubStep "Testing MQTT connection..."
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect($CONFIG.MqttHost, [int]$CONFIG.MqttPort)
    $tcp.Close()
    Write-Host "      MQTT: CONNECTED (Port $($CONFIG.MqttPort))" -ForegroundColor Green
} catch {
    Write-Host "      MQTT: FAILED - Cannot reach $($CONFIG.MqttHost):$($CONFIG.MqttPort)" -ForegroundColor Red
}

# Test SFTP
Write-SubStep "Testing SFTP connection..."
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect($CONFIG.SftpHost, [int]$CONFIG.SftpPort)
    $tcp.Close()
    Write-Host "      SFTP: CONNECTED (Port $($CONFIG.SftpPort))" -ForegroundColor Green
} catch {
    Write-Host "      SFTP: FAILED - Cannot reach $($CONFIG.SftpHost):$($CONFIG.SftpPort)" -ForegroundColor Red
}

# Test Local Database
Write-SubStep "Testing local database..."
try {
    $connStr = "Server=$($CONFIG.SqlServer);Database=master;User Id=$($CONFIG.SqlUser);Password=$($CONFIG.SqlPassword);TrustServerCertificate=True;Connection Timeout=5;"
    $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
    $conn.Open()
    $conn.Close()
    Write-Host "      SQL:  CONNECTED" -ForegroundColor Green
} catch {
    Write-Host "      SQL:  FAILED - $($_.Exception.Message)" -ForegroundColor Red
}

Write-LogFile "Connectivity tests completed"

# ============================================================
# STEP 6: FIREWALL RULES
# ============================================================

Write-Banner "STEP 6: Firewall Rules"

if (Ask-YesNo "Configure outbound firewall rules?") {
    $rules = @(
        @{ Name = "Kiosk Branch - MQTT Out";  Port = $CONFIG.MqttPort; Direction = "Outbound" },
        @{ Name = "Kiosk Branch - SFTP Out";  Port = $CONFIG.SftpPort; Direction = "Outbound" },
        @{ Name = "Kiosk Branch - API Out";   Port = $CONFIG.ApiPort;  Direction = "Outbound" }
    )

    foreach ($rule in $rules) {
        $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        if (-not $existing) {
            New-NetFirewallRule -DisplayName $rule.Name -Direction Outbound -Protocol TCP -RemotePort $rule.Port -Action Allow | Out-Null
            Write-SubStep "Created: $($rule.Name)"
        } else {
            Write-SubStep "Exists: $($rule.Name)"
        }
    }
    Write-Success "Firewall rules configured"
}

# ============================================================
# FINAL SUMMARY
# ============================================================

Write-Banner "INSTALLATION COMPLETE"

$summary = @"

  Branch Configuration Summary
  ============================

  Branch ID:       $($CONFIG.BranchId)
  Branch Name:     $($CONFIG.BranchName)

  SERVER CONNECTION:
    API:           http://$($CONFIG.ServerIP):$($CONFIG.ApiPort)
    MQTT:          $($CONFIG.MqttHost):$($CONFIG.MqttPort)
    SFTP:          $($CONFIG.SftpHost):$($CONFIG.SftpPort)

  LOCAL:
    Database:      $($CONFIG.SqlServer) / $($CONFIG.SqlDatabase)
    Service:       $($CONFIG.ServiceName)
    Root Path:     $($CONFIG.MainPath)
    App Folder:    $($CONFIG.AppFolder)
    App Name:      $($CONFIG.MainAppName)

  FILES:
    Service:       $serviceDeployPath
    Config:        $configPath
    Logs:          $($CONFIG.MainPath)\Log\

  MANAGEMENT:
    Start:   Start-Service $($CONFIG.ServiceName)
    Stop:    Stop-Service $($CONFIG.ServiceName)
    Status:  Get-Service $($CONFIG.ServiceName)
    Logs:    Get-Content "$($CONFIG.MainPath)\Log\ConnectionLog\*" -Tail 50

"@

Write-Host $summary -ForegroundColor White

# Save summary
$summaryPath = "$($CONFIG.MainPath)\Log\InstallLog\install-summary.txt"
$summary | Set-Content $summaryPath
Write-Host "  Summary saved: $summaryPath" -ForegroundColor Gray

Write-LogFile "Branch installation completed"
Write-Host ""
