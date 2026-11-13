[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][version]$CurrentVersion,
    [string]$Destination = (Join-Path $PSScriptRoot '../.tools/upgrade-baseline')
)
$ErrorActionPreference = 'Stop'
$json = gh api "repos/$Repository/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'Could not read published releases for upgrade testing.' }
$releases = $json | ConvertFrom-Json
$previous = $releases | Where-Object {
    $parsed = [version]'0.0'
    -not $_.draft -and -not $_.prerelease -and
        [version]::TryParse($_.tag_name.TrimStart('v'), [ref]$parsed) -and $parsed -lt $CurrentVersion
} | Sort-Object { [version]$_.tag_name.TrimStart('v') } -Descending | Select-Object -First 1
if (-not $previous) { throw "No published version older than $CurrentVersion is available for upgrade testing." }
$previousVersion = $previous.tag_name.TrimStart('v')
$assetName = "AuthLocal-$previousVersion-windows-x64-setup.exe"
$asset = $previous.assets | Where-Object name -EQ $assetName | Select-Object -First 1
if (-not $asset -or $asset.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') { throw 'The previous installer has no SHA256-verified release asset.' }
if (-not $asset.browser_download_url.StartsWith("https://github.com/$Repository/releases/download/", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected previous-release download URL.'
}
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item $Destination -ItemType Directory -Force | Out-Null
$path = Join-Path $Destination $assetName
Invoke-WebRequest $asset.browser_download_url -OutFile $path
if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $asset.digest.Substring(7)) { throw 'Previous release installer checksum mismatch.' }
[pscustomobject]@{ Version = $previousVersion; Path = $path }
