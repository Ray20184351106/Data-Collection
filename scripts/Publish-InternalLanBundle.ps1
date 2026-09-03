[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\outputs\internal-lan-bundles'),
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$bundleName = 'internal-lan-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$bundleRoot = Join-Path $resolvedOutputRoot $bundleName
$centerOutput = Join-Path $bundleRoot 'Center'
$centerAssets = Join-Path $centerOutput 'assets'
$centerAgentPayload = Join-Path $centerAssets 'AgentPayload'
$deviceTemplate = Join-Path $bundleRoot 'DevicePackageTemplate'
$agentPayload = Join-Path $deviceTemplate 'AgentPayload'

if (Test-Path -LiteralPath $bundleRoot) {
    throw "输出目录已存在，脚本不会覆盖：$bundleRoot"
}

New-Item -ItemType Directory -Path $centerOutput -Force:$false | Out-Null
New-Item -ItemType Directory -Path $agentPayload -Force:$false | Out-Null

function Invoke-SelfContainedPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Project,
        [Parameter(Mandatory)]
        [string]$Destination,
        [bool]$SingleFile = $false
    )

    $arguments = @(
        'publish',
        $Project,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-o', $Destination,
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        ('-p:PublishSingleFile={0}' -f $SingleFile.ToString().ToLowerInvariant())
    )
    if ($SingleFile) {
        $arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
        $arguments += '-p:EnableCompressionInSingleFile=true'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish失败：$Project，退出码$LASTEXITCODE"
    }
}

Invoke-SelfContainedPublish `
    -Project (Join-Path $repoRoot 'Acquisition.Center\Acquisition.Center.csproj') `
    -Destination $centerOutput

Invoke-SelfContainedPublish `
    -Project (Join-Path $repoRoot 'Acquisition.Agent\Acquisition.Agent.csproj') `
    -Destination $agentPayload

$agentSettings = Join-Path $agentPayload 'appsettings.json'
if (Test-Path -LiteralPath $agentSettings -PathType Leaf) {
    Remove-Item -LiteralPath $agentSettings
}
$agentDevelopmentSettings = Join-Path $agentPayload 'appsettings.Development.json'
if (Test-Path -LiteralPath $agentDevelopmentSettings -PathType Leaf) {
    Remove-Item -LiteralPath $agentDevelopmentSettings
}

Invoke-SelfContainedPublish `
    -Project (Join-Path $repoRoot 'Acquisition.Agent.Setup\Acquisition.Agent.Setup.csproj') `
    -Destination $deviceTemplate `
    -SingleFile $true

$setupExecutable = Join-Path $deviceTemplate 'AgentSetup.exe'
if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) {
    throw "Setup自包含发布未生成AgentSetup.exe：$setupExecutable"
}

$unexpectedSetupFiles = @(
    Get-ChildItem -LiteralPath $deviceTemplate -File |
        Where-Object { $_.Name -ne 'AgentSetup.exe' }
)
if ($unexpectedSetupFiles.Count -gt 0) {
    $names = ($unexpectedSetupFiles.Name -join ', ')
    throw "Setup发布产生未预期的根文件，未继续组包：$names"
}

New-Item -ItemType Directory -Path $centerAssets -Force:$false | Out-Null
New-Item -ItemType Directory -Path $centerAgentPayload -Force:$false | Out-Null
Copy-Item -LiteralPath $setupExecutable -Destination (Join-Path $centerAssets 'AgentSetup.exe') -ErrorAction Stop
Get-ChildItem -LiteralPath $agentPayload -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $centerAgentPayload -Recurse -ErrorAction Stop
}

$payloadFiles = @(
    Get-ChildItem -LiteralPath $agentPayload -File -Recurse |
        Sort-Object FullName
)
if ($payloadFiles.Count -eq 0) {
    throw 'AgentPayload为空，未生成设备包模板。'
}

$payloadEntries = @(
    [ordered]@{
        path = 'AgentSetup.exe'
        sha256 = (Get-FileHash -LiteralPath $setupExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        length = (Get-Item -LiteralPath $setupExecutable).Length
    }
    foreach ($file in $payloadFiles) {
        $relative = [System.IO.Path]::GetRelativePath($deviceTemplate, $file.FullName).Replace('\', '/')
        [ordered]@{
            path = $relative
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            length = $file.Length
        }
    }
)

$payloadManifest = [ordered]@{
    schemaVersion = 1
    files = $payloadEntries
}
$payloadManifestPath = Join-Path $deviceTemplate 'payload.manifest.json'
$payloadManifestJson = $payloadManifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($payloadManifestPath, $payloadManifestJson, [System.Text.UTF8Encoding]::new($false))

# 这是故意不可安装的模板值。Center生成设备包时必须原子替换整个deployment.json，
# enrollmentToken不能写入源码、构建日志或持久化发布目录。
$deploymentTemplate = [ordered]@{
    schemaVersion = 1
    agentId = 'REPLACE-WITH-UNIQUE-AGENT-ID'
    deploymentId = '00000000-0000-0000-0000-000000000000'
    centerBaseUrl = 'http://REPLACE-WITH-CENTER:5080'
    enrollmentToken = 'REPLACE-AT-DEVICE-PACKAGE-GENERATION'
    enrollmentExpiresAtUtc = '1970-01-01T00:00:00+00:00'
    site = ''
    building = ''
    line = ''
    acquisitionAppDirectory = 'D:\REPLACE-WITH-ACQUISITION-APP'
    localDatabasePath = 'C:\ProgramData\AcquisitionAgent\agent.db'
    legacyDatabasePath = 'D:\REPLACE-WITH-ACQUISITION-APP\Data\采集记录.db'
    legacyConfigPath = 'D:\REPLACE-WITH-ACQUISITION-APP\config.json'
    legacyExecutablePath = 'D:\REPLACE-WITH-ACQUISITION-APP\文件数据采集系统.exe'
    installDirectory = 'C:\Program Files\AcquisitionAgent'
    startWinFormsOnLogon = $false
    agentVersion = '1.0.0'
}
$deploymentPath = Join-Path $deviceTemplate 'deployment.json'
$deploymentJson = $deploymentTemplate | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($deploymentPath, $deploymentJson, [System.Text.UTF8Encoding]::new($false))

$rootEntries = @(Get-ChildItem -LiteralPath $deviceTemplate | Select-Object -ExpandProperty Name)
$expectedRootEntries = @('AgentPayload', 'AgentSetup.exe', 'deployment.json', 'payload.manifest.json')
$unexpectedRootEntries = @($rootEntries | Where-Object { $_ -notin $expectedRootEntries })
$missingRootEntries = @($expectedRootEntries | Where-Object { $_ -notin $rootEntries })
if ($unexpectedRootEntries.Count -gt 0 -or $missingRootEntries.Count -gt 0) {
    throw "设备模板根结构不符合约定。缺少：$($missingRootEntries -join ', ')；多出：$($unexpectedRootEntries -join ', ')"
}

[pscustomobject]@{
    BundleRoot = $bundleRoot
    Center = $centerOutput
    DevicePackageTemplate = $deviceTemplate
    AgentPayloadFileCount = $payloadFiles.Count
    CodeSigned = $false
} | Format-List
