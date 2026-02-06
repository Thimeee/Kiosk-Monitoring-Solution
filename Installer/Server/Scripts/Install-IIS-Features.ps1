# Install IIS Features - Called by Installer
$ErrorActionPreference = "SilentlyContinue"

$features = @(
    "IIS-WebServerRole", "IIS-WebServer", "IIS-CommonHttpFeatures",
    "IIS-HttpErrors", "IIS-HttpRedirect", "IIS-ApplicationDevelopment",
    "IIS-NetFxExtensibility45", "IIS-HealthAndDiagnostics", "IIS-HttpLogging",
    "IIS-Security", "IIS-RequestFiltering", "IIS-Performance",
    "IIS-WebServerManagementTools", "IIS-ManagementConsole",
    "IIS-StaticContent", "IIS-DefaultDocument", "IIS-WebSockets",
    "IIS-ASPNET45", "IIS-ISAPIExtensions", "IIS-ISAPIFilter",
    "IIS-HttpCompressionStatic", "IIS-HttpCompressionDynamic"
)

foreach ($feature in $features) {
    Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart -ErrorAction SilentlyContinue | Out-Null
}

# Enable Application Initialization if available
Install-WindowsFeature Web-AppInit -ErrorAction SilentlyContinue | Out-Null

exit 0
