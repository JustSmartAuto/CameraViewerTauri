<#
.SYNOPSIS
    Installs SvgOverlayPlugin into a system nomacs installation.

.DESCRIPTION
    Finds the nomacs install directory (registry, then common paths),
    copies SvgOverlayPlugin.dll into <nomacs>\plugins\SvgOverlayPlugin\
    and (optionally) enables it in nomacs.ini.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1
    .\install.ps1 -PluginPath .\build\SvgOverlayPlugin\SvgOverlayPlugin.dll
    .\install.ps1 -NomacsDir "C:\Program Files\nomacs" -EnablePlugin
#>
[CmdletBinding()]
param(
    # Path to the compiled SvgOverlayPlugin.dll (searched automatically if omitted)
    [string]$PluginPath = "",

    # nomacs installation directory (auto-detected if omitted)
    [string]$NomacsDir = "",

    # Also enable the plugin in nomacs.ini
    [switch]$EnablePlugin
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- helpers
function Find-NomacsDir {
    # 1) registry (uninstall keys, both views)
    $regPaths = @(
        "HKLM:SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($rp in $regPaths) {
        $hit = Get-ItemProperty $rp -ErrorAction SilentlyContinue |
               Where-Object { $_.DisplayName -like "nomacs*" } |
               Select-Object -First 1
        if ($hit) {
            $dir = $hit.InstallLocation
            if (-not $dir) { $dir = Split-Path $hit.UninstallString -Parent }
            if ($dir) { $dir = $dir.Trim('"') }
            # accept the install root (nomacs.exe inside bin\)
            if ($dir -and ((Test-Path "$dir\nomacs.exe") -or (Test-Path "$dir\bin\nomacs.exe"))) { return $dir }
        }
    }

    # 2) common install locations
    $candidates = @(
        "$env:ProgramFiles\nomacs",
        "${env:ProgramFiles(x86)}\nomacs",
        "$env:LOCALAPPDATA\Programs\nomacs",
        "$env:ProgramFiles\nomacs 3.19",
        "${env:ProgramFiles(x86)}\nomacs 3.19"
    )
    foreach ($c in $candidates) {
        if ((Test-Path "$c\nomacs.exe") -or (Test-Path "$c\bin\nomacs.exe")) { return $c }
    }
    return $null
}

function Find-PluginDll {
    param([string]$Root)
    $candidates = @(
        "$Root\build\SvgOverlayPlugin\SvgOverlayPlugin.dll",
        "$Root\build\SvgOverlayPlugin\Release\SvgOverlayPlugin.dll",
        "$Root\build\Release\SvgOverlayPlugin.dll"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }
    # fallback: newest dll under build
    $dll = Get-ChildItem -Path "$Root\build" -Recurse -Filter "SvgOverlayPlugin.dll" -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($dll) { return $dll.FullName }
    return $null
}

# ---------------------------------------------------------------- locate inputs
if (-not $NomacsDir) {
    $NomacsDir = Find-NomacsDir
}
if (-not $NomacsDir -or -not (Test-Path $NomacsDir)) {
    throw "Could not locate nomacs installation. Re-run with -NomacsDir `"C:\path\to\nomacs`"."
}
Write-Host "nomacs directory : $NomacsDir" -ForegroundColor Cyan

if (-not $PluginPath) {
    $PluginPath = Find-PluginDll -Root $PSScriptRoot
}
if (-not $PluginPath -or -not (Test-Path $PluginPath)) {
    throw "Could not find SvgOverlayPlugin.dll. Build the project first or pass -PluginPath."
}
Write-Host "plugin dll       : $PluginPath" -ForegroundColor Cyan

# ---------------------------------------------------------------- copy
# nomacs loads plugins from <nomacs>\bin\plugins (applicationDirPath + /plugins),
# top-level *.dll only (no subfolders)
$targetDir = Join-Path $NomacsDir "bin\plugins"

if (-not (Test-Path $targetDir)) {
    try {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    } catch {
        # not writable (e.g. Program Files without admin) -> fallback to user install dir
        $fallback = Join-Path $env:LOCALAPPDATA "nomacs\bin\plugins"
        Write-Warning "$targetDir is not writable, falling back to $fallback"
        New-Item -ItemType Directory -Path $fallback -Force | Out-Null
        $targetDir = $fallback
    }
}

# remove copies installed by earlier versions
foreach ($old in @(
    (Join-Path $targetDir "SvgOverlayPlugin\SvgOverlayPlugin.dll"),
    (Join-Path $NomacsDir "plugins\SvgOverlayPlugin.dll")
)) {
    if (Test-Path $old) {
        Write-Host "removing old copy: $old"
        Remove-Item $old -Force
        Remove-Item (Split-Path $old -Parent) -Force -ErrorAction SilentlyContinue
    }
}

Copy-Item -Path $PluginPath -Destination (Join-Path $targetDir "SvgOverlayPlugin.dll") -Force
Write-Host "installed to     : $targetDir" -ForegroundColor Green

# ---------------------------------------------------------------- (optional) enable in nomacs.ini
if ($EnablePlugin) {
    $ini = Join-Path $env:APPDATA "nomacs\nomacs.ini"
    if (Test-Path $ini) {
        $content = Get-Content $ini -Raw
        # nomacs stores plugin activation as: [Plugins] pluginWhitelist=...  / appSettings
        if ($content -match '(?m)^plugins=.*$') {
            Write-Host "nomacs.ini found at $ini – please enable 'SvgOverlayPlugin' via Edit > Settings > Plugins." -ForegroundColor Yellow
        } else {
            Write-Host "nomacs.ini found at $ini – enable the plugin via Edit > Settings > Plugins after starting nomacs." -ForegroundColor Yellow
        }
    } else {
        Write-Host "nomacs.ini not found yet – enable the plugin via Edit > Settings > Plugins after starting nomacs." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Done. Restart nomacs, then enable 'SvgOverlayPlugin' (Edit > Settings > Plugins)" -ForegroundColor Green
Write-Host "and toggle it via the plugin menu: 'Toggle SVG Overlay'." -ForegroundColor Green
