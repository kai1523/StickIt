[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'StickIt.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode) {
    throw 'Could not find the Version property in StickIt.csproj.'
}

try {
    $projectVersion = [version]$versionNode.InnerText
} catch {
    throw "The project Version '$($versionNode.InnerText)' is not a valid numeric version."
}

if ($projectVersion.Build -lt 0) {
    throw "The project Version '$($versionNode.InnerText)' must include Major, Minor, and Step numbers."
}

$versionLabel = '{0}.{1:D2}.{2:D2}' -f $projectVersion.Major, $projectVersion.Minor, $projectVersion.Build
$localOutputRoot = Join-Path $PSScriptRoot 'dist\local'
$publishDirectory = Join-Path $localOutputRoot $versionLabel
$archivePath = Join-Path $localOutputRoot "StickIt-v$versionLabel-win-x64.zip"
$archiveTempPath = Join-Path $localOutputRoot "StickIt-v$versionLabel-win-x64.pending.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found. Install the .NET 10 SDK, then run this script again.'
}

New-Item -ItemType Directory -Path $localOutputRoot -Force | Out-Null

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($localOutputRoot).TrimEnd('\') + '\'
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($resolvedOutputRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The generated publish directory is outside dist\local.'
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDirectory 'StickIt.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "The publish completed without producing StickIt.exe in $publishDirectory."
}

if (Test-Path -LiteralPath $archiveTempPath) {
    Remove-Item -LiteralPath $archiveTempPath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archiveTempPath -CompressionLevel Optimal
Move-Item -LiteralPath $archiveTempPath -Destination $archivePath -Force

$textFileCount = @(Get-ChildItem -LiteralPath $publishDirectory -Filter '*.txt' -File).Count
Write-Host "Published StickIt v$versionLabel to: $publishDirectory"
Write-Host "Packaged $textFileCount text list(s) in: $archivePath"
