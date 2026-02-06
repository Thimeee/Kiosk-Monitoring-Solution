#Requires -RunAsAdministrator
# ============================================================
# Kiosk Monitoring Solution - Server Verification Script
# ============================================================
# Checks all services and connectivity after installation
# Usage: .\Verify-Server.ps1
# ============================================================

$Host.UI.RawUI.WindowTitle = "Kiosk Monitoring - Server Verification"

Clear-Host
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║     Server Health Verification                       ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$passed = 0
$failed = 0
$warnings = 0

function Test-Check {
    param([string]$Name, [scriptblock]$Test)
    Write-Host "  Checking: $Name... " -NoNewline
    try {
        $result = & $Test
        if ($result) {
            Write-Host "PASS" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "FAIL" -ForegroundColor Red
            $script:failed++
        }
    } catch {
        Write-Host "FAIL ($($_.Exception.Message))" -ForegroundColor Red
        $script:failed++
    }
}

# ---- WINDOWS SERVICES ----
Write-Host ""
Write-Host "  WINDOWS SERVICES" -ForegroundColor Yellow
Write-Host "  ----------------" -ForegroundColor Yellow

Test-Check "IIS (W3SVC)" {
    $svc = Get-Service W3SVC -ErrorAction SilentlyContinue
    $svc -and $svc.Status -eq "Running"
}

Test-Check "Mosquitto MQTT" {
    $svc = Get-Service mosquitto -ErrorAction SilentlyContinue
    $svc -and $svc.Status -eq "Running"
}

Test-Check "OpenSSH Server" {
    $svc = Get-Service sshd -ErrorAction SilentlyContinue
    $svc -and $svc.Status -eq "Running"
}

Test-Check "SQL Server" {
    $svc = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
    if (-not $svc) { $svc = Get-Service "MSSQL`$*" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    $svc -and $svc.Status -eq "Running"
}

# ---- TCP PORTS ----
Write-Host ""
Write-Host "  TCP PORTS" -ForegroundColor Yellow
Write-Host "  ---------" -ForegroundColor Yellow

$ports = @(
    @{ Port = 22;   Name = "SSH/SFTP (22)" },
    @{ Port = 1883; Name = "MQTT Plain (1883)" },
    @{ Port = 5155; Name = "API Backend (5155)" },
    @{ Port = 5235; Name = "Frontend UI (5235)" },
    @{ Port = 7128; Name = "VNC Proxy (7128)" }
)

foreach ($p in $ports) {
    Test-Check $p.Name {
        $tcp = New-Object System.Net.Sockets.TcpClient
        try {
            $tcp.Connect("127.0.0.1", $p.Port)
            $tcp.Close()
            return $true
        } catch {
            return $false
        }
    }.GetNewClosure()
}

# ---- IIS WEBSITES ----
Write-Host ""
Write-Host "  IIS WEBSITES" -ForegroundColor Yellow
Write-Host "  ------------" -ForegroundColor Yellow

Import-Module WebAdministration -ErrorAction SilentlyContinue

$sites = @("MonitoringAPI", "MonitoringUI", "VNCProxy")
foreach ($site in $sites) {
    Test-Check "IIS Site: $site" {
        $s = Get-Website -Name $site -ErrorAction SilentlyContinue
        $s -and $s.State -eq "Started"
    }.GetNewClosure()
}

# ---- IIS APP POOLS ----
Write-Host ""
Write-Host "  IIS APP POOLS" -ForegroundColor Yellow
Write-Host "  -------------" -ForegroundColor Yellow

$pools = @("MonitoringAPIPool", "MonitoringUIPool", "VNCProxyPool")
foreach ($pool in $pools) {
    Test-Check "App Pool: $pool" {
        $p = Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue
        $p -and $p.Value -eq "Started"
    }.GetNewClosure()
}

# ---- HTTP ENDPOINTS ----
Write-Host ""
Write-Host "  HTTP ENDPOINTS" -ForegroundColor Yellow
Write-Host "  --------------" -ForegroundColor Yellow

Test-Check "API /weatherforecast" {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:5155/weatherforecast" -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        return $r.StatusCode -eq 200
    } catch { return $false }
}

# ---- DIRECTORIES ----
Write-Host ""
Write-Host "  DIRECTORIES" -ForegroundColor Yellow
Write-Host "  -----------" -ForegroundColor Yellow

$dirs = @(
    "C:\Mosquitto",
    "C:\SFTP",
    "C:\SFTP\Patches",
    "C:\Monitoring\Terminals",
    "C:\Monitoring\Logs",
    "C:\inetpub\wwwroot\MonitoringAPI",
    "C:\inetpub\wwwroot\MonitoringUI",
    "C:\inetpub\wwwroot\VNCProxy"
)

foreach ($dir in $dirs) {
    Test-Check "Directory: $dir" {
        Test-Path $dir
    }.GetNewClosure()
}

# ---- SUMMARY ----
Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "  RESULTS: $passed PASSED | $failed FAILED" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

if ($failed -eq 0) {
    Write-Host "  All checks passed! Server is ready." -ForegroundColor Green
} else {
    Write-Host "  $failed check(s) failed. Review and fix the issues above." -ForegroundColor Red
}
Write-Host ""
