<#
.SYNOPSIS
  Builds self-contained single-file Agent Speed for Windows (x64 and ARM64).
.DESCRIPTION
  Output: dist\AgentSpeed-Windows-x64.zip and dist\AgentSpeed-Windows-arm64.zip, each containing AgentSpeed.exe.
.PARAMETER Version
  Version to stamp (defaults to the repository VERSION file, then 1.0.0).
#>
[CmdletBinding()]
param(
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $root 'windows\src\AgentSpeed\AgentSpeed.csproj'
$dist = Join-Path $root 'dist'

if (-not $Version) { $Version = $env:AGENT_SPEED_VERSION }
if (-not $Version) {
    $versionFile = Join-Path $root 'VERSION'
    if (Test-Path $versionFile) { $Version = (Get-Content -Raw $versionFile).Trim() }
}
if (-not $Version) { $Version = '1.0.0' }
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$') { throw "Invalid version '$Version'" }
$fileVersion = ($Version -split '[-+]')[0]

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 10 SDK (dotnet) is required.' }

# Single-file compression keeps the exe at ~65 MB instead of ~150 MB; the zip is ~60 MB either way.
# AGENT_SPEED_COMPRESS=0 trades disk space for faster startup and less memory (no in-memory decompression).
$compress = if ($env:AGENT_SPEED_COMPRESS -eq '0') { 'false' } else { 'true' }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$work = Join-Path ([IO.Path]::GetTempPath()) ('agentspeed-publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null

try {
    foreach ($arch in @('x64', 'arm64')) {
        $rid = "win-$arch"
        $out = Join-Path $work $rid
        Write-Host "==> Publishing Agent Speed $Version for $rid"
        & dotnet publish $project `
            -c Release `
            -r $rid `
            --self-contained true `
            --artifacts-path (Join-Path $work 'artifacts') `
            -o $out `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            "-p:EnableCompressionInSingleFile=$compress" `
            -p:PublishTrimmed=false `
            -p:PublishReadyToRun=true `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -p:GenerateDocumentationFile=false `
            "-p:Version=$fileVersion" `
            "-p:InformationalVersion=$Version" `
            -p:ContinuousIntegrationBuild=true `
            -nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

        $exe = Join-Path $out 'AgentSpeed.exe'
        if (-not (Test-Path $exe)) { throw "$exe was not produced" }

        $zip = Join-Path $dist "AgentSpeed-Windows-$arch.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path $exe -DestinationPath $zip -CompressionLevel Optimal
        $size = (Get-Item $exe).Length
        Write-Host ("    AgentSpeed.exe: {0:N1} MiB -> {1}" -f ($size / 1MB), $zip)
    }
    Write-Host '==> Done'
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
