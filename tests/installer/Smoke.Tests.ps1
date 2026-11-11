[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$UpgradeInstaller,
    [string]$ExpectedVersion = '1.5.0',
    [string]$UpgradeVersion = '1.5.0.1'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Installer smoke checks require Windows.' }
$Installer = (Resolve-Path $Installer).Path
$UpgradeInstaller = (Resolve-Path $UpgradeInstaller).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('AuthLocal-setup-test-' + [guid]::NewGuid())
$installPath = Join-Path $temporary 'Application'
$dataPath = Join-Path $env:LOCALAPPDATA 'LocalAuth'
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser', 'Registry64')
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8CEEC88B-634A-4BAE-A5C2-104385962A36}_is1'
$metadataKey = 'Software\AuthLocal'
if ($registry.OpenSubKey($metadataKey) -or (Test-Path $dataPath)) {
    throw 'Run in a fresh Windows test profile; existing AuthLocal data will not be touched.'
}
New-Item $temporary, $dataPath -ItemType Directory -Force | Out-Null
$fixture = [byte[]](1, 3, 5, 7, 9, 11)
[IO.File]::WriteAllBytes((Join-Path $dataPath 'accounts.vault'), $fixture)
[IO.File]::WriteAllBytes((Join-Path $dataPath 'ordinary.local'), $fixture)
'{"DisclaimerVersion":1,"ThemeMode":"Dark","Language":"fr","AutoLockMinutes":5}' |
    Set-Content (Join-Path $dataPath 'settings.json') -Encoding utf8
$before = @(Get-ChildItem $dataPath -File | Get-FileHash | Select-Object -ExpandProperty Hash)
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "PASS: $Message"
}
function Run-Setup([string]$File, [string]$LogName, [switch]$ExpectBlocked) {
    $log = Join-Path $temporary $LogName
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installPath`"", "/LOG=`"$log`"")
    $process = Start-Process $File -ArgumentList $arguments -Wait -PassThru
    if ($ExpectBlocked) { Assert ($process.ExitCode -ne 0) 'running-app mutex blocks an unsafe replacement' }
    elseif ($process.ExitCode -ne 0) { Get-Content $log; throw "Setup failed with $($process.ExitCode)." }
}
function Assert-Registration([string]$Version) {
    $metadata = $registry.OpenSubKey($metadataKey)
    $uninstall = $registry.OpenSubKey($uninstallKey)
    try {
        Assert ($null -ne $metadata -and $metadata.GetValue('InstallPath') -eq $installPath -and $metadata.GetValue('Version') -eq $Version) 'per-user install path and version registered'
        Assert ($null -ne $uninstall -and $uninstall.GetValue('DisplayVersion') -eq $Version -and $uninstall.GetValue('DisplayName') -eq 'AuthLocal for Windows') 'stable Windows uninstall entry has the current version'
        $metadataNames = @($metadata.GetValueNames() | Sort-Object)
        Assert (($metadataNames -join ',') -eq 'InstallPath,Version') 'registry contains installation metadata only'
    }
    finally { if ($metadata) { $metadata.Dispose() }; if ($uninstall) { $uninstall.Dispose() } }
}
function Assert-Data {
    $after = @(Get-ChildItem $dataPath -File | Get-FileHash | Select-Object -ExpandProperty Hash)
    Assert (($before -join ',') -eq ($after -join ',')) 'vault, ordinary storage and preferences preserved byte for byte'
}
$mutexJob = $null
try {
    Run-Setup $Installer 'install.log'
    Assert (Test-Path (Join-Path $installPath 'AuthLocal.exe')) 'installer deploys AuthLocal.exe'
    Assert-Registration $ExpectedVersion
    Assert-Data
    $ready = Join-Path $temporary 'mutex-ready'
    $mutexJob = Start-Job -ArgumentList $ready -ScriptBlock {
        param($Ready)
        $mutex = [Threading.Mutex]::new($false, 'Local\AuthLocal.App')
        try { [IO.File]::WriteAllText($Ready, 'ready'); Start-Sleep -Seconds 90 }
        finally { $mutex.Dispose() }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path $ready) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert (Test-Path $ready) 'simulated running-app mutex acquired'
    Run-Setup $UpgradeInstaller 'blocked-upgrade.log' -ExpectBlocked
    Assert-Registration $ExpectedVersion
    Stop-Job $mutexJob; Remove-Job $mutexJob; $mutexJob = $null
    Run-Setup $UpgradeInstaller 'upgrade.log'
    Assert-Registration $UpgradeVersion
    Assert-Data
    $uninstallEntries = $registry.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall')
    try {
        $matches = @($uninstallEntries.GetSubKeyNames() | Where-Object { $_ -like '*8CEEC88B-634A-4BAE-A5C2-104385962A36*' })
        Assert ($matches.Count -eq 1) 'upgrade reuses one uninstall entry'
    }
    finally { $uninstallEntries.Dispose() }
    $uninstaller = Join-Path $installPath 'unins000.exe'
    $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
    Assert ($process.ExitCode -eq 0) 'uninstaller succeeds'
    Assert (-not (Test-Path (Join-Path $installPath 'AuthLocal.exe'))) 'uninstaller removes the application'
    Assert ($null -eq $registry.OpenSubKey($metadataKey) -and $null -eq $registry.OpenSubKey($uninstallKey)) 'uninstaller removes registration'
    Assert-Data
    Write-Host 'All install, blocked-update, upgrade and uninstall checks passed.'
}
finally {
    if ($mutexJob) { Stop-Job $mutexJob; Remove-Job $mutexJob }
    $registry.Dispose()
    # Remove only the synthetic fixture made by this script, never a real profile.
    if (Test-Path $dataPath) { Remove-Item $dataPath -Recurse -Force }
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }
}
