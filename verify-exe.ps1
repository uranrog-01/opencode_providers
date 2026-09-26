$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WindowProbe {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

    public static List<string> ForProcess(uint target) {
        List<string> found = new List<string>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr param) {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == target) {
                StringBuilder t = new StringBuilder(300);
                GetWindowText(hWnd, t, 300);
                StringBuilder c = new StringBuilder(300);
                GetClassName(hWnd, c, 300);
                found.Add(c.ToString() + " | title='" + t.ToString() + "' | visible=" + IsWindowVisible(hWnd));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$exe = Join-Path $PSScriptRoot 'release\OpenCodeProvidersTool.exe'

Write-Output '=== 1. embedded resources inside the exe ==='
$asm = [System.Reflection.Assembly]::LoadFile($exe)
$names = $asm.GetManifestResourceNames()
foreach ($n in $names) { Write-Output ("  resource: " + $n) }
$hasNewtonsoft = $names -contains 'Newtonsoft.Json.dll'
Write-Output ("  Newtonsoft.Json embedded: " + $hasNewtonsoft)
$asm = $null
[System.GC]::Collect()

Write-Output ''
Write-Output '=== 2. launch and list every top-level window it owns ==='
$p = Start-Process -FilePath $exe -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 8

if ($p.HasExited) {
    Write-Output ("  EXITED early with code " + $p.ExitCode)
    exit 1
}

$windows = [WindowProbe]::ForProcess([uint32]$p.Id)
foreach ($w in $windows) { Write-Output ("  window: " + $w) }

# The app shows exactly one window when healthy; anything else is a dialog (error or
# unsaved-changes prompt) and means startup did not complete cleanly.
$visible = @($windows | Where-Object { $_ -match 'visible=True' })
Write-Output ''
if ($visible.Count -ne 1) {
    Write-Output ("  RESULT: expected exactly 1 visible window, found " + $visible.Count)
    $bad = 1
} elseif ($visible[0] -notmatch 'OpenCode Providers Tool') {
    Write-Output "  RESULT: the visible window is not the main form"
    $bad = 1
} else {
    Write-Output '  RESULT: single main window, no dialogs - config was found, parsed and loaded'
    $bad = 0
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
exit $bad
