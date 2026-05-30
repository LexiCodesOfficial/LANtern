[CmdletBinding()]
param(
    [ValidateSet("linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime,
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$desktopProject = Join-Path $repositoryRoot "src\Lantern.Desktop\Lantern.Desktop.csproj"
$publishDirectory = Join-Path $artifactsRoot "publish\$Runtime"
$packagesDirectory = Join-Path $artifactsRoot "packages"
$stagingDirectory = Join-Path $artifactsRoot "staging\$Runtime"
$licensePath = Join-Path $repositoryRoot "LICENSE"
$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacOsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

if (-not $Version) {
    [xml]$project = Get-Content -Raw $desktopProject
    $Version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Package version '$Version' is invalid. Use a numeric version such as 1.0.1."
}

function Assert-ArtifactPath([string]$Path) {
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsRoot)
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify '$resolvedPath' because it is outside '$resolvedArtifacts'."
    }
}

function Reset-Directory([string]$Path) {
    Assert-ArtifactPath $Path
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Write-Checksum([System.IO.FileInfo]$Package) {
    $hash = (Get-FileHash $Package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = Join-Path $Package.DirectoryName "$($Package.Name).sha256.txt"
    "$hash  $($Package.Name)" | Set-Content -Path $checksumPath -Encoding ascii
    Write-Output "Package created: $($Package.FullName)"
    Write-Output "Checksum created: $checksumPath"
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

if (-not (Test-Path $publishDirectory)) {
    throw "Publish directory '$publishDirectory' does not exist."
}

New-Item -ItemType Directory -Path $packagesDirectory -Force | Out-Null

if ($Runtime.StartsWith("linux-", [System.StringComparison]::OrdinalIgnoreCase)) {
    Reset-Directory $stagingDirectory
    $releaseDirectoryName = "LANtern-$Version-$Runtime"
    $releaseDirectory = Join-Path $stagingDirectory $releaseDirectoryName
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $releaseDirectory -Recurse -Force
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $releaseDirectory "LICENSE") -Force
    if (-not $isWindowsHost) {
        & chmod +x (Join-Path $releaseDirectory "LANtern")
    }

    $packagePath = Join-Path $packagesDirectory "LANtern-$Version-$Runtime.tar.gz"
    Assert-ArtifactPath $packagePath
    if (Test-Path $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }

    & tar -czf $packagePath -C $stagingDirectory $releaseDirectoryName
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed with exit code $LASTEXITCODE."
    }

    Write-Checksum (Get-Item $packagePath)
    exit 0
}

Reset-Directory $stagingDirectory
$appBundle = Join-Path $stagingDirectory "LANtern.app"
$contentsDirectory = Join-Path $appBundle "Contents"
$macOsDirectory = Join-Path $contentsDirectory "MacOS"
$resourcesDirectory = Join-Path $contentsDirectory "Resources"
New-Item -ItemType Directory -Path $macOsDirectory, $resourcesDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $macOsDirectory -Recurse -Force
Copy-Item -LiteralPath $licensePath -Destination (Join-Path $resourcesDirectory "LICENSE") -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "src\Lantern.Desktop\Assets\lantern-icon.png") -Destination (Join-Path $resourcesDirectory "lantern-icon.png") -Force

if (-not $isWindowsHost) {
    & chmod +x (Join-Path $macOsDirectory "LANtern")
}

$iconFile = "lantern-icon.png"
if ($isMacOsHost -and (Get-Command sips -ErrorAction SilentlyContinue) -and (Get-Command iconutil -ErrorAction SilentlyContinue)) {
    $iconsetDirectory = Join-Path $stagingDirectory "lantern-icon.iconset"
    Reset-Directory $iconsetDirectory
    $iconTargets = @{
        "icon_16x16.png" = 16
        "icon_16x16@2x.png" = 32
        "icon_32x32.png" = 32
        "icon_32x32@2x.png" = 64
        "icon_128x128.png" = 128
        "icon_128x128@2x.png" = 256
        "icon_256x256.png" = 256
        "icon_256x256@2x.png" = 512
        "icon_512x512.png" = 512
        "icon_512x512@2x.png" = 1024
    }

    foreach ($target in $iconTargets.GetEnumerator()) {
        & sips -z $target.Value $target.Value (Join-Path $resourcesDirectory "lantern-icon.png") --out (Join-Path $iconsetDirectory $target.Key) | Out-Null
    }

    & iconutil -c icns --output (Join-Path $resourcesDirectory "lantern-icon.icns") $iconsetDirectory
    if ($LASTEXITCODE -eq 0) {
        $iconFile = "lantern-icon.icns"
    }
}

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>LANtern</string>
  <key>CFBundleDisplayName</key>
  <string>LANtern</string>
  <key>CFBundleIdentifier</key>
  <string>com.lunarbit.lantern</string>
  <key>CFBundleVersion</key>
  <string>$Version</string>
  <key>CFBundleShortVersionString</key>
  <string>$Version</string>
  <key>CFBundleExecutable</key>
  <string>LANtern</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>$iconFile</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@

Set-Content -LiteralPath (Join-Path $contentsDirectory "Info.plist") -Value $infoPlist -Encoding utf8
$packagePath = Join-Path $packagesDirectory "LANtern-$Version-$Runtime.zip"
Assert-ArtifactPath $packagePath
if (Test-Path $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

if ($isMacOsHost -and (Test-Path "/usr/bin/ditto")) {
    & /usr/bin/ditto -c -k --sequesterRsrc --keepParent $appBundle $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "ditto failed with exit code $LASTEXITCODE."
    }
}
else {
    Compress-Archive -LiteralPath $appBundle -DestinationPath $packagePath -CompressionLevel Optimal
}

Write-Checksum (Get-Item $packagePath)
