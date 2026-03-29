# Install the DaisiGit CLI (dg) on Windows
# Usage: irm https://git.daisi.ai/cli/install.ps1 | iex
$ErrorActionPreference = "Stop"

$BaseUrl = if ($env:DG_BASE_URL) { $env:DG_BASE_URL } else { "https://git.daisi.ai" }
$InstallDir = if ($env:DG_INSTALL_DIR) { $env:DG_INSTALL_DIR } else { "$env:LOCALAPPDATA\DaisiGit" }

$Binary = "dg-win-x64.exe"
$Url = "$BaseUrl/cli/download/$Binary"

Write-Host "Downloading dg for Windows x64..."

# Create install directory
if (!(Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$DestPath = Join-Path $InstallDir "dg.exe"
Invoke-WebRequest -Uri $Url -OutFile $DestPath -UseBasicParsing

# Add to PATH if not already there
$UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($UserPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$UserPath;$InstallDir", "User")
    $env:Path = "$env:Path;$InstallDir"
    Write-Host "Added $InstallDir to PATH"
}

Write-Host ""
Write-Host "dg installed to $DestPath"
Write-Host ""
Write-Host "Get started:"
Write-Host "  dg auth login --server $BaseUrl"
Write-Host ""
Write-Host "Note: restart your terminal for PATH changes to take effect."
