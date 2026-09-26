param(
    [string]$Exe = "release\OpenCodeProvidersTool.exe",
    [string]$Out = "shot.png",
    [int]$Wait = 8,
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [string]$AppArgs = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Shot {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(70);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    public static void GrabScreen(int x, int y, int w, int h, string path) {
        using (Bitmap bmp = new Bitmap(w, h))
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
"@

[void][Shot]::SetProcessDPIAware()
$exePath = Join-Path $PSScriptRoot $Exe
$argList = @()
if ($AppArgs.Length -gt 0) { $argList = $AppArgs.Split(" ") }
$proc = if ($argList.Count -gt 0) { Start-Process -FilePath $exePath -ArgumentList $argList -PassThru -WorkingDirectory (Split-Path $exePath) }
        else { Start-Process -FilePath $exePath -PassThru -WorkingDirectory (Split-Path $exePath) }
Start-Sleep -Seconds $Wait
if ($proc.HasExited) { throw ("exited early: " + $proc.ExitCode) }
$proc.Refresh()
$main = $proc.MainWindowHandle

# Topmost so nothing on the desktop covers it while the screen is sampled.
[void][Shot]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][Shot]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 900

$r = New-Object Shot+RECT
[void][Shot]::GetWindowRect($main, [ref]$r)

if ($ClickX -ge 0) {
    [Shot]::Click(($r.Left + $ClickX), ($r.Top + $ClickY))
    Start-Sleep -Milliseconds 1000
}

$outPath = Join-Path $PSScriptRoot $Out
[Shot]::GrabScreen($r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top), $outPath)
Write-Output ("saved " + $outPath)

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
