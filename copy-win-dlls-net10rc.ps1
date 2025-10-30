# Enable error handling
$ErrorActionPreference = "Stop"
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

# Logging
$LOG_FILE = "script_debug.log"
"Script started at $(Get-Date)" | Out-File -FilePath $LOG_FILE -Encoding utf8
function Log { param([string]$m) "$(Get-Date): $m" | Out-File -FilePath $LOG_FILE -Append -Encoding utf8 }

# ---- Source (from VS Debug build) ----
$configuration = "Release"
$tfm = "net10.0-windows10.0.19041.0"
$windowsSourcePath = "C:\code\NetworkMonitorLib\bin\$configuration\$tfm\win-x64"

if (!(Test-Path $windowsSourcePath)) {
    Log "ERROR: Source path '$windowsSourcePath' does not exist."
    exit 1
}
Log "Source path: $windowsSourcePath"

# ---- Files to copy ----
$filesToCopy = @(
    "NetworkMonitor.dll",
    "System*.dll",
    "PuppeteerSharp.dll",
    "RestSharp.dll",
    "Nito*.dll",
    "Nanoid.dll",
    "mscorlib.dll",
    "netstandard.dll",
    "HtmlAgilityPack.dll",
    "FluentFTP.dll",
    "Betalgo.Ranul.OpenAI.dll",
    "BouncyCastle.Cryptography.dll",
    "Microsoft.Extensions*.dll",
    "Microsoft.IdentityModel*.dll",
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "Microsoft.CodeAnalysis.CSharp.dll",
    "Microsoft.CodeAnalysis.dll",
    "Microsoft.CSharp.dll",
    "Markdig.dll"
)

# ---- Destinations ----
$windowsDestinations = @(
    "C:\code\NetworkMonitorQuantumSecure\Resources\Raw\windowsdlls",
    "C:\code\FreeNetworkMonitorAgent\Resources\Raw\windowsdlls"
)

# ---- Copy files ----
foreach ($dest in $windowsDestinations) {
    if (!(Test-Path $dest)) {
        Log "Destination '$dest' does not exist. Creating."
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }

    Log "Copying files to $dest"
    foreach ($pattern in $filesToCopy) {
        $patternPath = Join-Path -Path $windowsSourcePath -ChildPath $pattern
        $matched = Get-Item -Path $patternPath -ErrorAction SilentlyContinue

        if ($matched) {
            Copy-Item -Path $patternPath -Destination $dest -Force -ErrorAction Stop
            Log "Copied: $pattern to $dest"
        } else {
            Log "WARNING: No match for: $patternPath"
        }
    }

    # Remove unwanted native file if present
    $targetFile = Join-Path -Path $dest -ChildPath "System.IO.Compression.Native.dll"
    if (Test-Path $targetFile) {
        Remove-Item -Path $targetFile -Force -ErrorAction Stop
        Log "Deleted: $targetFile"
    } else {
        Log "INFO: Not found (skip delete): $targetFile"
    }
}

# ---- Run create-manifest scripts ----
$manifestScripts = @(
    "C:\code\NetworkMonitorQuantumSecure\Resources\Raw\windowsdlls\create-manifest-windows.ps1",
    "C:\code\FreeNetworkMonitorAgent\Resources\Raw\windowsdlls\create-manifest-windows.ps1"
)

foreach ($script in $manifestScripts) {
    if (!(Test-Path $script)) {
        Log "ERROR: Manifest script not found: $script"
        continue
    }

    Log "Running create-manifest: $script"
    $manifestOutput = powershell -ExecutionPolicy Bypass -File $script 2>&1
    $manifestOutput | Out-File -Append $LOG_FILE
    Log "Manifest script completed"
}

Log "Script completed successfully"
"Script completed successfully. Check $LOG_FILE for details." | Out-File -Append $LOG_FILE
