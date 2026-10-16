# 共享目录自动更新

## 范围

设备保持原有采集目录。新增独立 .NET Framework 4.6.2 更新器，启动时读取共享目录版本；有新版才更新。已运行的程序需要先从托盘真正退出，第一版不安排定时重启。

服务器目录：

```text
共享发布目录/
  latest.json
  packages/1.0.0.1.zip
```

latest.json 使用 `version` 和相对 `package` 路径。客户端 updater.json 使用 `sourceDirectory`（实际 UNC 地址）。程序版本记录在 installed-version.txt，未接入过的目录视为 0.0.0.0。

## 版本显示与更新日志

主窗口标题和“帮助”页显示 installed-version.txt 中的实际安装版本；没有安装版本记录时显示程序集版本。顶部“帮助”右侧的“更新日志”可查看本地完整发布历史，左侧选择版本及日期，右侧阅读更新内容。

每个版本首次启动时，主界面初始化和已配置机台默认启动后会自动打开一次更新说明窗口。窗口为非模态，主界面和机台采集可继续运行；已展示版本保存在 UpdateCache/last-notified-version.txt，同一版本下次启动不重复提示。首次使用此功能时也会提示一次，手动查看日志不改变提示记录。

仓库根目录 CHANGELOG.md 是更新说明的维护入口。每次发布前，把“未发布”章节改为实际版本与日期（如 `## 1.0.0.2 - 2026-10-16`），填写面向操作人员的更新内容并保留历史章节，然后沿用下方原有发布命令。只有含版本号和日期的章节进入发布历史；当前版本没有对应说明时显示“该版本暂无更新说明”。

构建会将该文件复制为 Resources/CHANGELOG.md，现有发布脚本及更新器即可随程序分发，并纳入原有备份与失败恢复流程；断网时仍读取本地日志。初始历史仅记录已核实的 1.0.0.1 发布，未根据提交记录推算旧版版本号。

## 文件边界

更新包只含发布输出里的 EXE、DLL、EXE.config 和运行依赖/资源子目录。保留 appsettings.json、config.json、updater.json、installed-version.txt、Data、GeneratedModels、Logs 和现场采集文件。更新器自身首次安装，正常程序包不替换 Updater.exe。

更新包先复制到本地 UpdateCache/package.zip，再展开到 UpdateCache/staging，按本次包中文件清单替换。保留 UpdateCache/previous.zip 作为本次覆盖前的程序文件备份。替换失败恢复本次涉及的文件；不修改数据库，不承诺旧程序可以兼容新版数据库结构。

共享目录不可读或更新包尚不可读时，继续启动本地程序；包损坏或本地替换失败时显示错误，不启动不确定状态的程序。

## 操作命令

### 开发电脑：先生成本地发布包

在仓库根目录运行（Windows PowerShell 5.1 / PowerShell 7 均可）：

```powershell
.\scripts\Publish-WinFormsUpdate.ps1 -Version 1.0.0.1
```

输出到 `outputs\winforms-updates\版本号-时间\`，包含 `latest.json`、`packages\版本号.zip`、`首次接入\` 和隔离构建输出。脚本不会将调试目录或现场配置直接打包。发布版本独立于 AssemblyVersion，每次发布递增 `-Version`。

### 本机先使用项目目录发布

本机发布源固定为项目下 `outputs\winforms-publish`，本机更新器接入 `MachineDataAcquisitionSystem\bin\Debug`。

后续在项目根目录执行下面一条命令即可生成并发布下一个版本：

```powershell
.\scripts\Publish-LocalWinFormsUpdate.ps1 -Version 1.0.0.2
```

首次本地版本为 1.0.0.1，后续每次递增版本号。桌面“文件数据采集系统”快捷方式指向 Debug 目录的 Updater.exe；直接从 Visual Studio 调试或直接打开主程序 EXE 不经过更新器。

这里使用本机磁盘路径，只供本机验证。其他电脑接入时，需要共享发布目录，并把设备 updater.json 的 sourceDirectory 改成它们可以读取的 UNC 地址。

### 发布到实际共享目录

把下面示例替换成真实 UNC 地址，在开发电脑运行：

```powershell
.\scripts\Publish-WinFormsUpdate.ps1 -Version 1.0.0.2 -PublishDirectory '\\服务器\软件发布\文件采集'
```

脚本先上传不可覆盖的版本 ZIP，完成后原子替换 latest.json。同版本包已存在时请换更高版本，避免设备取到同版本不同内容。不要用开发输出目录或设备运行目录作为 `-BuildOutputDirectory`；该参数仅供受控构建输出/隔离测试使用。

### 设备：首次接入一次

1. 使用实际运行采集程序的 Windows 账号，确认能读取共享目录 `latest.json` 和 `packages`。
2. 从托盘选择“退出程序”，等待采集程序退出。
3. 将发布输出中的 `首次接入` 文件夹复制到设备任意临时位置，在其中打开 PowerShell。
4. 执行以下命令，替换实际程序目录和共享目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-WinFormsUpdater.ps1 -ApplicationDirectory 'D:\采集程序' -SourceDirectory '\\服务器\软件发布\文件采集'
```

脚本只复制更新器和写入 updater.json，创建当前用户桌面快捷方式；当前用户原已启用且指向本目录的登录启动项会改为 Updater.exe，未启用的不会主动开启。可加 `-WhatIf` 先查看操作；`-SkipDesktopShortcut` / `-SkipStartupRegistration` 可跳过对应接入步骤。

5. 双击新桌面快捷方式，或运行原程序目录中的 Updater.exe。

以后仍从这个快捷方式启动；直接打开采集主程序 EXE 会绕过版本检查。程序内部保存配置后的重启会正常停止采集、刷新剩余数据，再通过更新器启动。若目录里尚无更新器，旧部署继续直接启动。

更新器执行期间占用 UpdateCache/update.lock；一个设备目录只进行一次更新。包损坏或替换失败会显示错误；若提示自动恢复未完成，保留 UpdateCache/previous.zip，并在恢复程序文件前不要重复尝试更新。数据库结构兼容性需另行验收。

### 本地验证

```powershell
dotnet test .\tests\Acquisition.Updater.Tests\Acquisition.Updater.Tests.csproj
dotnet build .\Acquisition.Updater\Acquisition.Updater.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-WinFormsUpdater.ps1
```

闭环脚本只使用随机临时目录、假程序文件和文本占位数据，不连接数据库、不安装服务、不更改真实启动项/快捷方式。报告位于 `outputs\winforms-updater\verification-result.json`。保留最近更新包、覆盖前备份及 staging 用于复现和人工恢复；不会清空目录或递归删除。

## 现场验收

先一台设备，分别确认：新版替换、配置/数据库不变、子目录依赖存在、共享断开仍能本地启动、登录启动走更新器、重启后仅自动启动已勾选机台。更新中会短暂停采，现场需核对目录积压文件及入库结果。

## 本次验证记录（2026-10-15）

- 更新器全套 9 项测试、相关 WinForms 启动/设置/自启动 23 项测试通过。
- Windows PowerShell 5.1 隔离闭环 19 项检查通过，包含两次发布、接入脚本预演、现场文件保留、离线回退和真实更新器进程启动测试 EXE。
- Release 构建通过；原项目 NPOI EULA 提示仍在，未自动接受许可。
- 真实共享权限、当前用户登录启动和设备采集恢复尚待现场首台验收，测试没有连接业务数据库。
- 本地包基于当前工作区生成，包含原有未提交功能改动；未上传共享目录或部署到现场。

## 更新日志功能验证（2026-10-16）

- 主程序全套 291 项、更新器全套 9 项测试通过，覆盖版本读取、历史排序、首次提示、同版不重复提示、非模态窗口以及更新日志备份和失败回滚。
- 隔离更新闭环 23 项检查通过，包含日志随包发布、连续升级保留完整历史和离线读取本地日志；未连接业务数据库。
- Release 构建成功，输出的 Resources/CHANGELOG.md 与仓库根目录源文件哈希一致；实际窗口已核对中文内容和版本选择。
- 本次仅完成源码和隔离验证，尚未发布；机台默认启动和配置保存后重启的现场表现仍需随下一版本验收。
