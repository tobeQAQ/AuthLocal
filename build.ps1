[CmdletBinding()]
param(
    [switch]$Installer,
    [string]$IsccPath,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipTests) {
        dotnet run --project tests/LocalAuth.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
        if (-not $IsWindows) { throw 'Build on Windows to run the required desktop checks, or use -SkipTests after testing separately.' }
        dotnet run --project tests/LocalAuth.Windows.Tests -c Release -r win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Windows checks failed.' }
    }
    [xml]$project = Get-Content src/LocalAuth.App/LocalAuth.App.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $publish = Join-Path $PSScriptRoot 'artifacts/publish/windows-x64'
    $packages = Join-Path $PSScriptRoot 'artifacts/packages'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    New-Item $publish, $packages -ItemType Directory -Force | Out-Null
    dotnet publish src/LocalAuth.App -c Release -r win-x64 -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item 'LICENSE', 'README.md', 'README.en.md', '使用说明.txt', 'THIRD-PARTY-NOTICES.txt' $publish
    Copy-Item -Recurse 'licenses' $publish
    Copy-Item -Recurse 'docs' $publish
    $portable = Join-Path $packages "AuthLocal-$version-windows-x64-portable.zip"
    Compress-Archive -Path "$publish/*" -DestinationPath $portable -Force
    if ($Installer) {
        if (-not $IsccPath) { $IsccPath = & ./scripts/Get-InnoSetup.ps1 }
        if (-not (Test-Path $IsccPath)) { throw "ISCC.exe not found: $IsccPath" }
        & $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DPackageDir=$packages" installer/AuthLocal.iss
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    $checksums = @($portable)
    if ($Installer) { $checksums += Join-Path $packages "AuthLocal-$version-windows-x64-setup.exe" }
    $lines = foreach ($file in $checksums) {
        $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))"
    }
    $lines | Set-Content (Join-Path $packages "AuthLocal-$version-windows-x64-SHA256SUMS.txt") -Encoding utf8
    Write-Host "Built AuthLocal for Windows $version in $packages"
}
finally { Pop-Location }
