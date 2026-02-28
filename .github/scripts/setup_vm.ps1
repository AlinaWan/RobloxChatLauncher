<#
.SYNOPSIS
All-in-one installer for Roblox Chat Launcher
.DESCRIPTION
Installs .NET SDK or Desktop Runtime, downloads RobloxChatLauncher source (branch/tag/commit) or first release exe.
#>

param (
    [ValidateSet("Runtime","SDK")]
    [string]$Mode = "Runtime",

    [string]$Version = "10.0",     # .NET channel
    [string]$Branch = "main",      # Branch, Tag, Commit Hash, or Release Tag if -UseReleaseExe
    [switch]$UseReleaseExe         # If true, download first release exe instead of source zip
)

$ErrorActionPreference = "Stop"

## 1️⃣ Prepare Directories
$dlPath = "C:\Downloads"
$dotnetPath = "C:\dotnet"

Write-Host "📁 Preparing directories..." -ForegroundColor Cyan
if (!(Test-Path $dlPath)) { New-Item -ItemType Directory -Path $dlPath -Force }
if (!(Test-Path $dotnetPath)) { New-Item -ItemType Directory -Path $dotnetPath -Force }

## 2️⃣ Install .NET
Write-Host "📦 Installing .NET $Version ($Mode)..." -ForegroundColor Cyan
$installScript = Join-Path $dotnetPath "dotnet-install.ps1"
Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript

if ($Mode -eq "SDK") {
    & powershell -ExecutionPolicy Bypass -File $installScript -Channel $Version
} else {
    & powershell -ExecutionPolicy Bypass -File $installScript -Runtime windowsdesktop -Channel $Version
}

# Ensure dotnet is in the PATH for this session
$env:PATH += ";$dotnetPath"

Write-Host "✅ .NET installation complete" -ForegroundColor Green
dotnet --info

## 3️⃣ Download RobloxChatLauncher
if ($UseReleaseExe) {
    Write-Host "📥 Downloading release installer (.exe)..." -ForegroundColor Cyan
    $releaseApi = "https://api.github.com/repos/AlinaWan/RobloxChatLauncher/releases"
    $releases = Invoke-RestMethod -Uri $releaseApi
    if ($releases.Count -eq 0) { throw "No releases found!" }

    # Use Branch as release tag if specified
    if ($Branch -and $Branch -ne "main") {
        $release = $releases | Where-Object { $_.tag_name -eq $Branch } | Select-Object -First 1
        if (!$release) { throw "Release tag '$Branch' not found!" }
    } else {
        $release = $releases[0]  # first release by default
    }

    # Find first .exe asset
    $exeAsset = $release.assets | Where-Object { $_.name -match "\.exe$" } | Select-Object -First 1
    if (!$exeAsset) { throw "No .exe found in release '$($release.tag_name)'" }

    $outFile = Join-Path $dlPath $exeAsset.name
    Invoke-WebRequest -Uri $exeAsset.browser_download_url -OutFile $outFile
    Write-Host "✅ Release downloaded to $outFile" -ForegroundColor Green
} else {
    Write-Host "📥 Downloading source from ref: $Branch..." -ForegroundColor Cyan
    # Determine URL type
    if ($Branch -match '^\d{7,}$' -or $Branch -match '^[a-f0-9]{7,40}$') {
        $url = "https://github.com/AlinaWan/RobloxChatLauncher/archive/$Branch.zip"
    } elseif ($Branch -match '^v') {
        $url = "https://github.com/AlinaWan/RobloxChatLauncher/archive/refs/tags/$Branch.zip"
    } else {
        $url = "https://github.com/AlinaWan/RobloxChatLauncher/archive/refs/heads/$Branch.zip"
    }
    $zipFile = Join-Path $dlPath "RobloxChatLauncher.zip"
    Invoke-WebRequest -Uri $url -OutFile $zipFile
    $destDir = Join-Path $dlPath "RobloxChatLauncher-$($Branch -replace '/','-')"
    Expand-Archive -Path $zipFile -DestinationPath $destDir -Force
    Write-Host "✅ Source extracted to $destDir" -ForegroundColor Green
}

Write-Host "🎀 Roblox Chat Launcher setup complete!" -ForegroundColor Green