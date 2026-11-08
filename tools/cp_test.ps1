# Launch explorer.exe with CreateProcessW directly (the way most programs do it) and report
# whether the redirect mod is loaded in this process. ASCII only (PS 5.1 reads scripts as ANSI).
# Usage: powershell -File tools\cp_test.ps1 [-CmdArgs '/select,"C:\path\file.txt"'] [-Out file]
# Run it from a normal (non-elevated) process: Windhawk does not inject into elevated ones.
param([string]$CmdArgs = ('"' + (Resolve-Path (Join-Path $PSScriptRoot '..\assets')).Path + '"'), [string]$Out = '')

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class CP {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SI { public int cb; public string r, d, t; public int x, y, w, h, xc, yc, fa, fl; public short sw, cb2; public IntPtr r2, i, o, e; }
  [StructLayout(LayoutKind.Sequential)] public struct PI { public IntPtr hp, ht; public int pid, tid; }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool CreateProcessW(string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inh, uint fl, IntPtr env, string cwd, ref SI si, out PI pi);
  public static string Run(string cmd) { var si = new SI(); si.cb = Marshal.SizeOf(si); PI pi;
    bool ok = CreateProcessW(null, new System.Text.StringBuilder(cmd), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi);
    if (!ok) return "CreateProcess failed " + Marshal.GetLastWin32Error();
    string name = "?"; try { name = System.Diagnostics.Process.GetProcessById(pi.pid).MainModule.FileName; } catch { name = "(exited)"; }
    return "child pid=" + pi.pid + " image=" + name; }
}
'@
$me = Get-Process -Id $PID
$loaded = [bool]($me.Modules | Where-Object { $_.ModuleName -like 'refilebrowser-explorer-redirect*' })
$cmd = "explorer.exe $CmdArgs"
$res = [CP]::Run($cmd)
$lines = @("self pid=$PID mod loaded=$loaded", "ran: $cmd", $res)
if ($Out) { $lines | Set-Content -Path $Out -Encoding UTF8 } else { $lines }
