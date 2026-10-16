[CmdletBinding()]
param([string]$UpdaterAssemblyPath, [string]$ReportPath)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $UpdaterAssemblyPath) { $UpdaterAssemblyPath = Join-Path $repoRoot 'Acquisition.Updater\bin\Release\net462\Updater.exe' }
if (-not $ReportPath) { $ReportPath = Join-Path $repoRoot 'outputs\winforms-updater\verification-result.json' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AcquisitionUpdaterE2E-' + [guid]::NewGuid().ToString('N'))
$build = Join-Path $testRoot 'build'
$device = Join-Path $testRoot 'device'
$share = Join-Path $testRoot 'share'
$bundles = Join-Path $testRoot 'bundles'
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert-Equal($Expected, $Actual, [string]$Name) {
    if ($Expected -ne $Actual) { throw "${Name}: expected '$Expected', actual '$Actual'" }
    $checks.Add($Name)
}
try {
    New-Item -ItemType Directory -Path $build | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $build 'x64') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $build 'Resources') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $build 'Data') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $device 'Data') | Out-Null
    [IO.File]::WriteAllText((Join-Path $build '文件数据采集系统.exe'), 'program-v1')
    $firstNotes = "## 1.0.0.1 - 2026-10-15`r`n- 共享目录更新。"
    [IO.File]::WriteAllText((Join-Path $build 'Resources\CHANGELOG.md'), $firstNotes)
    [IO.File]::WriteAllText((Join-Path $build 'x64\SQLite.Interop.dll'), 'native-dependency')
    [IO.File]::WriteAllText((Join-Path $build '文件数据采集系统.exe.config'), '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.6.2" /></startup></configuration>')
    [IO.File]::WriteAllText((Join-Path $build 'appsettings.json'), 'must-not-publish')
    [IO.File]::WriteAllText((Join-Path $build 'Data\采集记录.db'), 'must-not-publish')
    [IO.File]::WriteAllText((Join-Path $device '文件数据采集系统.exe'), 'old-program')
    [IO.File]::WriteAllText((Join-Path $device 'appsettings.json'), 'device-settings')
    [IO.File]::WriteAllText((Join-Path $device 'Data\采集记录.db'), 'device-data')
    $first = & (Join-Path $PSScriptRoot 'Publish-WinFormsUpdate.ps1') -Version '1.0.0.1' -PublishDirectory $share -OutputRoot $bundles -BuildOutputDirectory $build -UpdaterBuildDirectory (Split-Path -Parent $UpdaterAssemblyPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($first.Package)
    try {
        Assert-Equal 4 $zip.Entries.Count 'package contains program dependencies and release history'
        Assert-Equal 'Resources/CHANGELOG.md' ($zip.GetEntry('Resources/CHANGELOG.md').FullName) 'release history is included in package'
        Assert-Equal $null ($zip.GetEntry('appsettings.json')) 'settings excluded from package'
        Assert-Equal $null ($zip.GetEntry('Data/采集记录.db')) 'database excluded from package'
    }
    finally { $zip.Dispose() }
    $install = Join-Path $first.Bootstrap 'Install-WinFormsUpdater.ps1'
    & $install -ApplicationDirectory $device -SourceDirectory $share -SkipDesktopShortcut -SkipStartupRegistration -WhatIf | Out-Null
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $device 'Updater.exe')) 'WhatIf does not install updater'
    & $install -ApplicationDirectory $device -SourceDirectory $share -SkipDesktopShortcut -SkipStartupRegistration | Out-Null
    Assert-Equal $share ((Get-Content -LiteralPath (Join-Path $device 'updater.json') -Raw | ConvertFrom-Json).sourceDirectory) 'installer writes actual shared source'
    [Reflection.Assembly]::LoadFrom($UpdaterAssemblyPath) | Out-Null
    $engine = New-Object Acquisition.Updater.UpdateService($device, $share)
    Assert-Equal $true ($engine.ApplyLatest().Updated) 'first upgrade succeeds'
    Assert-Equal 'program-v1' ([IO.File]::ReadAllText((Join-Path $device '文件数据采集系统.exe'))) 'first program deployed'
    Assert-Equal $firstNotes ([IO.File]::ReadAllText((Join-Path $device 'Resources\CHANGELOG.md'))) 'first release history deployed by existing updater'
    Assert-Equal 'device-settings' ([IO.File]::ReadAllText((Join-Path $device 'appsettings.json'))) 'device settings retained'
    Assert-Equal 'device-data' ([IO.File]::ReadAllText((Join-Path $device 'Data\采集记录.db'))) 'device database retained'
    Assert-Equal $false ($engine.ApplyLatest().Updated) 'same release skips update'
    [IO.File]::WriteAllText((Join-Path $build '文件数据采集系统.exe'), 'program-v2')
    $secondNotes = "## 1.0.0.2 - 2026-10-16`r`n- 更新提示。`r`n`r`n" + $firstNotes
    [IO.File]::WriteAllText((Join-Path $build 'Resources\CHANGELOG.md'), $secondNotes)
    $second = & (Join-Path $PSScriptRoot 'Publish-WinFormsUpdate.ps1') -Version '1.0.0.2' -PublishDirectory $share -OutputRoot $bundles -BuildOutputDirectory $build -UpdaterBuildDirectory (Split-Path -Parent $UpdaterAssemblyPath)
    Assert-Equal $true ($engine.ApplyLatest().Updated) 'second upgrade succeeds'
    Assert-Equal 'program-v2' ([IO.File]::ReadAllText((Join-Path $device '文件数据采集系统.exe'))) 'second program deployed'
    Assert-Equal $secondNotes ([IO.File]::ReadAllText((Join-Path $device 'Resources\CHANGELOG.md'))) 'second release retains full history'
    Assert-Equal '1.0.0.2' ([IO.File]::ReadAllText((Join-Path $device 'installed-version.txt'))) 'installed version advances after replacement'
    Assert-Equal '1.0.0.2' ((Get-Content -LiteralPath (Join-Path $share 'latest.json') -Raw | ConvertFrom-Json).version) 'share latest atomically advances'
    $offline = New-Object Acquisition.Updater.UpdateService($device, (Join-Path $testRoot 'unavailable-share'))
    Assert-Equal $false ($offline.ApplyLatest().Updated) 'unavailable share keeps local version'
    Assert-Equal $secondNotes ([IO.File]::ReadAllText((Join-Path $device 'Resources\CHANGELOG.md'))) 'offline startup retains local release history'

    # 使用无数据库、只写启动标记的临时 EXE，验证真正的更新器入口和旧进程等待。
    $smokeExe = Join-Path $testRoot 'SmokeApp.exe'
    Add-Type -TypeDefinition 'public static class SmokeApp { public static void Main() { System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "launcher-started.txt"), System.Environment.CurrentDirectory); } }' -OutputAssembly $smokeExe -OutputType WindowsApplication
    [IO.File]::Copy($smokeExe, (Join-Path $device '文件数据采集系统.exe'), $true)
    $stub = Start-Process -FilePath (Join-Path $device '文件数据采集系统.exe') -WorkingDirectory $device -WindowStyle Hidden -PassThru
    if (-not $stub.WaitForExit(10000)) { $stub.Kill(); throw 'Isolated application stub timed out.' }
    Assert-Equal $device ([IO.File]::ReadAllText((Join-Path $device 'launcher-started.txt'))) 'isolated application stub works'
    [IO.File]::Delete((Join-Path $device 'launcher-started.txt'))
    $previous = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile -Command "Start-Sleep -Seconds 2"' -WindowStyle Hidden -PassThru
    $launcher = Start-Process -FilePath (Join-Path $device 'Updater.exe') -ArgumentList @('--wait-for-pid', $previous.Id) -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 300
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $device 'launcher-started.txt')) 'launcher waits for previous process'
    if (-not $launcher.WaitForExit(20000)) { $launcher.Kill(); throw 'Isolated updater launcher timed out.' }
    Assert-Equal 0 $launcher.ExitCode 'updater process exits successfully'
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath (Join-Path $device 'launcher-started.txt')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert-Equal $device ([IO.File]::ReadAllText((Join-Path $device 'launcher-started.txt'))) 'launcher starts local application with correct working directory'
    New-Item -ItemType Directory -Path (Split-Path -Parent $ReportPath) -Force | Out-Null
    [ordered]@{ Passed = $checks.Count; Checks = @($checks); DatabaseConnections = 0; ProductionDeployment = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Output "Passed $($checks.Count) checks. Report: $ReportPath"
}
finally {
    # 唯一随机目录由本次测试创建；逐个删除其文件，不使用目录递归删除。
    if (Test-Path -LiteralPath $testRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $testRoot -File -Recurse) { [IO.File]::Delete($file.FullName) }
    }
}
