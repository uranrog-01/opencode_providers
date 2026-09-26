param(
    [string]$Exe = "release\OpenCodeProvidersTool.exe",
    [int]$GrabY = 28,
    [int]$MoveX = 140,
    [int]$MoveY = 90
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Drag {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
    public static void Down() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
    public static void Up() { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
}
"@

[void][Drag]::SetProcessDPIAware()

$exePath = Join-Path $PSScriptRoot $Exe
$proc = Start-Process -FilePath $exePath -PassThru -WorkingDirectory (Split-Path $exePath)
Start-Sleep -Seconds 8
if ($proc.HasExited) { throw ("app exited early: " + $proc.ExitCode) }
$proc.Refresh()
$main = $proc.MainWindowHandle

[void][Drag]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][Drag]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800

$before = [Drag]::Rect($main)
Write-Output ("before: " + $before.Left + "," + $before.Top)

# grab the caption bar and drag it
$grabX = $before.Left + [int](($before.Right - $before.Left) / 2)
$grabY = $before.Top + $GrabY
[void][Drag]::SetCursorPos($grabX, $grabY)
Start-Sleep -Milliseconds 250
[Drag]::Down()
Start-Sleep -Milliseconds 250
for ($i = 1; $i -le 8; $i++) {
    [void][Drag]::SetCursorPos($grabX + [int]($MoveX * $i / 8), $grabY + [int]($MoveY * $i / 8))
    Start-Sleep -Milliseconds 60
}
Start-Sleep -Milliseconds 200
[Drag]::Up()
Start-Sleep -Milliseconds 600

$after = [Drag]::Rect($main)
Write-Output ("after:  " + $after.Left + "," + $after.Top)

$dx = $after.Left - $before.Left
$dy = $after.Top - $before.Top
Write-Output ("moved by: " + $dx + "," + $dy)

if ([Math]::Abs($dx) -gt 20 -or [Math]::Abs($dy) -gt 20) {
    Write-Output "RESULT: window moved - dragging works"
    $bad = 0
} else {
    Write-Output "RESULT: window did NOT move - dragging is broken"
    $bad = 1
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
exit $bad
