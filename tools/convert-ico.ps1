Add-Type -AssemblyName System.Drawing
$icoPath = Join-Path (Get-Location) "data.ico"
$pngPath = Join-Path (Get-Location) "src\main\resources\data.png"
$ico = New-Object System.Drawing.Icon($icoPath)
$bmp = $ico.ToBitmap()
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$ico.Dispose()
Write-Host "Converted to $pngPath size=$((Get-Item $pngPath).Length)"
