# Compile windhawk\refilebrowser-explorer-redirect.wh.cpp with Windhawk's bundled clang (same flags as the
# Windhawk UI uses) for x64 and x86, and install it as a local mod.
# Usage: powershell -File tools\install_mod.ps1 [-Windhawk <install dir>] [-Exe <ReFileBrowser.exe>] [-Uninstall]
#   -Windhawk  defaults to the folder of the running windhawk.exe, else %ProgramFiles%\Windhawk
#   -Exe       defaults to ..\dist\ReFileBrowser.exe; leave the mod setting empty to use the registered path
# A non-portable Windhawk keeps its data under %ProgramData%: run this from an elevated prompt then.
# Restart Windhawk afterwards (windhawk.exe -restart-bg) so it picks the mod up.
# ASCII only: Windows PowerShell 5.1 reads scripts as ANSI.
param([string]$Windhawk = '', [string]$Exe = '', [switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$id = 'refilebrowser-explorer-redirect'
$version = '1.1'
$src = Join-Path $PSScriptRoot "..\windhawk\$id.wh.cpp"

if (-not $Windhawk) {
    $running = Get-Process windhawk -ErrorAction SilentlyContinue | Where-Object Path | Select-Object -First 1
    $Windhawk = if ($running) { Split-Path $running.Path } else { Join-Path $env:ProgramFiles 'Windhawk' }
}
$whIni = Join-Path $Windhawk 'windhawk.ini'
if (-not (Test-Path $whIni)) { throw "Windhawk not found in '$Windhawk' (no windhawk.ini). Pass -Windhawk <install dir>." }
# windhawk.ini [Storage]: CompilerPath / EnginePath / AppDataPath, relative to the install dir or with %vars%
$storage = @{}
foreach ($line in Get-Content $whIni) { if ($line -match '^\s*([A-Za-z]+Path)\s*=\s*(.+?)\s*$') { $storage[$Matches[1]] = $Matches[2] } }
function WhPath([string]$key, [string]$fallback) {
    $v = if ($storage[$key]) { [Environment]::ExpandEnvironmentVariables($storage[$key]) } else { $fallback }
    if ([IO.Path]::IsPathRooted($v)) { $v } else { Join-Path $Windhawk $v }
}
$compiler = WhPath 'CompilerPath' 'Compiler'
$engine = WhPath 'EnginePath' 'Engine'
$appData = WhPath 'AppDataPath' (Join-Path $env:ProgramData 'Windhawk')
$mods = Join-Path $appData 'Engine\Mods'
$sources = Join-Path $appData 'ModsSource'
$ini = Join-Path $mods "$id.ini"
if (-not $Exe) { $d = Join-Path $PSScriptRoot '..\dist\ReFileBrowser.exe'; if (Test-Path $d) { $Exe = (Resolve-Path $d).Path } }
Write-Output "Windhawk: $Windhawk  engine: $engine  data: $appData"

function Remove-OldDlls {
    foreach ($arch in '32', '64') {
        Get-ChildItem (Join-Path $mods $arch) -Filter "${id}_*.dll" -ErrorAction SilentlyContinue |
            ForEach-Object { try { Remove-Item $_.FullName -Force } catch { Write-Output "in use, left: $($_.Name)" } }
    }
}

if ($Uninstall) {
    Remove-Item $ini -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $sources "$id.wh.cpp") -Force -ErrorAction SilentlyContinue
    Remove-OldDlls
    Write-Output 'uninstalled'
    exit 0
}

$code = [IO.File]::ReadAllText($src)
$dllName = "${id}_${version}_$(Get-Random -Minimum 100000 -Maximum 999999).dll"
$clang = Join-Path $compiler 'bin\clang++.exe'

foreach ($t in @(@{ target = 'x86_64-w64-mingw32'; sub = '64' }, @{ target = 'i686-w64-mingw32'; sub = '32' })) {
    $out = Join-Path $mods "$($t.sub)\$dllName"
    $lib = Join-Path $engine "$($t.sub)\windhawk.lib"
    $argList = @('-std=c++23', '-O2', '-shared', '-DUNICODE', '-D_UNICODE',
        '-DWINVER=0x0A00', '-D_WIN32_WINNT=0x0A00', '-D_WIN32_IE=0x0A00', '-DNTDDI_VERSION=0x0A000008',
        '-D__USE_MINGW_ANSI_STDIO=0', '-DWH_MOD', "-DWH_MOD_ID=L`"$id`"", "-DWH_MOD_VERSION=L`"$version`"",
        $lib, '-x', 'c++', '-', '-include', 'windhawk_api.h', '-target', $t.target,
        '-Wl,--export-all-symbols', '-o', $out)
    $psi = New-Object Diagnostics.ProcessStartInfo $clang
    $psi.Arguments = ($argList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $compiler
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Write($code); $p.StandardInput.Close()
    $err = $p.StandardError.ReadToEnd(); $null = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Output "compile failed for $($t.target):"; Write-Output $err; exit 1 }
    if ($err) { Write-Output "[$($t.target)] $err" }
    Write-Output "built $($t.sub)\$dllName ($([int]((Get-Item $out).Length / 1KB)) KB)"
}

# Settings: keep the user's values if the ini already exists
$settings = [ordered]@{ exePath = $Exe; redirectPlainLaunch = '1'; log = '0' }
if (Test-Path $ini) {
    $inSettings = $false
    foreach ($line in Get-Content $ini) {
        if ($line -match '^\[(.+)\]$') { $inSettings = $Matches[1] -eq 'Settings'; continue }
        if ($inSettings -and $line -match '^([^=]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
    }
}
$old = @(Get-ChildItem (Join-Path $mods '64') -Filter "${id}_*.dll" | Where-Object Name -ne $dllName)

$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
# Include/Exclude come from the mod header (@include / @exclude lines), joined with '|' like Windhawk does
$include = ([regex]::Matches($code, '(?m)^//\s*@include\s+(\S+)') | ForEach-Object { $_.Groups[1].Value }) -join '|'
$exclude = ([regex]::Matches($code, '(?m)^//\s*@exclude\s+(\S+)') | ForEach-Object { $_.Groups[1].Value }) -join '|'
$lines = @('[Mod]', "LibraryFileName=$dllName", 'Disabled=0', "Include=$include", "Exclude=$exclude", 'Architecture=',
    "Version=$version", "SettingsChangeTime=$now", '', '[Settings]')
$lines += $settings.Keys | ForEach-Object { "$_=$($settings[$_])" }
[IO.File]::WriteAllLines($ini, $lines, (New-Object Text.UTF8Encoding $false))
Copy-Item $src (Join-Path $sources "$id.wh.cpp") -Force

foreach ($f in $old) { foreach ($arch in '32', '64') { Remove-Item (Join-Path $mods "$arch\$($f.Name)") -Force -ErrorAction SilentlyContinue } }
Write-Output "installed: $ini"
