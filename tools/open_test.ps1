# Try common "open folder" methods one by one and report whether ReFileBrowser or the system
# Explorer opened the new window; closes the new windows afterwards. (ASCII only: PS 5.1 reads it as ANSI)
# Usage: powershell -STA -File tools\open_test.ps1 [-Only name,...]
param([string[]]$Only)

Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class OT {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr h, string verb, string file, string args, string dir, int show);
  [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SHParseDisplayName(string n, IntPtr bc, out IntPtr pidl, uint a, out uint b);
  [DllImport("shell32.dll")] static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint flags);
  [DllImport("shell32.dll")] static extern void ILFree(IntPtr p);
  [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr r, uint c);
  public static int OpenAndSelect(string file) {
    IntPtr p; uint o; int hr = SHParseDisplayName(file, IntPtr.Zero, out p, 0, out o); if (hr < 0) return hr;
    hr = SHOpenFolderAndSelectItems(p, 0, IntPtr.Zero, 0); ILFree(p); return hr;
  }
  // visible top-level windows: hwnd -> "class|process|title"
  public static Dictionary<long, string> Snapshot() {
    var d = new Dictionary<long, string>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      var c = new StringBuilder(128); GetClassName(h, c, 128);
      var t = new StringBuilder(256); GetWindowText(h, t, 256);
      uint pid; GetWindowThreadProcessId(h, out pid);
      string pn = "?"; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
      d[(long)h] = c + "|" + pn + "|" + t; return true; }, IntPtr.Zero);
    return d;
  }
}
'@
[OT]::CoInitializeEx([IntPtr]::Zero, 2) | Out-Null

$dir = (Resolve-Path (Join-Path $PSScriptRoot '..\assets')).Path
$file = Join-Path $dir 'app.ico'
$tests = [ordered]@{
  'ShellExecute(default verb)'  = { [OT]::ShellExecute([IntPtr]::Zero, $null, $dir, $null, $null, 1) | Out-Null }
  'ShellExecute("open")'        = { [OT]::ShellExecute([IntPtr]::Zero, 'open', $dir, $null, $null, 1) | Out-Null }
  'ShellExecute("explore")'     = { [OT]::ShellExecute([IntPtr]::Zero, 'explore', $dir, $null, $null, 1) | Out-Null }
  'explorer.exe <dir>'          = { Start-Process explorer.exe -ArgumentList "`"$dir`"" }
  'explorer.exe /select,<file>' = { Start-Process explorer.exe -ArgumentList "/select,`"$file`"" }
  'SHOpenFolderAndSelectItems'  = { [OT]::OpenAndSelect($file) | Out-Null }
}

foreach ($name in $tests.Keys) {
  if ($Only -and -not ($Only | Where-Object { $name -like "*$_*" })) { continue }
  $before = [OT]::Snapshot()
  & $tests[$name]
  $new = @()
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 6000) {
    Start-Sleep -Milliseconds 200
    $now = [OT]::Snapshot()
    $new = @($now.Keys | Where-Object { -not $before.ContainsKey($_) -and ($now[$_] -match '^(CabinetWClass|WindowsForms)') })
    if ($new.Count -gt 0) { Start-Sleep -Milliseconds 800; $now = [OT]::Snapshot(); break }
  }
  if ($new.Count -eq 0) { "{0,-30} -> (no new window)" -f $name; continue }
  foreach ($h in $new) {
    $parts = $now[$h] -split '\|', 3
    $who = if ($parts[1] -eq 'ReFileBrowser') { 'ReFileBrowser' } elseif ($parts[0] -eq 'CabinetWClass') { 'SYSTEM-EXPLORER' } else { $parts[1] }
    "{0,-30} -> {1}  {2}ms" -f $name, $who, $sw.ElapsedMilliseconds
    [OT]::PostMessage([IntPtr]$h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE
  }
  Start-Sleep -Milliseconds 800
}
