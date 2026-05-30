[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repositoryRoot "src\Lantern.Desktop\Lantern.Desktop.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$Runtime"
$installerDefinition = Join-Path $PSScriptRoot "LANtern.iss"

if (-not $Version) {
    [xml]$project = Get-Content -Raw $desktopProject
    $Version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Installer version '$Version' is invalid. Use a numeric version such as 0.2.0."
}

if (-not $SkipPublish) {
    dotnet publish $desktopProject `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        -p:Version=$Version `
        --output $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

$isccCandidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it, then run this script again."
}

& $iscc `
    "/DAppVersion=$Version" `
    "/DAppRuntime=$Runtime" `
    "/DPublishDir=..\artifacts\publish\$Runtime" `
    $installerDefinition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Get-Item (Join-Path $repositoryRoot "artifacts\installer\LANtern-Setup-$Version-$Runtime.exe") -ErrorAction SilentlyContinue

if (-not $installer) {
    throw "The installer compiler completed without producing an installer."
}

$hash = (Get-FileHash $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $installer.DirectoryName "$($installer.BaseName).sha256.txt"
"$hash  $($installer.Name)" | Set-Content -Path $checksumPath -Encoding ascii

Write-Output "Installer created: $($installer.FullName)"
Write-Output "Checksum created: $checksumPath"
