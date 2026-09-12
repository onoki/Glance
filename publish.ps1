# Build a portable Windows x64 app: .\publish.ps1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot 'portable'))
$packageRoot = [IO.Path]::GetFullPath((Join-Path $outputRoot 'Glance'))

function Assert-OutputReplaceable {
    foreach ($folder in @('data', 'blobs', 'backups', 'recovery', 'exports')) {
        if (Test-Path -LiteralPath (Join-Path $packageRoot $folder)) {
            throw "The output contains runtime data ($folder). Move portable\Glance somewhere safe before rebuilding."
        }
    }
    if (Test-Path -LiteralPath $outputRoot) {
        if ((Get-Item -LiteralPath $outputRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'The portable output directory must not be a directory link.'
        }
    }
}

Assert-OutputReplaceable
foreach ($command in @('npm.cmd', 'dotnet')) {
    if (!(Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Missing $command. The build machine needs Node.js/npm and the .NET 9 SDK."
    }
}

# A failed build leaves the previous package intact; unique staging avoids stale files.
$stagingRoot = Join-Path $sourceRoot ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
Push-Location (Join-Path $sourceRoot 'ui')
try {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
} finally {
    Pop-Location
}

& dotnet publish (Join-Path $sourceRoot 'desktop/Glance.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

$stagedUi = Join-Path $stagingRoot 'ui/dist'
New-Item -ItemType Directory -Path $stagedUi -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'ui/dist') | Copy-Item -Destination $stagedUi -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'schema') -Destination $stagingRoot -Recurse -Force
foreach ($required in @('glance.exe', 'ui/dist/index.html', 'schema/schema.sql', 'schema/migrations')) {
    if (!(Test-Path -LiteralPath (Join-Path $stagingRoot $required))) {
        throw "Package is incomplete: missing $required"
    }
}

Assert-OutputReplaceable
# Check the exact absolute replacement path before recursive deletion.
if (Test-Path -LiteralPath $packageRoot) {
    $existing = Get-Item -LiteralPath $packageRoot
    if ($existing.FullName -ne $packageRoot -or $existing.Parent.FullName -ne $outputRoot -or
        ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to replace an unexpected output path or directory link.'
    }
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Get-ChildItem -LiteralPath $stagingRoot | Copy-Item -Destination $packageRoot -Recurse -Force
Write-Host "Portable app ready: $packageRoot"
Write-Host 'Copy this entire folder to the production PC and double-click glance.exe.'
Write-Host 'The package contains no development data. No environment variables are needed.'
