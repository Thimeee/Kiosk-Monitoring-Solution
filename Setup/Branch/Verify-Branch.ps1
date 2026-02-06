#Requires -RunAsAdministrator
# ============================================================
# Kiosk Monitoring Solution - Branch Verification Script
# ============================================================
# Usage: .\Verify-Branch.ps1 -ServerIP "192.168.1.24"
# ============================================================

param(
    [string]$ServerIP = "192.168.1.24",
    [int]$ApiPort = 5155,
    [int]$MqttPort = 1883,
    [int]$SftpPort = 22,
    [string]$MainPath = "C:\CDK_Monitoring",
    [string]$ServiceName = "BranchMonitoringService"
)

$Host.UI.RawUI.WindowTitle = "Kiosk Monitoring - Branch Verification"

Clear-Host
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║     Branch Health Verification                       ║" -ForegroundColor Green
Write-Host "  ║     Server: $ServerIP                                 " -ForegroundColor Green
Write-Host "  ╚══════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

$passed = 0
$failed = 0

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

# ---- LOCAL SERVICE ----
Write-Host "  LOCAL SERVICE" -ForegroundColor Yellow
Write-Host "  -------------" -ForegroundColor Yellow

Test-Check "Branch Monitoring Service" {
    $svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
    $svc -and $svc.Status -eq "Running"
}

Test-Check "Local SQL Server" {
    $svc = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
    if (-not $svc) { $svc = Get-Service "MSSQL`$*" -ErrorAction SilentlyContinue | Select-Object -First 1 }
    $svc -and $svc.Status -eq "Running"
}

# ---- SERVER CONNECTIVITY ----
Write-Host ""
Write-Host "  SERVER CONNECTIVITY" -ForegroundColor Yellow
Write-Host "  -------------------" -ForegroundColor Yellow

Test-Check "Ping Server ($ServerIP)" {
    $ping = Test-Connection $ServerIP -Count 2 -Quiet
    return $ping
}

$remoteTests = @(
    @{ Port = $ApiPort;  Name = "API ($ServerIP`:$ApiPort)" },
    @{ Port = $MqttPort; Name = "MQTT ($ServerIP`:$MqttPort)" },
    @{ Port = $SftpPort; Name = "SFTP ($ServerIP`:$SftpPort)" }
)

foreach ($t in $remoteTests) {
    Test-Check $t.Name {
        $tcp = New-Object System.Net.Sockets.TcpClient
        try {
            $tcp.Connect($ServerIP, $t.Port)
            $tcp.Close()
            return $true
        } catch { return $false }
    }.GetNewClosure()
}

Test-Check "API HTTP Response" {
    try {
        $r = Invoke-WebRequest -Uri "http://$($ServerIP):$ApiPort/weatherforecast" -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        return $r.StatusCode -eq 200
    } catch { return $false }
}

# ---- LOCAL DIRECTORIES ----
Write-Host ""
Write-Host "  LOCAL DIRECTORIES" -ForegroundColor Yellow
Write-Host "  -----------------" -ForegroundColor Yellow

$dirs = @(
    "$MainPath",
    "$MainPath\Log",
    "$MainPath\Log\ConnectionLog",
    "$MainPath\Log\ExceptionLog",
    "$MainPath\Patch",
    "$MainPath\DB_Data",
    "$MainPath\Service"
)

foreach ($dir in $dirs) {
    Test-Check "Dir: $dir" {
        Test-Path $dir
    }.GetNewClosure()
}

# ---- CONFIG FILE ----
Write-Host ""
Write-Host "  CONFIGURATION" -ForegroundColor Yellow
Write-Host "  -------------" -ForegroundColor Yellow

Test-Check "appsettings.json exists" {
    Test-Path "$MainPath\Config\appsettings.json" -or
    Test-Path "$MainPath\Service\appsettings.json"
}

# ---- RECENT LOGS ----
Write-Host ""
Write-Host "  RECENT LOGS" -ForegroundColor Yellow
Write-Host "  -----------" -ForegroundColor Yellow

$logDirs = @("ConnectionLog", "ExceptionLog", "InitialLog")
foreach ($logDir in $logDirs) {
    $logPath = "$MainPath\Log\$logDir"
    $recentLog = Get-ChildItem $logPath -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($recentLog) {
        $age = (Get-Date) - $recentLog.LastWriteTime
        if ($age.TotalHours -lt 24) {
            Write-Host "  $logDir : $($recentLog.Name) ($('{0:N0}' -f $age.TotalMinutes) min ago)" -ForegroundColor Green
        } else {
            Write-Host "  $logDir : $($recentLog.Name) ($('{0:N0}' -f $age.TotalHours) hours ago)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  $logDir : No logs found" -ForegroundColor Gray
    }
}

# ---- SUMMARY ----
Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "  RESULTS: $passed PASSED | $failed FAILED" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

if ($failed -eq 0) {
    Write-Host "  All checks passed! Branch is operational." -ForegroundColor Green
} else {
    Write-Host "  $failed check(s) failed. Review issues above." -ForegroundColor Red
}
Write-Host ""
