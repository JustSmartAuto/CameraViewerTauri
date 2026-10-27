<#
.SYNOPSIS
    Builds the one-click installer NomacsSvgOverlayPlugin-Setup.exe (.NET 4.8)
    with the plugin DLL embedded. Run after build-zig.ps1.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$PluginDll = ""
)

$ErrorActionPreference = "Stop"
if (-not $PluginDll) { $PluginDll = "$PSScriptRoot\build\SvgOverlayPlugin.dll" }

if (-not (Test-Path $PluginDll)) {
    throw "Plugin DLL not found: $PluginDll - run build-zig.ps1 first"
}

# locate MSBuild via vswhere
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere not found - Visual Studio required to compile the installer" }
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found" }
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

# embed the (possibly freshly built) plugin dll
Copy-Item $PluginDll "$PSScriptRoot\installer\SvgOverlayPlugin.dll" -Force

& $msbuild "$PSScriptRoot\installer\installer.csproj" /p:Configuration=Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed" }

$exe = "$PSScriptRoot\installer\bin\Release\NomacsSvgOverlayPlugin-Setup.exe"
Copy-Item $exe "$PSScriptRoot\NomacsSvgOverlayPlugin-Setup.exe" -Force
Write-Host "installer: $PSScriptRoot\NomacsSvgOverlayPlugin-Setup.exe" -ForegroundColor Green
Write-Host "Double-click this exe on the target machine to install the plugin."
