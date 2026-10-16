[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][version]$Version,
    [string]$PublishDirectory,
    [string]$OutputRoot,
    [string]$BuildOutputDirectory,
    [string]$UpdaterBuildDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'outputs\winforms-updates' }
$bundleName = $Version.ToString() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
$bundle = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $bundleName
New-Item -ItemType Directory -Path $bundle -ErrorAction Stop | Out-Null

if (-not $BuildOutputDirectory) {
    $BuildOutputDirectory = Join-Path $bundle 'Build\WinForms'
    & dotnet build (Join-Path $repoRoot 'MachineDataAcquisitionSystem\MachineDataAcquisitionSystem.csproj') -c $Configuration "-p:OutDir=$BuildOutputDirectory\" --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '采集主程序构建失败，未发布版本。' }
}
if (-not $UpdaterBuildDirectory) {
    $UpdaterBuildDirectory = Join-Path $bundle 'Build\Updater'
    & dotnet build (Join-Path $repoRoot 'Acquisition.Updater\Acquisition.Updater.csproj') -c $Configuration "-p:OutDir=$UpdaterBuildDirectory\" --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '更新器构建失败，未发布版本。' }
}
$applicationName = '文件数据采集系统.exe'
if (-not (Test-Path -LiteralPath (Join-Path $BuildOutputDirectory $applicationName) -PathType Leaf)) { throw '发布输出缺少采集主程序。' }
$sourceRoot = [IO.Path]::GetFullPath($BuildOutputDirectory).TrimEnd('\') + '\'
$programFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Where-Object {
    $relative = $_.FullName.Substring($sourceRoot.Length).Replace('\', '/')
    $first = $relative.Split('/')[0]
    $first -notin @('Data', 'GeneratedModels', 'Logs', 'Log', 'UpdateCache') -and
    $relative -notmatch '^Updater\.exe($|\.)' -and
    ($_.Name -match '\.(exe|dll)$|\.exe\.config$' -or ($first -eq 'Resources' -and $_.Extension -ne '.json'))
} | Sort-Object FullName)

$packages = Join-Path $bundle 'packages'
New-Item -ItemType Directory -Path $packages | Out-Null
$packageName = $Version.ToString() + '.zip'
$packagePath = Join-Path $packages $packageName
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $programFiles) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
$manifest = [ordered]@{ version = $Version.ToString(); package = 'packages/' + $packageName } | ConvertTo-Json
$latest = Join-Path $bundle 'latest.json'
[IO.File]::WriteAllText($latest, $manifest, [Text.UTF8Encoding]::new($false))

$bootstrap = Join-Path $bundle '首次接入'
New-Item -ItemType Directory -Path $bootstrap | Out-Null
Copy-Item -LiteralPath (Join-Path $UpdaterBuildDirectory 'Updater.exe') -Destination (Join-Path $bootstrap 'Updater.exe')
$updaterConfig = Join-Path $UpdaterBuildDirectory 'Updater.exe.config'
if (Test-Path -LiteralPath $updaterConfig) { Copy-Item -LiteralPath $updaterConfig -Destination (Join-Path $bootstrap 'Updater.exe.config') }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-WinFormsUpdater.ps1') -Destination (Join-Path $bootstrap 'Install-WinFormsUpdater.ps1')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\shared-folder-updater.md') -Destination (Join-Path $bootstrap '使用说明.md')

if ($PublishDirectory) {
    $publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
    $remotePackages = Join-Path $publishRoot 'packages'
    New-Item -ItemType Directory -Path $remotePackages -Force | Out-Null
    $remotePackage = Join-Path $remotePackages $packageName
    if (Test-Path -LiteralPath $remotePackage) { throw "同版本更新包已存在，请递增版本号：$remotePackage" }
    Copy-Item -LiteralPath $packagePath -Destination $remotePackage
    $pending = Join-Path $publishRoot ('latest-' + [guid]::NewGuid().ToString('N') + '.tmp')
    Copy-Item -LiteralPath $latest -Destination $pending
    $remoteLatest = Join-Path $publishRoot 'latest.json'
    if (Test-Path -LiteralPath $remoteLatest) { [IO.File]::Replace($pending, $remoteLatest, [System.Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($pending, $remoteLatest) }
}

[pscustomobject]@{
    BundleRoot = $bundle
    Package = $packagePath
    Latest = $latest
    Bootstrap = $bootstrap
    Version = $Version.ToString()
    ProgramFileCount = $programFiles.Count
    PublishedTo = $PublishDirectory
}
