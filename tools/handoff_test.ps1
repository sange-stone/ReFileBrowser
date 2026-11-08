# For each target: forward it to the running ReFileBrowser (command line, no keystrokes) and report who
# showed it - a new system Explorer window (CabinetWClass) or ReFileBrowser ("attach" line in its log).
# New Explorer windows opened by the test are closed again. Needs ReFileBrowser started with REFILEBROWSER_LOG=1.
# ASCII only (PS 5.1 reads scripts as ANSI).
param([string]$Exe = (Join-Path $PSScriptRoot '..\dist\ReFileBrowser.exe'))

Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class HT {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static Dictionary<long, string> Cabinets() { var d = new Dictionary<long, string>(); EnumWindows((h, x) => {
    if (!IsWindowVisible(h)) return true; var c = new StringBuilder(64); GetClassName(h, c, 64);
    if (c.ToString() == "CabinetWClass") { var t = new StringBuilder(256); GetWindowText(h, t, 256); d[(long)h] = t.ToString(); } return true; }, IntPtr.Zero); return d; }
}
'@

$targets = [ordered]@{
    'ControlPanel'   = 'shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}'
    'AllCplItems'    = 'shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}'
    'PowerOptions'   = 'shell:::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}'
    'UserAccounts'   = 'shell:::{60632754-c523-4b62-b45c-4172da012619}'
    'RecycleBin'     = 'shell:RecycleBinFolder'
    'WindowsTools'   = 'shell:::{D20EA4E1-3957-11d2-A40B-0C5020524153}'
    'PlainFolder'    = (Resolve-Path (Join-Path $PSScriptRoot '..\assets')).Path
}
$log = Join-Path $env:TEMP 'refilebrowser.log'
foreach ($name in $targets.Keys) {
    $before = [HT]::Cabinets()
    $logStart = (Get-Content $log).Count
    Start-Process -FilePath $Exe -ArgumentList "`"$($targets[$name])`"" -Wait
    Start-Sleep -Milliseconds 2500
    $after = [HT]::Cabinets()
    $new = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
    $logNew = Get-Content $log -Encoding UTF8 | Select-Object -Skip $logStart
    $attached = @($logNew | Where-Object { $_ -match 'navigated host=\S+ first=True' }).Count
    $handed = @($logNew | Where-Object { $_ -match 'hand to Explorer' }).Count
    $who = if ($new.Count -gt 0 -and $attached -eq 0) { 'SYSTEM-EXPLORER' } elseif ($attached -gt 0 -and $new.Count -eq 0) { 'ReFileBrowser' } elseif ($new.Count -eq 0 -and $attached -eq 0) { 'NOTHING' } else { 'BOTH' }
    "{0,-14} -> {1,-16} explorerWindows+{2} refilebrowserTabs+{3} handedOff={4}  {5}" -f $name, $who, $new.Count, $attached, $handed, (($new | ForEach-Object { $after[$_] }) -join ', ')
    foreach ($h in $new) { [HT]::PostMessage([IntPtr]$h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    Start-Sleep -Milliseconds 600
}
