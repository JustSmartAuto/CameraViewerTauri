<#
.SYNOPSIS
    Builds SvgOverlayPlugin.dll using zig c++ (MSVC ABI) – no Visual Studio
    project or CMake required.

.DESCRIPTION
    - Runs Qt moc on the plugin header (Q_OBJECT / Q_PLUGIN_METADATA).
    - Compiles and links with `uvx --from ziglang python -m ziglang c++`
      targeting x86_64-windows-msvc so the DLL is ABI-compatible with the
      MSVC build of nomacs and its bundled Qt6 DLLs.
    - Requires Qt 6.x MSVC (installed e.g. via aqtinstall, see README)
      and the nomacs plugin headers in third_party/nomacs.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build-zig.ps1
    .\build-zig.ps1 -Install          # build + copy into nomacs plugins dir
#>
[CmdletBinding()]
param(
    [string]$QtDir    = "D:\Qt\6.9.3\msvc2022_64",
    [string]$NomacsInclude = "",
    [string]$NomacsDir = "",       # nomacs installation (for nomacsCore.dll); auto-detected if omitted
    [string]$OutDir   = "",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

# resolve defaults here – $PSScriptRoot is unreliable inside param() on PS 5.1
if (-not $NomacsInclude) { $NomacsInclude = "$PSScriptRoot\third_party\nomacs\ImageLounge\src" }
if (-not $OutDir)        { $OutDir = "$PSScriptRoot\build" }

# zig (installed through uv) --------------------------------------------------
function Invoke-Zig {
    # `zig` is provided by the `ziglang` PyPI package via uvx
    & uvx --from ziglang python -m ziglang @args
    if ($LASTEXITCODE -ne 0) { throw "zig failed ($LASTEXITCODE)" }
}

# sanity checks ----------------------------------------------------------------
if (-not (Test-Path "$QtDir\bin\moc.exe"))  { throw "moc.exe not found under $QtDir – install Qt first (see README)" }
if (-not (Test-Path "$QtDir\lib\Qt6Widgets.lib")) { throw "Qt6Widgets.lib not found under $QtDir\lib" }
if (-not (Test-Path "$NomacsInclude\DkCore\DkPluginInterface.h")) { throw "nomacs headers missing – see README (third_party)" }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# nomacsCore import library ----------------------------------------------------
# The plugin calls DkViewPort::imageContainer() / DkImageContainer::filePath()
# from nomacsCore.dll. Windows linking needs an import library, which nomacs
# does not ship – so we generate one from the installed DLL with MSVC tools.
function Find-NomacsCoreDll {
    param([string]$NomacsDir)
    $candidates = @()
    if ($NomacsDir) { $candidates += "$NomacsDir\bin\nomacsCore.dll", "$NomacsDir\nomacsCore.dll" }
    $candidates += "$env:ProgramFiles\nomacs\bin\nomacsCore.dll",
                   "${env:ProgramFiles(x86)}\nomacs\bin\nomacsCore.dll",
                   "$env:LOCALAPPDATA\Programs\nomacs\bin\nomacsCore.dll"
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
    }
    return $null
}

function New-ImportLib {
    param([string]$DllPath, [string]$OutLib)

    # locate dumpbin/lib from the newest MSVC toolset via vswhere
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "vswhere not found – Visual Studio (Build Tools) with C++ is required once, to create the nomacsCore import library" }
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "no Visual Studio with C++ tools found – cannot create the nomacsCore import library" }
    $msvcBin = Get-ChildItem (Join-Path $vsPath "VC\Tools\MSVC\*\bin\Hostx64\x64") -Directory -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending | Select-Object -First 1
    if (-not $msvcBin) { throw "MSVC toolset not found under $vsPath" }
    $dumpbin = Join-Path $msvcBin.FullName "dumpbin.exe"
    $libExe  = Join-Path $msvcBin.FullName "lib.exe"

    Write-Host "generating import library from $DllPath ..."
    $exports = & $dumpbin /exports $DllPath
    $def = New-Object System.Collections.Generic.List[string]
    $def.Add("EXPORTS")
    foreach ($line in $exports) {
        # "    ordinal  hint  RVA       name" – keep exactly 4-column lines only
        if ($line -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)\s*$') {
            $def.Add("    " + $Matches[1])
        }
    }
    if ($def.Count -lt 100) { throw "dumpbin parsing failed – only $($def.Count) symbols found in $DllPath" }

    $defPath = [IO.Path]::ChangeExtension($OutLib, ".def")
    [IO.File]::WriteAllLines($defPath, $def)
    & $libExe /def:"$defPath" /machine:x64 /out:"$OutLib" | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutLib)) { throw "lib.exe failed to create $OutLib" }
    Write-Host "import library: $OutLib ($($def.Count - 1) symbols)" -ForegroundColor DarkGray
}

$nomacsCoreDll = Find-NomacsCoreDll -NomacsDir $NomacsDir
$nomacsCoreLib = Join-Path $OutDir "nomacsCore.lib"

# regenerate when the dll is newer than the lib (nomacs was updated)
if ($nomacsCoreDll -and (Test-Path $nomacsCoreLib) -and
    ((Get-Item $nomacsCoreDll).LastWriteTime -gt (Get-Item $nomacsCoreLib).LastWriteTime)) {
    Remove-Item $nomacsCoreLib -Force
}
if (-not (Test-Path $nomacsCoreLib)) {
    if (-not $nomacsCoreDll) { throw "nomacsCore.dll not found – pass -NomacsDir or install nomacs first" }
    New-ImportLib -DllPath $nomacsCoreDll -OutLib $nomacsCoreLib
}

# moc --------------------------------------------------------------------------
& "$QtDir\bin\moc.exe" "$PSScriptRoot\src\SvgOverlayPlugin.h" `
    -o "$OutDir\moc_SvgOverlayPlugin.cpp" -I"$PSScriptRoot\src" -I"$NomacsInclude\DkCore"
if ($LASTEXITCODE -ne 0) { throw "moc failed" }

# compile + link ---------------------------------------------------------------
$includes = @(
    "$PSScriptRoot\src",
    "$OutDir",
    "$NomacsInclude",
    "$NomacsInclude\DkCore",
    "$NomacsInclude\DkGui",
    "$QtDir\include",
    "$QtDir\include\QtCore",
    "$QtDir\include\QtGui",
    "$QtDir\include\QtWidgets",
    "$QtDir\include\QtSvg"
) | ForEach-Object { "-I`"$_`"" }

$defines = @("WIN32", "_WINDOWS", "UNICODE", "_UNICODE", "QT_NO_DEBUG",
             "QT_CORE_LIB", "QT_GUI_LIB", "QT_WIDGETS_LIB", "QT_SVG_LIB", "NOMINMAX") |
           ForEach-Object { "-D$_" }

$srcs = @(
    "$PSScriptRoot\src\SvgOverlayPlugin.cpp",
    "$OutDir\moc_SvgOverlayPlugin.cpp"
)

$argsZig = @("c++", "-target", "x86_64-windows-msvc", "-O2", "-std=c++17", "-shared",
             "-nostdlib++",   # use MSVC STL headers, don't build zig's libcxxabi
             "-o", "`"$OutDir\SvgOverlayPlugin.dll`"") +
           $defines + $includes + $srcs +
           @("`"$QtDir\lib\Qt6Widgets.lib`"", "`"$QtDir\lib\Qt6Svg.lib`"",
             "`"$QtDir\lib\Qt6Gui.lib`"", "`"$QtDir\lib\Qt6Core.lib`"",
             "`"$nomacsCoreLib`"",
             "-defaultlib:advapi32", "-defaultlib:user32", "-defaultlib:shell32", "-defaultlib:ole32")

Write-Host "zig c++ $($argsZig -join ' ')" -ForegroundColor DarkGray
Invoke-Zig @argsZig

Write-Host "built: $OutDir\SvgOverlayPlugin.dll" -ForegroundColor Green

if ($Install) {
    & "$PSScriptRoot\install.ps1" -PluginPath "$OutDir\SvgOverlayPlugin.dll"
}
