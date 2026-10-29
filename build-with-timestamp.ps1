# build-with-timestamp.ps1 — Windows 版打包脚本
# 用法: .\build-with-timestamp.ps1
# 功能: 执行 gradle fatJar，将产物直接重命名为带时间戳后缀的文件名（移动，不保留基础 jar）

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---- 1. 定位 Gradle ----
$gradleBat = Join-Path $ScriptDir "gradle-dist\gradle-9.7.1\bin\gradle.bat"
if (Test-Path $gradleBat) {
    $gradle = $gradleBat
} else {
    $g = Get-Command gradle -ErrorAction SilentlyContinue
    $gradle = if ($g) { $g.Source } else { $null }
}
if (-not $gradle) { throw "gradle not found (neither in gradle-dist nor PATH)" }

# ---- 2. 构建 ----
Write-Host "Building fat jar..."
& $gradle fatJar --no-daemon
if ($LASTEXITCODE -ne 0) { throw "gradle fatJar failed with exit $LASTEXITCODE" }

# ---- 3. 找最新 fat jar ----
$jarFile = $null
$try1 = Get-ChildItem -Path $ScriptDir -Filter "audit-log-server-*-fat*.jar" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
if ($try1 -and $try1.Count -gt 0) {
    $jarFile = $try1[0]
} else {
    $libsDir = Join-Path $ScriptDir "build\libs"
    if (Test-Path $libsDir) {
        $try2 = Get-ChildItem -Path $libsDir -Filter "audit-log-server-*.jar" -File |
            Sort-Object LastWriteTime -Descending
        if ($try2 -and $try2.Count -gt 0) { $jarFile = $try2[0] }
    }
}
if (-not $jarFile) { throw "Could not find generated jar file" }
Write-Host "Found: $($jarFile.FullName)"

# ---- 4. 构造带时间戳的新文件名 ----
$ts = Get-Date -Format "yyyyMMddHHmmssfff"
$base = [System.IO.Path]::GetFileNameWithoutExtension($jarFile.Name)

# 去掉可能已存在的尾部 _时间戳 后缀（13 位以上数字）
$stem = ($base -replace '_[0-9]{13,}$', '')
$newName = "$stem`_$ts.jar"
$dest = Join-Path $ScriptDir $newName

# ---- 5. 直接移动（不保留原文件） ----
if ($jarFile.FullName -ieq $dest) {
    Write-Host "Already named $newName — nothing to do"
} else {
    Write-Host "Moving $($jarFile.Name) -> $newName"
    Move-Item -Force $jarFile.FullName $dest
}

Write-Host "Done: $newName"
