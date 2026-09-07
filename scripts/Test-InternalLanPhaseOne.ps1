[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BundleRoot,
    [int]$RuntimePort = 5097
)

$ErrorActionPreference = 'Stop'
$resolvedBundle = [System.IO.Path]::GetFullPath($BundleRoot)
$centerDirectory = Join-Path $resolvedBundle 'Center'
if (-not (Test-Path -LiteralPath (Join-Path $centerDirectory 'Acquisition.Center.dll') -PathType Leaf)) {
    throw 'BundleRoot中没有可运行的Center发布文件。'
}
if (-not (Test-Path -LiteralPath (Join-Path $centerDirectory 'assets\AgentSetup.exe') -PathType Leaf)) {
    throw 'Center发布目录中没有AgentSetup.exe资产。'
}

$testId = [Guid]::NewGuid().ToString('N')
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('AcquisitionPhaseOneE2E-' + $testId)
New-Item -ItemType Directory -Path $testDirectory -Force:$false | Out-Null
$settingsPath = Join-Path $testDirectory 'center-bootstrap.json'
$centerStdout = Join-Path $testDirectory 'center.stdout.log'
$centerStderr = Join-Path $testDirectory 'center.stderr.log'
$agentStdout = Join-Path $testDirectory 'agent.stdout.log'
$agentStderr = Join-Path $testDirectory 'agent.stderr.log'
$packagePath = Join-Path $testDirectory 'LINE01-PC01-AgentSetup.zip'
$packageRoot = Join-Path $testDirectory 'device-package'
$databaseName = 'AcquisitionCenterE2E_' + $testId
$connection = "Server=(localdb)\MSSQLLocalDB;Database=$databaseName;Integrated Security=true;TrustServerCertificate=true"
$oldSettingsPath = [Environment]::GetEnvironmentVariable('ACQUISITION_CENTER_SETTINGS_PATH')
$oldEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT')
$bootstrapProcess = $null
$centerProcess = $null
$agentProcess = $null

function Wait-ForHttp([string]$uri, [int]$seconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try { return Invoke-RestMethod -Uri $uri -TimeoutSec 2 }
        catch { Start-Sleep -Milliseconds 400 }
    }
    throw "等待服务超时：$uri"
}

function Stop-OwnedProcess($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
}

function Remove-IsolatedDatabase([string]$name) {
    if ($name -notmatch '^AcquisitionCenterE2E_[0-9a-f]{32}$') {
        throw "拒绝清理不符合随机测试库命名规则的数据库：$name"
    }
    $sqlcmd = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $sqlcmd) {
        Write-Warning "未找到sqlcmd，随机测试数据库需要手动清理：$name"
        return
    }

    $query = "IF DB_ID(N'$name') IS NOT NULL BEGIN ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name]; END"
    & $sqlcmd.Source -S '(localdb)\MSSQLLocalDB' -d master -E -b -Q $query
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "随机测试数据库清理失败，需要手动清理：$name"
    }
}

try {
    if (Get-NetTCPConnection -State Listen -LocalPort 5080, $RuntimePort -ErrorAction SilentlyContinue) {
        throw '一期验收所需端口已被占用，脚本不会覆盖现有服务。'
    }

    [Environment]::SetEnvironmentVariable('ACQUISITION_CENTER_SETTINGS_PATH', $settingsPath)
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production')

    $bootstrapProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('Acquisition.Center.dll') -WorkingDirectory $centerDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $centerStdout -RedirectStandardError $centerStderr
    $bootstrapHealth = Wait-ForHttp 'http://127.0.0.1:5080/health' 30
    if ($bootstrapHealth.status -ne 'bootstrap-required') { throw 'Center未进入首次设置模式。' }

    $bootstrapSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $csrf = Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/auth/csrf' -WebSession $bootstrapSession
    $headers = @{ 'X-CSRF-TOKEN' = $csrf.token }
    $draft = @{
        runMode = 'IsolatedTest'
        bindAddress = '127.0.0.1'
        port = $RuntimePort
        advertisedBaseUrl = "http://127.0.0.1:$RuntimePort"
        databaseConnectionString = $connection
        internalLanEnabled = $true
        requireHttps = $false
    } | ConvertTo-Json
    $validation = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/bootstrap/v1/validate' -WebSession $bootstrapSession -Headers $headers -ContentType 'application/json' -Body $draft
    if (-not $validation.isValid) { throw '隔离Center设置校验失败。' }
    $created = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5080/api/bootstrap/v1/complete' -WebSession $bootstrapSession -Headers $headers -ContentType 'application/json' -Body $draft
    $accessCode = [string]$created.deploymentAdminAccessCode
    if ([string]::IsNullOrWhiteSpace($accessCode) -or -not $created.restartRequired) { throw '首次设置未生成有效管理员访问码。' }
    Stop-OwnedProcess $bootstrapProcess
    $bootstrapProcess = $null

    $centerProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('Acquisition.Center.dll') -WorkingDirectory $centerDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $centerStdout -RedirectStandardError $centerStderr
    $runtimeHealth = Wait-ForHttp "http://127.0.0.1:$RuntimePort/health" 30
    if ($runtimeHealth.status -ne 'healthy') { throw 'Center未以最终配置启动。' }

    $operatorSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $runtimeCsrf = Invoke-RestMethod -Uri "http://127.0.0.1:$RuntimePort/api/auth/csrf" -WebSession $operatorSession
    $runtimeHeaders = @{ 'X-CSRF-TOKEN' = $runtimeCsrf.token }
    $login = @{ accessCode = $accessCode } | ConvertTo-Json
    $loginResult = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$RuntimePort/api/auth/operator/login" -WebSession $operatorSession -Headers $runtimeHeaders -ContentType 'application/json' -Body $login
    if (-not $loginResult.authenticated) { throw '管理员登录失败。' }
    $runtimeCsrf = Invoke-RestMethod -Uri "http://127.0.0.1:$RuntimePort/api/auth/csrf" -WebSession $operatorSession
    $runtimeHeaders = @{ 'X-CSRF-TOKEN' = $runtimeCsrf.token }

    $packageRequest = @{
        agentId = 'LINE01-PC01'
        site = '一期隔离厂区'
        building = '测试楼'
        line = '测试线'
        acquisitionAppDirectory = 'D:\采集程序'
        startWinFormsOnLogon = $false
    } | ConvertTo-Json
    Invoke-WebRequest -Method Post -Uri "http://127.0.0.1:$RuntimePort/api/v1/deployments/package" -WebSession $operatorSession -Headers $runtimeHeaders -ContentType 'application/json' -Body $packageRequest -OutFile $packagePath | Out-Null
    Expand-Archive -LiteralPath $packagePath -DestinationPath $packageRoot
    $rootNames = @(Get-ChildItem -LiteralPath $packageRoot | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedRootNames = @('AgentPayload', 'AgentSetup.exe', 'deployment.json', 'payload.manifest.json')
    if (@(Compare-Object $expectedRootNames $rootNames).Count -ne 0) { throw 'Center签发的设备包根目录不符合契约。' }
    $deployment = Get-Content -LiteralPath (Join-Path $packageRoot 'deployment.json') -Raw -Encoding utf8 | ConvertFrom-Json
    $payloadManifest = Get-Content -LiteralPath (Join-Path $packageRoot 'payload.manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if (@($payloadManifest.files | Where-Object { $_.path -eq 'AgentSetup.exe' }).Count -ne 1) { throw 'Center设备包未清单化AgentSetup.exe。' }
    if (@($payloadManifest.files | Where-Object { $_.path -like '*appsettings*' }).Count -ne 0) { throw 'Center设备包包含运行时appsettings。' }

    $runtimeDirectory = Join-Path $packageRoot 'AgentPayload'
    $agentDatabasePath = Join-Path $testDirectory 'agent.db'
    $identityPath = Join-Path $testDirectory 'identity.bin'
    $enrollmentPath = Join-Path $testDirectory 'enrollment.json'
    $installStatusPath = Join-Path $testDirectory 'install-status.json'
    $agentSettingsPath = Join-Path $runtimeDirectory 'appsettings.json'
    $agentSettings = @{ Agent = @{
        agentId = [string]$deployment.agentId
        centerBaseUrl = [string]$deployment.centerBaseUrl
        registrationKey = ''
        localDatabasePath = $agentDatabasePath
        legacyDatabasePath = [string]$deployment.legacyDatabasePath
        legacyConfigPath = [string]$deployment.legacyConfigPath
        legacyExecutablePath = [string]$deployment.legacyExecutablePath
        identityPath = $identityPath
        enrollmentTokenPath = $enrollmentPath
        installStatusPath = $installStatusPath
        site = [string]$deployment.site
        heartbeatSeconds = 5
    }} | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($agentSettingsPath, $agentSettings, [System.Text.UTF8Encoding]::new($false))
    if ((Get-Content -LiteralPath $agentSettingsPath -Raw -Encoding utf8).Contains([string]$deployment.enrollmentToken)) { throw 'Agent运行时配置泄露了一次性注册令牌。' }
    $enrollment = @{ agentId = [string]$deployment.agentId; enrollmentToken = [string]$deployment.enrollmentToken } | ConvertTo-Json
    [System.IO.File]::WriteAllText($enrollmentPath, $enrollment, [System.Text.UTF8Encoding]::new($false))

    $agentProcess = Start-Process -FilePath (Join-Path $runtimeDirectory 'Acquisition.Agent.exe') -WorkingDirectory $runtimeDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $agentStdout -RedirectStandardError $agentStderr
    $deploymentState = $null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(35)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 1
        $deployments = Invoke-RestMethod -Uri "http://127.0.0.1:$RuntimePort/api/v1/deployments" -WebSession $operatorSession
        $deploymentState = @($deployments | Where-Object { $_.agentId -eq 'LINE01-PC01' }) | Select-Object -First 1
        if ($null -ne $deploymentState -and $null -ne $deploymentState.enrolledAtUtc -and $null -ne $deploymentState.lastHeartbeatAtUtc) { break }
    }
    if ($null -eq $deploymentState -or $null -eq $deploymentState.enrolledAtUtc -or $null -eq $deploymentState.lastHeartbeatAtUtc) { throw 'Agent未完成注册和首次心跳。' }
    if (Test-Path -LiteralPath $enrollmentPath) { throw 'Agent注册成功后没有消费一次性注册文件。' }

    [pscustomobject]@{
        BootstrapMode = $bootstrapHealth.status
        RuntimeMode = $runtimeHealth.status
        DevicePackageRoot = $rootNames -join ', '
        PayloadManifestEntryCount = @($payloadManifest.files).Count
        EnrollmentConsumed = -not (Test-Path -LiteralPath $enrollmentPath)
        CenterRecordedEnrollment = $null -ne $deploymentState.enrolledAtUtc
        CenterRecordedHeartbeat = $null -ne $deploymentState.lastHeartbeatAtUtc
        DeploymentState = [string]$deploymentState.state
        EvidenceDirectory = $testDirectory
    } | Format-List
}
finally {
    Stop-OwnedProcess $agentProcess
    Stop-OwnedProcess $centerProcess
    Stop-OwnedProcess $bootstrapProcess
    Remove-IsolatedDatabase $databaseName
    if (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath }
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath }
    $deploymentFile = Join-Path $packageRoot 'deployment.json'
    if (Test-Path -LiteralPath $deploymentFile) { Remove-Item -LiteralPath $deploymentFile }
    $enrollmentFile = Join-Path $testDirectory 'enrollment.json'
    if (Test-Path -LiteralPath $enrollmentFile) { Remove-Item -LiteralPath $enrollmentFile }
    $identityFile = Join-Path $testDirectory 'identity.bin'
    if (Test-Path -LiteralPath $identityFile) { Remove-Item -LiteralPath $identityFile }
    [Environment]::SetEnvironmentVariable('ACQUISITION_CENTER_SETTINGS_PATH', $oldSettingsPath)
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $oldEnvironment)
}
