# Capture a program's main window with PrintWindow (works even when it is covered) and save it scaled.
# Usage: powershell -File tools\shot.ps1 [-Name ReFileBrowser] [-Title <window title>] [-Scale 0.5] [-Out path]
# ASCII only (Windows PowerShell 5.1 reads scripts as ANSI).
param([double]$Scale = 0.5, [string]$Out = "$env:TEMP\refilebrowser_shot.png", [string]$Name = 'ReFileBrowser', [string]$Title = '')

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class Shot {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  // first visible, titled, uncloaked window of the process (optionally with an exact title)
  public static IntPtr Find(uint pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, x) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      int cloaked; DwmGetWindowAttribute(h, 14, out cloaked, 4);
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.Length == 0 || cloaked != 0) return true;
      if (title.Length == 0 || sb.ToString() == title) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@
[Shot]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Name"; exit 1 }
$h = [Shot]::Find([uint32]$p.Id, $Title)
if ($h -eq [IntPtr]::Zero) { Write-Output "no window titled '$Title'"; exit 1 }
$r = New-Object Shot+RECT
[Shot]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$bmp = New-Object Drawing.Bitmap $w, $hgt
$g = [Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Shot]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$sw = [int]($w * $Scale); $sh = [int]($hgt * $Scale)
$small = New-Object Drawing.Bitmap $sw, $sh
$g2 = [Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = 'HighQualityBicubic'
$g2.DrawImage($bmp, 0, 0, $sw, $sh)
$g2.Dispose(); $bmp.Dispose()
$small.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $small.Dispose()
Write-Output "pid=$($p.Id) rect=$($r.L),$($r.T) ${w}x${hgt} -> $Out"
