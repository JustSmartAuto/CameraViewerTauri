# build-bundle.ps1 — Portable bundle for Cognex Camera Log Server
#   Single .exe containing launcher + embedded JRE + embedded fat jar
#
# Prerequisites:
#   1. Fat jar already built by build-with-timestamp.ps1
#   2. Zulu JDK 25 installed (for jlink)
#   3. dotnet SDK 8+/9+ (can target net48)
#
# Output: dist\Cognex相机日志服务器_{timestamp}.exe + .config
param(
  [string]$JavaHome,
  [switch]$SkipJlink
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Launcher = Join-Path $PSScriptRoot "Launcher"
$Payload = Join-Path $Launcher "payload"
$Stamp = Get-Date -Format "yyyyMMddHHmmss"
$OutDir = Join-Path $Root "dist"

Write-Host "==> Bundle build" -ForegroundColor Cyan
Write-Host "    root:   $Root"
Write-Host "    stamp:  $Stamp"

New-Item -ItemType Directory -Force -Path $Payload, $OutDir | Out-Null

# ---- 1. Find Gradle fat jar ----
$FatJar = Get-ChildItem "$Root\audit-log-server-*-fat*.jar" |
  Where-Object { $_.Name -notlike "*sources*" -and $_.Name -notlike "*javadoc*" } |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $FatJar) { throw "fat jar not found. Run build-with-timestamp.ps1 first." }
Write-Host "==> fat jar: $($FatJar.FullName)" -ForegroundColor Green
Copy-Item $FatJar.FullName "$Payload\app.jar" -Force

# ---- 2. jlink trimmed JRE ----
# 注意：模块集变化时必须重新 jlink（payload\jre.zip 会被覆盖）。
# 运行时用到的非默认模块：
#   java.net.http  —— 相机 Platform API 拉取（/api/audit-log）
#   jdk.crypto.ec  —— TLS 椭圆曲线算法
#   jdk.unsupported—— sun.misc.Unsafe（Gson/H2 反射优化）
$Modules = "java.desktop,java.logging,java.sql,java.prefs,java.instrument,java.naming," +
           "java.security.jgss,java.net.http,jdk.crypto.ec,jdk.unsupported"

$NeedJlink = (-not $SkipJlink)
if ($NeedJlink) {
  $Jlink = $null
  if ($JavaHome) {
    $Jlink = Join-Path $JavaHome "bin\jlink.exe"
    if (-not (Test-Path $Jlink)) { throw "jlink not found at $Jlink" }
  } else {
    $patterns = @(
      "C:\Program Files\Zulu\zulu-25\bin\jlink.exe",
      "C:\Program Files\Eclipse Adoptium\jdk-25*\bin\jlink.exe",
      "C:\Program Files\Microsoft\jdk-25*\bin\jlink.exe"
    )
    foreach ($pat in $patterns) {
      $found = Get-Item $pat -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($found) { $Jlink = $found.FullName; break }
    }
    if (-not $Jlink) {
      $whereOut = & where.exe jlink 2>$null
      if ($LASTEXITCODE -eq 0) { $Jlink = $whereOut[0] }
    }
  }
  if (-not $Jlink) {
    throw "jlink not found. Install Zulu JDK 25 (https://adoptium.net/)."
  }
  $JdkHome = Split-Path (Split-Path $Jlink)
  Write-Host "==> jlink -> jre.zip" -ForegroundColor Green

  $Tmp = Join-Path $env:TEMP "cognex-jre-$Stamp"
  if (Test-Path $Tmp) { Remove-Item $Tmp -Recurse -Force }

  & $Jlink `
    --module-path "$JdkHome\jmods" `
    --add-modules $Modules `
    --strip-debug --no-man-pages `
    --output "$Tmp"
  if ($LASTEXITCODE -ne 0) { throw "jlink failed" }

  Compress-Archive -Path "$Tmp\*" -DestinationPath "$Payload\jre.zip" -Force
  Remove-Item $Tmp -Recurse -Force
} else {
  Write-Host "==> -SkipJlink：复用 payload\jre.zip" -ForegroundColor Gray
}

# ---- 2b. JRE 版本标记 ----
# 客户端已解压的 JRE 版本与此不一致时，Launcher 会删除旧目录重新解压，
# 避免机器上残留旧模块集（历史 bug：缺 java.net.http 导致相机功能异常）。
$JreVersion = (Get-FileHash "$Payload\jre.zip" -Algorithm SHA256).Hash.Substring(0, 16)
Set-Content "$Payload\jre.version" $JreVersion -Encoding ASCII
Write-Host "==> jre.version: $JreVersion" -ForegroundColor Green

# ---- 3. dotnet build ----
Write-Host "==> dotnet build (net48 Release)" -ForegroundColor Green
Push-Location $Launcher
try {
  dotnet restore
  dotnet build -c Release
} finally {
  Pop-Location
}

# ---- 4. Copy to output dir ----
$AllExe = Get-ChildItem "$Launcher\bin\Release\*.exe" | Where-Object { $_.Name -notmatch "^(dotnet|vshost|Fody)" }
if (-not $AllExe) { throw "launcher exe not built." }
$Exe = $AllExe.FullName
$Cfg = "$Exe.config"
Write-Host "==> found launcher: $Exe" -ForegroundColor Green

$OutName = [System.IO.Path]::GetFileNameWithoutExtension($Exe) + "_$Stamp.exe"
Copy-Item $Exe "$OutDir\$OutName" -Force
Copy-Item $Cfg "$OutDir\${OutName}.config" -Force

$SizeMB = [math]::Round((Get-Item "$OutDir\$OutName").Length / 1MB, 1)
Write-Host ""
Write-Host "==> DONE" -ForegroundColor Cyan
Write-Host "    $OutDir\$OutName" -ForegroundColor White
Write-Host "    Embedded: JRE + app.jar + launcher   $SizeMB MB" -ForegroundColor Gray
Write-Host "    Double-click to run. No external dependencies." -ForegroundColor Gray
