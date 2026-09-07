# 文件数据采集集中管理平台（一期内网版）

一期将设备端的 WinForms 采集程序保留在原电脑上，同时增加 Center 和 Agent：Center 统一管理设备、配置版本和诊断；Agent 作为 Windows 服务负责心跳、离线补传和与本机 WinForms 的桥接。

## 一期操作入口

不需要手改 Agent 的 `appsettings.json`、注册码或配置路径。正常操作只有三步：

1. 发布并在 Center 本机完成首次设置。
2. 管理员在 Center 网页填写一台设备的现有采集目录，下载该设备专用 ZIP。
3. 在目标电脑以管理员身份运行 ZIP 中的 `AgentSetup.exe`，等待 Center 收到首次心跳。

设备 ZIP 是一次性注册包：其中的 enrollment token 只可使用一次。Agent 注册成功后会删除 token 文件，之后改用设备独立凭据；不要复制 ZIP 到其他电脑或长期保留。

## 构建发布包

在源码根目录执行：

```powershell
.\scripts\Publish-InternalLanBundle.ps1 -Configuration Release
```

脚本每次创建新的带时间戳输出目录，不覆盖旧包。输出包含：

- `Center`：自包含 Center 发布目录，内含 `assets\AgentSetup.exe` 和 `assets\AgentPayload`。
- `DevicePackageTemplate`：用于核对 ZIP 根结构的无凭据模板，不可直接安装。

Center 实际签发的设备 ZIP 根目录固定为：

```text
AgentSetup.exe
deployment.json
payload.manifest.json
AgentPayload\...
```

`payload.manifest.json` 会校验安装器和 Agent payload 的长度与 SHA-256。运行时 `appsettings.json` 由安装器在版本目录生成，不放入 ZIP payload；一次性 token 单独写入 `%ProgramData%\AcquisitionAgent\enrollment.json` 并限制为 SYSTEM 与 Administrators 可读。

## Center 首次设置

1. 在固定的 Center 服务器解压 `Center` 目录并运行 `Acquisition.Center.exe`。
2. 未初始化时，它只监听本机 `http://127.0.0.1:5080/setup.html`，不向内网暴露设置接口。
3. 在设置页选择运行模式、监听地址/端口、Agent 可访问地址、正式 SQL Server 连接和 HTTPS 要求。
4. 完成设置后立即保存页面只显示一次的管理员访问码；它不会保存到浏览器存储，也不会写入配置文件明文。
5. 按页面提示重启 Center，再从配置的地址登录。

正式环境必须使用 SQL Server，不能使用 LocalDB。Center 的监听地址和 Agent 访问地址分别配置：监听可为服务器网卡，Agent 地址必须是设备可访问的具体 URL，不能填写 `0.0.0.0`。

当前 HTTP 仅用于隔离联调。设备凭据通过 HTTP 传输的正式部署存在风险；扩展现场设备前应配置证书、将 Center 设置为要求 HTTPS，并完成防火墙与证书验收。

## 安装一台设备

在 Center 的“部署向导”中填写这台电脑真实存在的：设备编号、厂区/楼层/产线和 WinForms 采集程序绝对目录，例如 `D:\采集程序`。不要填写网络共享路径。

目标电脑上运行 `AgentSetup.exe` 前，安装器会检查该目录内的：

- `文件数据采集系统.exe`
- `Data\采集记录.db`
- `config.json`

检查通过后，安装器会：

- 将 Agent 安装到版本目录；不会覆盖或删除原 WinForms 采集程序。
- 设置 `AcquisitionAgent` Windows 服务为延迟自动启动。
- 让 Agent 与 WinForms 使用同一个 `ACQUISITION_AGENT_DB` 路径。
- 写入脱离 token 的运行时配置，并创建受保护的一次性注册文件。

安装器不会代管现有 WinForms 的“登录启动”配置；请保留设备当前的启动方式。Center 中的部署状态以“已注册”和“首次心跳”为准，下载 ZIP 或创建部署记录都不代表安装成功。

## 集中配置与回滚

“配置中心”只维护当前 WinForms 已支持的机台字段：`id`、`name`、`monitorPath`、`successPath`、`errorPath`。机台 ID 仅允许 1～6，所有目录必须是 Windows 本地绝对路径。

操作顺序：明确勾选目标 Agent → 修改机台路径 → 预览差异 → 创建发布任务。发布请求带新的请求 ID，预览一旦变更即失效。历史版本不可覆盖；回滚会创建一个更高版本。

“任务已创建”不等于“已生效”。只有 Agent 回执显示 `Applied` 且包含匹配的 `effectiveVersion/effectiveSha256` 才表示 WinForms 已确认重载；若 10 秒内未确认，Agent 会恢复旧文件并回报失败或回滚。

## 图片文件名采集

WinForms 字段映射支持“图片文件名”模式，首版支持 `.jpg`、`.jpeg`、`.png`、`.bmp`，不解析图片内容。配置时选择图片样本、目标模型、共享目录和用于保存共享地址的 `string` 字段，再将完整文件名、无扩展名文件名或 `[]`/`【】` 片段映射到业务字段。

采集成功时，图片按 `共享目录\Machine-机台ID\SHA256.扩展名` 保存；相同文件重试会复用已校验的共享图片，不覆盖不同内容。数据库事务提交后才会把源文件移入成功目录。共享目录建议使用 UNC 路径，并用实际运行 WinForms 的 Windows 账户验证读写权限。若业务上要求严格防止重复入库，还应在目标表为稳定业务键或图片路径增加唯一约束。

## 验收与日常诊断

Center 的“统一诊断”汇总 Agent 服务状态、本地数据库、WinForms 文件/进程、心跳时间和最终有效配置。未知状态不会显示为健康；导出的诊断 ZIP 会脱敏路径、hash、token、凭据和密钥。

可运行隔离闭环验收（只使用随机 LocalDB、127.0.0.1 和临时目录）：

```powershell
.\scripts\Test-InternalLanPhaseOne.ps1 -BundleRoot 'F:\...\outputs\internal-lan-bundles\internal-lan-时间戳\'
```

它验证首次设置、管理员 Cookie 登录、Center 设备 ZIP、一次性注册、设备凭据、首次心跳和 ZIP 契约；不安装真实 Windows 服务，也不验证 WinForms 热重载。脚本结束时会删除随机 LocalDB；为便于复核且避免批量删除，会输出并保留不含注册令牌的临时证据目录。

## 开发验证

```powershell
dotnet test .\tests\Acquisition.Contracts.Tests\Acquisition.Contracts.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Agent.Tests\Acquisition.Agent.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Center.Tests\Acquisition.Center.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Agent.Setup.Tests\Acquisition.Agent.Setup.Tests.csproj -c Release --no-restore
node --test .\tests\Acquisition.Center.Frontend.Tests\app.logic.test.cjs
dotnet build .\MachineDataAcquisitionSystem.sln -c Release --no-restore
```

## 尚待现场验证

- 真实 UAC、`sc.exe` 服务安装、失败回退、延迟启动和开机重启。
- Center bootstrap 文件 ACL 已在代码中收紧为当前安装账户、SYSTEM 与 Administrators；仍需现场核对安装账户，且 `%ProgramData%\AcquisitionAgent` 的目录级 ACL 尚待验收。
- 远端设备上的 WinForms bridge、热重载确认、文件采集、断网补传与恢复。
- HTTPS 证书、生产 SQL Server、内网防火墙、备份和上线演练。
- 当前数据库初始化采用 `EnsureCreated` 加幂等 DDL，尚未演进为完整 EF Migration 流程。
