# Enable error handling
$ErrorActionPreference = "Stop"
# Allow PowerShell script execution for this session
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

# Log file for debugging
$LOG_FILE = Join-Path $PSScriptRoot 'script_debug.log'
"Script started at $(Get-Date)" | Out-File -FilePath $LOG_FILE -Encoding utf8

# Function to log messages
function Log {
    param ([string]$message)
    "$(Get-Date): $message" | Out-File -FilePath $LOG_FILE -Append -Encoding utf8
}

# Publish NetworkMonitor for win-x64
Log "Publishing NetworkMonitor for win-x64"
# Use project path relative to script so publish runs even when invoked from another cwd
$projectPath = Join-Path -Path $PSScriptRoot -ChildPath 'NetworkMonitor-Maui.csproj'
if (!(Test-Path $projectPath)) {
    Log "ERROR: Project file not found at $projectPath"
    exit 1
}

# Run dotnet publish and stop if it fails to avoid copying stale files
Log "Running: dotnet publish \"$projectPath\" -c Release -r win-x64 --self-contained true"
$publishOutput = & dotnet publish "$projectPath" -c Release -r win-x64 --self-contained true 2>&1
$publishOutput | Out-File -Append $LOG_FILE
if ($LASTEXITCODE -ne 0) {
    Log "ERROR: dotnet publish failed with exit code $LASTEXITCODE"
    Log "Publish output: $publishOutput"
    exit 1
}

Log "Publish completed with output: $publishOutput"

# Resolve the active publish output so the script keeps working if the project framework changes
$projectXml = [xml](Get-Content -Path $projectPath -Raw)
$targetFramework = $projectXml.Project.PropertyGroup.TargetFramework |
    Where-Object { $_ -and $_.Trim() } |
    Select-Object -First 1

if (-not $targetFramework) {
    $targetFramework = ($projectXml.Project.PropertyGroup | Where-Object { $_.TargetFramework -and $_.TargetFramework.Trim() } | Select-Object -First 1).TargetFramework
}

if (-not $targetFramework) {
    Log "ERROR: Could not determine TargetFramework from $projectPath"
    exit 1
}

$windowsSourcePath = Join-Path $PSScriptRoot "bin\Release\$targetFramework\win-x64\publish"
if (!(Test-Path $windowsSourcePath)) {
    $fallback = Get-ChildItem -Path (Join-Path $PSScriptRoot 'bin\Release') -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\win-x64\\publish$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($fallback) {
        $windowsSourcePath = $fallback.FullName
    }
}

if (!(Test-Path $windowsSourcePath)) {
    Log "ERROR: Source path '$windowsSourcePath' does not exist!"
    exit 1
}

Log "Using publish source path: $windowsSourcePath"

# Files to copy
$filesToCopy = @( "NetworkMonitor.dll","System*.dll", "PuppeteerSharp.dll", "RestSharp.dll", "Nito*.dll", "Nanoid.dll", "mscorlib.dll", "netstandard.dll", "HtmlAgilityPack.dll", "FluentFTP.dll", "Betalgo.Ranul.OpenAI.dll", "BouncyCastle.Cryptography.dll", "Microsoft.Extensions*.dll", "Microsoft.IdentityModel*.dll", "Microsoft.Bcl.AsyncInterfaces.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CSharp.dll", "Markdig.dll", "MessagePack*.dll")

# Windows DLL destination
$workspaceRoot = Split-Path -Path $PSScriptRoot -Parent
$windowsDestination = Join-Path $workspaceRoot 'NetworkMonitorAgent\Resources\Raw\windowsdlls'

# Copy Windows DLLs directly to the app's resource folder
$dest = $windowsDestination

if (!(Test-Path $dest)) {
    Log "WARNING: Destination path '$dest' does not exist. Creating it."
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
}

Log "Copying files to $dest"
foreach ($file in $filesToCopy) {
    $matches = Get-ChildItem -Path $windowsSourcePath -Filter $file -File -ErrorAction SilentlyContinue
    if ($matches) {
        foreach ($match in $matches) {
            Copy-Item -Path $match.FullName -Destination $dest -Force -ErrorAction Stop
            Log "Copied: $($match.Name) to $dest"
        }
    } else {
        Log "WARNING: File pattern not found: $file"
    }
}

$targetFile = "System.IO.Compression.Native.dll"
$filePath = Join-Path -Path $dest -ChildPath $targetFile
if (Test-Path $filePath) {
    Remove-Item -Path $filePath -Force -ErrorAction Stop
    Log "Deleted: $filePath"
}

# Run the manifest generator so the manifest describes the copied DLLs
$manifestScript = Join-Path -Path $dest -ChildPath 'create-manifest-windows.ps1'
if (!(Test-Path $manifestScript)) {
    Log "ERROR: Manifest script not found: $manifestScript"
    exit 1
}

Log "Running create-manifest for $dest using $manifestScript"
Push-Location $dest
$manifestOutput = powershell -ExecutionPolicy Bypass -File $manifestScript 2>&1
$manifestExitCode = $LASTEXITCODE
Pop-Location
$manifestOutput | Out-File -Append $LOG_FILE
if ($manifestExitCode -ne 0) {
    Log "ERROR: Manifest generation failed for '$dest' with exit code $manifestExitCode"
    exit 1
}
Log "Manifest generated for $dest"

Log "Script completed successfully"
"Script completed successfully. Check $LOG_FILE for details." | Out-File -Append $LOG_FILE
