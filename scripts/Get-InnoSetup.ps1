[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot '../.tools/InnoSetup'))
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The Inno Setup compiler installer requires Windows.' }
$Destination = [IO.Path]::GetFullPath($Destination)
$compiler = Join-Path $Destination 'ISCC.exe'
if (Test-Path $compiler) { return $compiler }
$download = Join-Path ([IO.Path]::GetTempPath()) ('AuthLocal-inno-' + [guid]::NewGuid() + '.exe')
try {
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $download
    $expected = '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0'
    if ((Get-FileHash $download -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup download checksum mismatch.' }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$Destination`"")
    $process = Start-Process -FilePath $download -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path $compiler)) { throw "Compiler installation failed: $($process.ExitCode)." }
    return $compiler
}
finally { Remove-Item $download -Force -ErrorAction SilentlyContinue }
