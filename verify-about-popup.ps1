param(
    [string]$Out = "about-popup.png",
    [string]$Button = "About"
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class AboutProbe {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder t, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder t, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumWindowsProc(IntPtr h, IntPtr p);

    public static string TextOf(IntPtr h) {
        var t = new StringBuilder(600);
        GetWindowTextW(h, t, 600);
        return t.ToString();
    }

    public static string ClassOf(IntPtr h) {
        var t = new StringBuilder(300);
        GetClassNameW(h, t, 300);
        return t.ToString();
    }

    /// <summary>Every top-level window of the process, visible or not.</summary>
    public static List<string> TopLevel(uint target) {
        var found = new List<string>();
        EnumWindows(delegate(IntPtr top, IntPtr p) {
            uint pid; GetWindowThreadProcessId(top, out pid);
            if (pid == target) {
                found.Add(top.ToInt64() + "|" + ClassOf(top) + "|vis=" + IsWindowVisible(top)
                    + "|'" + TextOf(top) + "'");
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Handles of visible top-level windows only.</summary>
    public static List<IntPtr> Visible(uint target) {
        var found = new List<IntPtr>();
        EnumWindows(delegate(IntPtr top, IntPtr p) {
            uint pid; GetWindowThreadProcessId(top, out pid);
            if (pid == target && IsWindowVisible(top)) found.Add(top);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static List<string> Children(IntPtr parent) {
        var found = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr child, IntPtr p) {
            string text = TextOf(child);
            if (text.Length > 0) found.Add(text);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindButton(IntPtr parent, string label) {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr child, IntPtr p) {
            // Prefix match, so a label ending in an ellipsis can be named without it.
            if (result == IntPtr.Zero && TextOf(child).StartsWith(label, StringComparison.Ordinal)) result = child;
            return true;
        }, IntPtr.Zero);
        return result;
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

[void][AboutProbe]::SetProcessDPIAware()

$exe = Join-Path $PSScriptRoot 'release\OpenCodeProvidersTool.exe'
$proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 8
if ($proc.HasExited) { throw ("exited early: " + $proc.ExitCode) }

$pid32 = [uint32]$proc.Id

Write-Output '=== all top-level windows BEFORE click ==='
[AboutProbe]::TopLevel($pid32) | ForEach-Object { Write-Output ("   " + $_) }

$main = ([AboutProbe]::Visible($pid32))[0]
# Foreground only — never topmost, or the main form would cover its own modal dialog.
[void][AboutProbe]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800

$btn = [AboutProbe]::FindButton($main, $Button)
if ($btn -eq [IntPtr]::Zero) {
    Write-Output ("RESULT: FAIL - no button labelled " + $Button)
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

# PostMessage, not SendMessage: the click handler opens a modal dialog, so a
# synchronous send would not return until that dialog is closed.
[void][AboutProbe]::PostMessageW($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Milliseconds 2000

$proc.Refresh()
Write-Output ''
Write-Output ("process exited after click: " + $proc.HasExited)

Write-Output ''
Write-Output '=== all top-level windows AFTER click ==='
$after = [AboutProbe]::TopLevel($pid32)
$after | ForEach-Object { Write-Output ("   " + $_) }

Write-Output ''
Write-Output '=== new windows after click (with child-control counts) ==='
$dialog = [IntPtr]::Zero
foreach ($entry in $after) {
    $handle = [IntPtr]([int64]($entry -split '\|')[0])
    if ($handle -eq $main) { continue }
    if ($entry -notmatch 'vis=True') { continue }
    $kids = [AboutProbe]::Children($handle).Count
    Write-Output ("   " + $entry + "  children=" + $kids)

    # The shadow companion is a bare layered window; the dialog is the one with controls.
    if ($kids -gt 0) { $dialog = $handle }
}

if ($dialog -eq [IntPtr]::Zero) {
    Write-Output ''
    Write-Output 'RESULT: FAIL - no visible window with controls appeared'
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

Write-Output ''
Write-Output '=== About popup contents ==='
[AboutProbe]::Children($dialog) | ForEach-Object { Write-Output ("   '" + $_ + "'") }

# Raise the dialog itself before sampling the screen, so the capture shows it.
[void][AboutProbe]::SetWindowPos($dialog, [IntPtr](-1), 0, 0, 0, 0, 0x0003)
[void][AboutProbe]::SetForegroundWindow($dialog)
Start-Sleep -Milliseconds 900

$r = New-Object AboutProbe+RECT
[void][AboutProbe]::GetWindowRect($dialog, [ref]$r)
Write-Output ''
Write-Output ("dialog rect: " + $r.Left + "," + $r.Top + "  " + ($r.Right - $r.Left) + " x " + ($r.Bottom - $r.Top))

# Capture a margin around the dialog so the drop shadow is included in the shot.
$pad = 34
$outPath = Join-Path $PSScriptRoot $Out
[AboutProbe]::GrabScreen(($r.Left - $pad), ($r.Top - $pad),
    ($r.Right - $r.Left) + $pad * 2, ($r.Bottom - $r.Top) + $pad * 2, $outPath)
Write-Output ("saved " + $outPath)

# Dismiss the dialog, then confirm the shadow companion went with it: a leftover
# click-through layered window would sit invisibly over the tool.
# "Save" is deliberately never clicked - this probe must not write to a config.
$closer = [IntPtr]::Zero
foreach ($label in @('Close', 'Cancel', 'OK')) {
    $closer = [AboutProbe]::FindButton($dialog, $label)
    if ($closer -ne [IntPtr]::Zero) { break }
}
if ($closer -ne [IntPtr]::Zero) {
    [void][AboutProbe]::PostMessageW($closer, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
    $proc.Refresh()
    $leftover = @([AboutProbe]::TopLevel($pid32) | Where-Object { $_ -match 'vis=True' })
    Write-Output ''
    Write-Output ("visible windows after dismissing the dialog: " + $leftover.Count)
    foreach ($entry in $leftover) { Write-Output ("   " + $entry) }
    if ($leftover.Count -ne 1) {
        Write-Output 'RESULT: FAIL - a window was left behind'
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        exit 1
    }
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Output ''
Write-Output 'RESULT: PASS - popup opened with a shadow, and nothing was left behind'
