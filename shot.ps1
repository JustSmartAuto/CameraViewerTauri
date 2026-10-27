param(
    [string]$OutFile = "d:\JustStupid\CameraHelper相机显示_2606120922\NomacsSvgOverlayPlugin\nomacs_pm2.png",
    [string]$Keys = "%u",   # keys to send after activating nomacs
    [int]$WheelDelta = 0,   # mouse wheel notches over the window center (+ = zoom in)
    [int]$ProcessId = 0,    # pin a specific nomacs pid (skip auto-detection)
    [int]$DelayMs = 1500
)
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinApiInput {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, int d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$p = if ($ProcessId -gt 0) {
    Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
} else {
    Get-Process nomacs -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Sort-Object StartTime -Descending | Select-Object -First 1
}
if (-not $p) { Write-Host "nomacs (with window) not running"; exit 1 }

[Microsoft.VisualBasic.Interaction]::AppActivate($p.Id)
Start-Sleep -Milliseconds 300
# force to the foreground – AppActivate alone loses against other topmost windows
[void][WinApiInput]::ShowWindow($p.MainWindowHandle, 9)   # SW_RESTORE
[void][WinApiInput]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 500

$ws = New-Object -ComObject WScript.Shell
foreach ($k in ($Keys -split ',')) {
    $ws.SendKeys($k)
    Start-Sleep -Milliseconds 500
}

# mouse wheel (e.g. nomacs zoom) at the window center
if ($WheelDelta -ne 0) {
    $r = New-Object WinApiInput+RECT
    [void][WinApiInput]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    $cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
    [void][WinApiInput]::SetCursorPos($cx, $cy)
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt [Math]::Abs($WheelDelta); $i++) {
        [WinApiInput]::mouse_event(0x0800, 0, 0, 120 * [Math]::Sign($WheelDelta), [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 200
    }
}
Start-Sleep -Milliseconds $DelayMs

$b = New-Object System.Drawing.Bitmap([System.Windows.Forms.SystemInformation]::VirtualScreen.Width, [System.Windows.Forms.SystemInformation]::VirtualScreen.Height)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(0, 0, 0, 0, $b.Size)
$b.Save($OutFile)
$g.Dispose(); $b.Dispose()
Write-Host "saved $OutFile"
