; ============================================================
; Kiosk Monitoring Solution - Server Installer
; Inno Setup Script
; ============================================================
; Build: Open this file in Inno Setup Compiler and click Build
; Output: Installer\Output\KioskMonitor-ServerSetup.exe
; ============================================================

#define MyAppName "Kiosk Monitoring Server"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Kiosk Monitoring Solution"
#define MyAppURL "https://github.com/KioskMonitoringSolution"

; Path to solution root (adjust if needed)
#define SolutionRoot "..\.."

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName=C:\KioskMonitor
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\Output
OutputBaseFilename=KioskMonitor-ServerSetup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\icon.ico
SetupLogging=yes

; Require minimum Windows Server 2019 or Windows 10
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ============================================================
; CUSTOM WIZARD PAGES
; ============================================================

[Messages]
WelcomeLabel2=This will install the Kiosk Monitoring Server including:%n%n- Mosquitto MQTT Broker%n- OpenSSH SFTP Server%n- MonitoringBackend API (IIS)%n- BranchMonitorFrontEnd (IIS)%n- VNC Proxy API (IIS)%n- Database setup%n- Firewall rules%n%nClick Next to continue.

; ============================================================
; DIRECTORIES
; ============================================================

[Dirs]
; Main directories
Name: "{app}"
Name: "{app}\API"
Name: "{app}\Frontend"
Name: "{app}\VNCProxy"
Name: "{app}\Config"
Name: "{app}\Scripts"
Name: "{app}\Tools"

; MQTT directories
Name: "C:\Mosquitto"
Name: "C:\Mosquitto\config"
Name: "C:\Mosquitto\data"
Name: "C:\Mosquitto\log"
Name: "C:\Mosquitto\certs"
Name: "C:\Mosquitto\passwords"

; SFTP directories
Name: "C:\SFTP"
Name: "C:\SFTP\Patches"
Name: "C:\SFTP\Terminals"
Name: "C:\SFTP\Logs"

; Monitoring directories
Name: "C:\Monitoring"
Name: "C:\Monitoring\Terminals"
Name: "C:\Monitoring\Logs"
Name: "C:\Monitoring\Logs\API"
Name: "C:\Monitoring\Logs\HealthCheck"
Name: "C:\Monitoring\Backups"
Name: "C:\Monitoring\Backups\Database"
Name: "C:\Monitoring\Certs"
Name: "C:\Monitoring\Scripts"

; IIS directories
Name: "C:\inetpub\wwwroot\MonitoringAPI"
Name: "C:\inetpub\wwwroot\MonitoringUI"
Name: "C:\inetpub\wwwroot\VNCProxy"
Name: "C:\inetpub\logs\MonitoringAPI"
Name: "C:\inetpub\logs\MonitoringUI"
Name: "C:\inetpub\logs\VNCProxy"

; ============================================================
; FILES TO INSTALL
; ============================================================

[Files]
; ---- DEPENDENCY INSTALLERS ----
; Mosquitto installer (place in Installer\Server\Dependencies\)
Source: "Dependencies\mosquitto-*-install-windows-x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: not IsMosquittoInstalled
; .NET 8.0 Hosting Bundle (place in Installer\Server\Dependencies\)
Source: "Dependencies\dotnet-hosting-*-win.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: not IsDotNetInstalled
; URL Rewrite Module (place in Installer\Server\Dependencies\)
Source: "Dependencies\rewrite_amd64*.msi"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall

; ---- PUBLISHED API ----
Source: "Published\API\*"; DestDir: "C:\inetpub\wwwroot\MonitoringAPI"; Flags: ignoreversion recursesubdirs createallsubdirs

; ---- PUBLISHED FRONTEND ----
Source: "Published\Frontend\*"; DestDir: "C:\inetpub\wwwroot\MonitoringUI"; Flags: ignoreversion recursesubdirs createallsubdirs

; ---- PUBLISHED VNC PROXY ----
Source: "Published\VNCProxy\*"; DestDir: "C:\inetpub\wwwroot\VNCProxy"; Flags: ignoreversion recursesubdirs createallsubdirs

; ---- CONFIGURATION TEMPLATES ----
Source: "Config\mosquitto.conf"; DestDir: "{app}\Config"; Flags: ignoreversion
Source: "Config\acl.conf"; DestDir: "{app}\Config"; Flags: ignoreversion
Source: "Config\sshd_config"; DestDir: "{app}\Config"; Flags: ignoreversion
Source: "Config\api-web.config"; DestDir: "{app}\Config"; Flags: ignoreversion
Source: "Config\ui-web.config"; DestDir: "{app}\Config"; Flags: ignoreversion
Source: "Config\vnc-web.config"; DestDir: "{app}\Config"; Flags: ignoreversion

; ---- SCRIPTS ----
Source: "Scripts\*"; DestDir: "{app}\Scripts"; Flags: ignoreversion recursesubdirs

; ============================================================
; REGISTRY
; ============================================================

[Registry]
Root: HKLM; Subkey: "SOFTWARE\KioskMonitor"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"
Root: HKLM; Subkey: "SOFTWARE\KioskMonitor"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"
Root: HKLM; Subkey: "SOFTWARE\KioskMonitor"; ValueType: string; ValueName: "ServerIP"; ValueData: "{code:GetServerIP}"
Root: HKLM; Subkey: "SOFTWARE\KioskMonitor"; ValueType: string; ValueName: "ApiPort"; ValueData: "{code:GetApiPort}"
Root: HKLM; Subkey: "SOFTWARE\KioskMonitor"; ValueType: string; ValueName: "MqttPort"; ValueData: "{code:GetMqttPort}"

; ============================================================
; ICONS (Start Menu)
; ============================================================

[Icons]
Name: "{group}\Server Verification"; Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\Verify-Server.ps1"""; WorkingDir: "{app}\Scripts"
Name: "{group}\Open Monitoring Dashboard"; Filename: "http://localhost:5235"
Name: "{group}\Open API Swagger"; Filename: "http://localhost:5155/swagger"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; ============================================================
; RUN AFTER INSTALL
; ============================================================

[Run]
; Install Mosquitto (silent)
Filename: "{tmp}\mosquitto-2.0.18-install-windows-x64.exe"; Parameters: "/S"; StatusMsg: "Installing Mosquitto MQTT Broker..."; Flags: waituntilterminated; Check: not IsMosquittoInstalled

; Install .NET Hosting Bundle (silent)
Filename: "{tmp}\dotnet-hosting-8.0.11-win.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installing .NET 8.0 Hosting Bundle..."; Flags: waituntilterminated; Check: not IsDotNetInstalled

; Install URL Rewrite (silent)
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\rewrite_amd64_en-US.msi"" /quiet /norestart"; StatusMsg: "Installing URL Rewrite Module..."; Flags: waituntilterminated

; Install OpenSSH
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0"""; StatusMsg: "Installing OpenSSH Server..."; Flags: waituntilterminated runhidden

; Install IIS Features
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\Install-IIS-Features.ps1"""; StatusMsg: "Installing IIS Features..."; Flags: waituntilterminated runhidden

; Configure everything
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\Configure-Server.ps1"" -ServerIP ""{code:GetServerIP}"" -ApiPort ""{code:GetApiPort}"" -MqttPort ""{code:GetMqttPort}"" -MqttUser ""{code:GetMqttUser}"" -MqttPassword ""{code:GetMqttPassword}"" -SftpUser ""{code:GetSftpUser}"" -SftpPassword ""{code:GetSftpPassword}"" -SqlInstance ""{code:GetSqlInstance}"" -SqlDatabase ""{code:GetSqlDatabase}"" -SqlUser ""{code:GetSqlUser}"" -SqlPassword ""{code:GetSqlPassword}"""; StatusMsg: "Configuring services..."; Flags: waituntilterminated runhidden

; IIS Reset
Filename: "iisreset.exe"; Parameters: "/restart"; StatusMsg: "Restarting IIS..."; Flags: waituntilterminated runhidden

; Open dashboard after install
Filename: "http://localhost:5235"; Flags: shellexec postinstall unchecked; Description: "Open Monitoring Dashboard"

; Run verification
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\Verify-Server.ps1"""; Flags: postinstall shellexec; Description: "Run Server Verification"

; ============================================================
; UNINSTALL
; ============================================================

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Stop-Service mosquitto -ErrorAction SilentlyContinue; Stop-Service sshd -ErrorAction SilentlyContinue"""; Flags: waituntilterminated runhidden
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Remove-Website -Name MonitoringAPI -ErrorAction SilentlyContinue; Remove-Website -Name MonitoringUI -ErrorAction SilentlyContinue; Remove-Website -Name VNCProxy -ErrorAction SilentlyContinue"""; Flags: waituntilterminated runhidden

[UninstallDelete]
Type: filesandordirs; Name: "C:\Monitoring\Logs"

; ============================================================
; PASCAL SCRIPT (Custom wizard pages + logic)
; ============================================================

[Code]
var
  ServerConfigPage: TInputQueryWizardPage;
  SqlConfigPage: TInputQueryWizardPage;
  MqttConfigPage: TInputQueryWizardPage;
  SftpConfigPage: TInputQueryWizardPage;

// ---- CUSTOM WIZARD PAGES ----

procedure InitializeWizard();
begin
  // Page 1: Server Configuration
  ServerConfigPage := CreateInputQueryPage(wpSelectDir,
    'Server Configuration',
    'Configure the server network settings.',
    'Enter the server IP address and ports:');
  ServerConfigPage.Add('Server IP Address:', False);
  ServerConfigPage.Add('API Port:', False);
  ServerConfigPage.Add('Frontend Port:', False);
  ServerConfigPage.Add('VNC Proxy Port:', False);
  ServerConfigPage.Values[0] := '192.168.1.24';
  ServerConfigPage.Values[1] := '5155';
  ServerConfigPage.Values[2] := '5235';
  ServerConfigPage.Values[3] := '7128';

  // Page 2: SQL Server Configuration
  SqlConfigPage := CreateInputQueryPage(ServerConfigPage.ID,
    'Database Configuration',
    'Configure SQL Server connection.',
    'Enter SQL Server details:');
  SqlConfigPage.Add('SQL Server Instance:', False);
  SqlConfigPage.Add('Database Name:', False);
  SqlConfigPage.Add('Application Username:', False);
  SqlConfigPage.Add('Application Password:', True);
  SqlConfigPage.Values[0] := 'localhost';
  SqlConfigPage.Values[1] := 'MonitoringKiosk';
  SqlConfigPage.Values[2] := 'MonitoringAppUser';
  SqlConfigPage.Values[3] := 'StrongP@ssw0rd!2026';

  // Page 3: MQTT Configuration
  MqttConfigPage := CreateInputQueryPage(SqlConfigPage.ID,
    'MQTT Broker Configuration',
    'Configure MQTT authentication.',
    'Enter MQTT broker credentials:');
  MqttConfigPage.Add('MQTT Port (TLS):', False);
  MqttConfigPage.Add('MQTT Port (Plain/Internal):', False);
  MqttConfigPage.Add('Backend Username:', False);
  MqttConfigPage.Add('Backend Password:', True);
  MqttConfigPage.Values[0] := '8883';
  MqttConfigPage.Values[1] := '1883';
  MqttConfigPage.Values[2] := 'MonitoringBackend';
  MqttConfigPage.Values[3] := 'Mqtt@Secure2026!';

  // Page 4: SFTP Configuration
  SftpConfigPage := CreateInputQueryPage(MqttConfigPage.ID,
    'SFTP Server Configuration',
    'Configure SFTP user for branch file transfers.',
    'Enter SFTP credentials:');
  SftpConfigPage.Add('SFTP Username:', False);
  SftpConfigPage.Add('SFTP Password:', True);
  SftpConfigPage.Add('SFTP Root Path:', False);
  SftpConfigPage.Values[0] := 'sftpuser';
  SftpConfigPage.Values[1] := 'Sftp@Secure2026!';
  SftpConfigPage.Values[2] := 'C:\SFTP';
end;

// ---- GETTER FUNCTIONS ----

function GetServerIP(Param: String): String;
begin
  Result := ServerConfigPage.Values[0];
end;

function GetApiPort(Param: String): String;
begin
  Result := ServerConfigPage.Values[1];
end;

function GetFrontendPort(Param: String): String;
begin
  Result := ServerConfigPage.Values[2];
end;

function GetVncPort(Param: String): String;
begin
  Result := ServerConfigPage.Values[3];
end;

function GetSqlInstance(Param: String): String;
begin
  Result := SqlConfigPage.Values[0];
end;

function GetSqlDatabase(Param: String): String;
begin
  Result := SqlConfigPage.Values[1];
end;

function GetSqlUser(Param: String): String;
begin
  Result := SqlConfigPage.Values[2];
end;

function GetSqlPassword(Param: String): String;
begin
  Result := SqlConfigPage.Values[3];
end;

function GetMqttPort(Param: String): String;
begin
  Result := MqttConfigPage.Values[0];
end;

function GetMqttPlainPort(Param: String): String;
begin
  Result := MqttConfigPage.Values[1];
end;

function GetMqttUser(Param: String): String;
begin
  Result := MqttConfigPage.Values[2];
end;

function GetMqttPassword(Param: String): String;
begin
  Result := MqttConfigPage.Values[3];
end;

function GetSftpUser(Param: String): String;
begin
  Result := SftpConfigPage.Values[0];
end;

function GetSftpPassword(Param: String): String;
begin
  Result := SftpConfigPage.Values[1];
end;

// ---- DEPENDENCY CHECKS ----

function IsMosquittoInstalled(): Boolean;
begin
  Result := FileExists('C:\Program Files\mosquitto\mosquitto.exe');
end;

function IsDotNetInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\inetsrv\aspnetcorev2.dll'));
end;

// ---- VALIDATION ----

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = ServerConfigPage.ID then
  begin
    if ServerConfigPage.Values[0] = '' then
    begin
      MsgBox('Server IP Address is required.', mbError, MB_OK);
      Result := False;
    end;
  end;

  if CurPageID = SqlConfigPage.ID then
  begin
    if SqlConfigPage.Values[3] = '' then
    begin
      MsgBox('SQL Password is required.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
