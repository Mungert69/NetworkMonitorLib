# Enable error handling
$ErrorActionPreference = "Stop"
# Allow PowerShell script execution for this session
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

# Log file for debugging
$LOG_FILE = "script_debug.log"
"Script started at $(Get-Date)" | Out-File -FilePath $LOG_FILE -Encoding utf8

# Function to log messages
function Log {
    param ([string]$message)
    "$(Get-Date): $message" | Out-File -FilePath $LOG_FILE -Append -Encoding utf8
}

# Publish NetworkMonitor for win-x64
Log "Publishing NetworkMonitor for win-x64"
$publishOutput = dotnet publish NetworkMonitor.csproj -c Release -r win-x64 --self-contained true 2>&1
$publishOutput | Out-File -Append $LOG_FILE
Log "Publish completed with output: $publishOutput"

# Define source path
$windowsSourcePath = "C:\co..\NetworkMonitorLib\bin\Release\net9.0\win-x64\publish"
if (!(Test-Path $windowsSourcePath)) {
    Log "ERROR: Source path '$windowsSourcePath' does not exist!"
    exit 1
}

# Files to copy
$filesToCopy = @( "System*.dll", "PuppeteerSharp.dll", "RestSharp.dll", "Nito*.dll", "Nanoid.dll", "mscorlib.dll", "netstandard.dll", "HtmlAgilityPack.dll", "FluentFTP.dll", "Betalgo.Ranul.OpenAI.dll", "BouncyCastle.Cryptography.dll", "Microsoft.Extensions*.dll", "Microsoft.IdentityModel*.dll", "Microsoft.Bcl.AsyncInterfaces.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CSharp.dll", "Markdig.dll")

# Windows DLL final destinations
$windowsDestinations = @(
    "C:\code\NetworkMonitorQuantumSecure\Resources\Raw\windowsdlls",
    "C:\code\FreeNetworkMonitorAgent\Resources\Raw\windowsdlls"
)

# Copy Windows DLLs directly to their final destinations
foreach ($dest in $windowsDestinations) {
    if (!(Test-Path $dest)) {
        Log "WARNING: Destination path '$dest' does not exist. Creating it."
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }

    Log "Copying files to $dest"
    foreach ($file in $filesToCopy) {
        $filePath = Join-Path -Path $windowsSourcePath -ChildPath $file
        $files = Get-Item -Path $filePath -ErrorAction SilentlyContinue

        if ($files) {
            Copy-Item -Path $filePath -Destination $dest -Force -ErrorAction Stop
            Log "Copied: $file to $dest"
        } else {
            Log "WARNING: File not found: $filePath"
        }
    }
   # Path to the file to be deleted
$targetFile = "System.IO.Compression.Native.dll"

foreach ($dest in $windowsDestinations) {
    $filePath = Join-Path -Path $dest -ChildPath $targetFile
    
    if (Test-Path $filePath) {
        Remove-Item -Path $filePath -Force -ErrorAction Stop
        Log "Deleted: $filePath"
    } else {
        Log "WARNING: File not found: $filePath"
    }
}
}

# Run create-manifest scripts
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
    Log "Manifest script output: $manifestOutput"
}

Log "Script completed successfully"
"Script completed successfully. Check $LOG_FILE for details." | Out-File -Append $LOG_FILE
