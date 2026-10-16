[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$ApplicationDirectory,
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [string]$UpdaterPayloadDirectory = $PSScriptRoot,
    [switch]$SkipDesktopShortcut,
    [switch]$SkipStartupRegistration
)

$ErrorActionPreference = 'Stop'
$applicationRoot = [IO.Path]::GetFullPath($ApplicationDirectory)
$applicationPath = Join-Path $applicationRoot '文件数据采集系统.exe'
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) { throw "未找到现有采集程序：$applicationPath" }
$updaterSource = Join-Path $UpdaterPayloadDirectory 'Updater.exe'
if (-not (Test-Path -LiteralPath $updaterSource -PathType Leaf)) { throw "请使用发布包中的首次接入目录运行脚本，缺少：$updaterSource" }
$updaterTarget = Join-Path $applicationRoot 'Updater.exe'
if ($PSCmdlet.ShouldProcess($applicationRoot, '接入共享目录更新器；保留现场配置和数据库')) {
    Copy-Item -LiteralPath $updaterSource -Destination $updaterTarget -Force
    $configSource = Join-Path $UpdaterPayloadDirectory 'Updater.exe.config'
    if (Test-Path -LiteralPath $configSource) { Copy-Item -LiteralPath $configSource -Destination (Join-Path $applicationRoot 'Updater.exe.config') -Force }
    $settings = @{ sourceDirectory = $SourceDirectory } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $applicationRoot 'updater.json'), $settings, [Text.UTF8Encoding]::new($false))

    # 仅迁移当前用户原本已启用且指向本目录的登录启动项，不主动开启。
    if (-not $SkipStartupRegistration) {
        $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
        if ($null -ne $runKey) {
            try {
                $existing = [string]$runKey.GetValue('文件数据采集系统')
                if ($existing.Trim('"') -eq $applicationPath) { $runKey.SetValue('文件数据采集系统', '"' + $updaterTarget + '"') }
            }
            finally { $runKey.Dispose() }
        }
    }
    if (-not $SkipDesktopShortcut) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '文件数据采集系统.lnk'))
        $shortcut.TargetPath = $updaterTarget
        $shortcut.WorkingDirectory = $applicationRoot
        $shortcut.IconLocation = $applicationPath + ',0'
        $shortcut.Save()
    }
}
[pscustomobject]@{ ApplicationDirectory = $applicationRoot; SourceDirectory = $SourceDirectory; Updater = $updaterTarget }
