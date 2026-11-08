# Open a list of shell locations in the running (log-enabled) ReFileBrowser via command-line forwarding,
# one new tab each, then print the PROBE lines from %TEMP%\refilebrowser.log: whether ExplorerBrowser
# produced a normal file view for each location. No keystrokes are sent.
# Start ReFileBrowser with REFILEBROWSER_LOG=1 first. ASCII only (PS 5.1 reads scripts as ANSI).
param([string]$Exe = (Join-Path $PSScriptRoot '..\dist\ReFileBrowser.exe'))

$targets = [ordered]@{
    'ThisPC'              = 'shell:MyComputerFolder'
    'Desktop'             = 'shell:Desktop'
    'Downloads'           = 'shell:Downloads'
    'RecycleBin'          = 'shell:RecycleBinFolder'
    'Network'             = 'shell:NetworkPlacesFolder'
    'Libraries'           = 'shell:Libraries'
    'Home'                = 'shell:::{f874310e-b6b7-47dc-bc84-b9e6b38f5903}'
    'Gallery'             = 'shell:::{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}'
    'QuickAccess'         = 'shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}'
    'ControlPanel'        = 'shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}'
    'AllCplItems'         = 'shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}'
    'PowerOptions'        = 'shell:::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}'
    'NetworkSharing'      = 'shell:::{8E908FC9-BECC-40f6-915B-F4CA0E70D03D}'
    'ProgramsFeatures'    = 'shell:::{7b81be6a-ce2b-4676-a29e-eb907a5126c5}'
    'DevicesPrinters'     = 'shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}'
    'UserAccounts'        = 'shell:::{60632754-c523-4b62-b45c-4172da012619}'
    'NetworkConnections'  = 'shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}'
    'WindowsTools'        = 'shell:::{D20EA4E1-3957-11d2-A40B-0C5020524153}'
    'Fonts'               = 'C:\Windows\Fonts'
    'AppsFolder'          = 'shell:AppsFolder'
    'Printers'            = 'shell:PrintersFolder'
    'ZipFile'             = ''
}

# a small zip to see whether zip folders open inside
$zip = Join-Path $env:TEMP 'refilebrowser_probe.zip'
if (-not (Test-Path $zip)) { Compress-Archive -Path (Join-Path $PSScriptRoot 'shot.ps1') -DestinationPath $zip }
$targets['ZipFile'] = $zip

$log = Join-Path $env:TEMP 'refilebrowser.log'
$start = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
foreach ($name in $targets.Keys) {
    Start-Process -FilePath $Exe -ArgumentList "`"$($targets[$name])`"" -Wait
    Start-Sleep -Milliseconds 1400
}
Start-Sleep -Seconds 1
$lines = Get-Content $log -Encoding UTF8 | Select-Object -Skip $start | Where-Object { $_ -match 'PROBE' }
$i = 0
foreach ($name in $targets.Keys) {
    $hit = $lines | Where-Object { $_ -match [regex]::Escape($targets[$name].Replace('shell:', '')) -or ($i -lt $lines.Count -and $false) } | Select-Object -First 1
    "{0,-20} {1}" -f $name, $(if ($hit) { $hit -replace '^\s*\d+\s+PROBE\s+', '' } else { '(see raw lines below)' })
    $i++
}
'--- raw PROBE lines in order ---'
$lines | ForEach-Object { $_ -replace '^\s*\d+\s+PROBE\s+', '' }
