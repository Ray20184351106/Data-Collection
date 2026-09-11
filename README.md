> 特别感谢禤总——本项目的原始开发人，为文件采集、解析与入库程序奠定了最初的基础。

# 文件数据采集与集中管理平台

本项目用于采集设备生成的文件，按可配置规则解析业务字段并写入数据库。系统以原有 WinForms 采集端为核心，在此基础上增加了数据模型与可视化字段映射、断线补传、逐机台自动采集，以及 Center、Agent、AgentSetup 组成的内网集中管理能力。

当前源码分为两条协同工作的链路：

```text
设备文件
  -> WinForms 监控目录
  -> 按“机台 + 扩展名”匹配已发布解析规则
  -> Excel / CSV / 图片文件名解析
  -> 模型转换与校验
  -> SQLite / SQL Server / MySQL / PostgreSQL
  -> 成功目录或失败目录

Center
  -> 签发单设备安装包、管理设备与配置版本
  -> Agent Windows 服务
  -> 心跳、诊断、离线补传、WinForms 配置桥接
```

## 主要能力

### WinForms 采集端

- 配置 1～6 号机台的监控、成功和失败目录，监控设备输出文件并展示采集状态、统计和日志。
- 支持 SQLite、SQL Server、MySQL、PostgreSQL 连接配置、连接测试和主数据库选择。
- 数据库暂时断开时记录待上传任务，恢复后继续补传；源文件只有在业务数据提交成功后才移入成功目录。
- 支持程序登录启动、最小化到托盘，以及按机台选择“程序启动后自动采集”；旧配置不会自动开启任何机台。
- 本地运行数据位于程序目录，核心文件包括 `config.json` 和 `Data\采集记录.db`。部署或复制时应保留完整发布目录，不能只复制 EXE。

### 数据模型与字段映射

- 可维护数据模型、字段、启停状态和物理表映射，并支持从 Excel 批量导入模型定义。
- 映射规则采用“定义 → 草稿 → 预览验证 → 发布 → 机台绑定”的版本化流程；运行时只使用已发布且通过完整性校验的规则。
- 支持单条记录、重复行表格、主表 + 子表和图片文件名四种记录模式。
- 支持固定单元格、锚点偏移、表头列、文件名片段、默认值、精确值映射以及受限类型转换。
- 删除映射时，会在同一事务中按外键依赖顺序清理机台绑定、模型快照、规则版本和规则定义，避免残留数据导致删除失败。
- 预览或保存遇到转换失败时，会提示记录行、目标字段、来源位置、原始值、目标类型和字段配置；诊断增强不会改变原有转换语义。
- AI 仅用于生成待人工确认的声明式映射草稿，不拥有保存、发布或执行任意代码的权限。

### Excel 与 CSV 映射

- Excel 支持 `.xls` 和 `.xlsx`；CSV 支持 `.csv`，并复用同一套模型、预览、版本、发布和运行时契约。
- CSV 可明确配置 UTF-8、GBK 或 GB18030 编码，分隔符、双引号、表头行、首条数据行和空白行策略。
- CSV 解析支持引号内分隔符、换行及双引号转义；编码、列数、文件大小、模板签名或字段转换不符合规则时整文件失败，不静默错列或截断。
- CSV 在映射界面中转换为虚拟工作表坐标，因此仍可使用 `A1`、`B2` 等位置完成点选、预览和重复行配置。

### 图片文件名采集

- 支持 `.jpg`、`.jpeg`、`.png`、`.bmp`，只解析文件名，不识别图片内容。
- 可将完整文件名、无扩展名文件名或 `[]` / `【】` 中的片段映射到模型字段。
- 采集成功时，图片归档到 `共享目录\Machine-机台ID\原始文件名`；同名不同内容不会被静默覆盖。
- 共享目录建议使用 UNC 路径，并使用实际运行 WinForms 的 Windows 账户验证读写权限。

### 内网集中管理

- Center 提供首次设置、管理员登录、设备管理、部署向导、配置版本、发布回滚和统一诊断。
- Center 当前集中下发的范围仅包括机台 `id`、`name`、`monitorPath`、`successPath`、`errorPath`；数据模型与字段映射仍在 WinForms 本地维护和发布。
- Agent 以 Windows 服务运行，负责一次性注册、心跳、配置接收、离线补传和本机 WinForms 状态桥接。
- AgentSetup 校验现有 WinForms 目录后安装 Agent，不覆盖或删除原采集程序。
- 单设备 ZIP 使用一次性 enrollment token；注册成功后删除 token，改用设备独立凭据。
- 配置发布只有在 Agent 回执为 `Applied`，且 `effectiveVersion`、`effectiveSha256` 与目标一致时，才表示 WinForms 已确认生效。
- 统一诊断中的未知状态不会被标记为健康，导出的诊断包会对路径、哈希、token、凭据和密钥做脱敏处理。

## 项目结构

| 路径 | 目标框架 | 职责 |
| --- | --- | --- |
| `MachineDataAcquisitionSystem/` | .NET Framework 4.6.2 | WinForms 采集、模型、映射、文件处理和数据库入库 |
| `Acquisition.Center/` | .NET 8 | 内网管理站点、设备注册、配置发布和诊断 |
| `Acquisition.Agent/` | .NET 8 | 设备端 Windows 服务和 WinForms 桥接 |
| `Acquisition.Agent.Setup/` | .NET 8 Windows | 单设备 Agent 安装器 |
| `Acquisition.Contracts/` | .NET 8 | Center、Agent、安装器共享契约 |
| `tests/` | net462 / .NET 8 / Node.js | 后端、WinForms 映射和 Center 前端回归测试 |
| `scripts/` | PowerShell | 内网发布组包与隔离闭环验收 |
| `docs/` | Markdown | 专项设计和操作说明 |

## 开发环境

- Windows 10/11 或 Windows Server。
- Visual Studio 2022，或可用的 MSBuild 环境。
- .NET Framework 4.6.2 Targeting Pack；WinForms 主程序和对应测试均以 `net462` 为准。
- .NET 8 SDK，用于 Center、Agent、安装器及其测试。
- Node.js，用于 Center 前端逻辑测试。
- NuGet 依赖需要能够从已配置的软件源还原。

涉及真实设备、数据库、共享目录或 Windows 服务的验证，还需要对应环境权限。不要把本地构建或隔离测试通过等同于现场部署通过。

## 构建与运行

首次拉取后在仓库根目录执行：

```powershell
dotnet restore .\MachineDataAcquisitionSystem.sln
dotnet build .\MachineDataAcquisitionSystem.sln -c Debug --no-restore
```

WinForms 输出位于：

```text
MachineDataAcquisitionSystem\bin\Debug\文件数据采集系统.exe
```

也可以直接在 Visual Studio 中将 `MachineDataAcquisitionSystem` 设为启动项目。首次运行前应确认实际采集目录、数据库连接和程序目录写权限；不要直接复用生产环境的 `config.json` 或 `Data\采集记录.db` 做开发测试。

本地启动 Center：

```powershell
dotnet run --project .\Acquisition.Center\Acquisition.Center.csproj
```

未初始化的 Center 默认只在本机开放首次设置页：

```text
http://127.0.0.1:5080/setup.html
```

正式环境必须为 Center 配置 SQL Server。`appsettings.json` 只保存通用日志项，首次设置生成的运行配置不应手工复制到其他环境。

## WinForms 基本使用流程

1. 在“系统配置”中配置数据库并执行连接测试，明确主数据库。
2. 维护 1～6 号机台及其监控、成功、失败目录。
3. 在“数据模型”中创建或导入目标模型，确认字段类型、必填属性和物理表。
4. 选择 Excel、CSV 或图片样本建立字段映射，先预览并处理所有校验错误。
5. 保存草稿、验证并发布规则，再绑定到指定“机台 + 文件扩展名”。
6. 手动启动采集，或在系统配置中勾选需要随程序启动的机台。
7. 通过主界面、日志和查询入口核对文件去向及实际入库结果。

映射规则发布后，如果模型字段、类型或必填属性发生变化，旧规则会被完整性检查拦截，需要重新确认映射并发布新版本。

## Center 与 Agent 部署

### 1. 生成内网发布包

```powershell
.\scripts\Publish-InternalLanBundle.ps1 -Configuration Release
```

脚本创建新的带时间戳目录，不覆盖旧包。输出包括：

- `Center/`：自包含的 Center 发布目录，含安装器与 Agent payload。
- `DevicePackageTemplate/`：无设备凭据的结构模板，仅供核对，不能直接安装。

Center 为单台设备签发的 ZIP 根结构固定为：

```text
AgentSetup.exe
deployment.json
payload.manifest.json
AgentPayload\...
```

`payload.manifest.json` 记录文件长度和 SHA-256。运行时 `appsettings.json` 由安装器在版本目录生成；一次性 token 单独写入 `%ProgramData%\AcquisitionAgent\enrollment.json`。

### 2. 完成 Center 首次设置

1. 将 `Center` 固定部署到服务器并启动 `Acquisition.Center.exe`。
2. 打开 `http://127.0.0.1:5080/setup.html`，配置运行模式、监听地址、端口、Agent 可访问地址、SQL Server 和 HTTPS 策略。
3. 保存只显示一次的管理员访问码，并按页面提示重启 Center。
4. 从配置的内网地址登录，进入部署向导。

监听地址与 Agent 访问地址不是同一概念：监听地址可以绑定服务器网卡，Agent 地址必须是设备能够访问的具体 URL，不能填写 `0.0.0.0`。

### 3. 安装设备 Agent

在 Center 部署向导中填写设备编号、厂区/楼层/产线，以及该电脑现有 WinForms 采集目录，例如 `D:\采集程序`。安装器会先检查：

```text
文件数据采集系统.exe
Data\采集记录.db
config.json
```

检查通过后，安装器把 Agent 安装到独立版本目录，将 `AcquisitionAgent` 服务设为延迟自动启动，并让 Agent 与 WinForms 指向同一份采集数据库。安装器不会代管 WinForms 的登录启动设置。

Center 中出现“已注册”和“首次心跳”才表示安装链路完成；下载 ZIP 或创建部署记录本身不代表安装成功。

### 4. 发布和回滚配置

在“配置中心”明确选择目标 Agent，编辑机台路径、预览差异后创建发布任务。历史版本不可覆盖，回滚会创建更高版本。若 WinForms 未在时限内确认新配置，Agent 会恢复旧文件并报告失败或回滚。

## 测试

在已完成依赖还原的前提下，可按项目分别执行：

```powershell
dotnet test .\tests\MachineDataAcquisitionSystem.Tests\MachineDataAcquisitionSystem.Tests.csproj -c Debug --no-restore
dotnet test .\tests\Acquisition.Contracts.Tests\Acquisition.Contracts.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Agent.Tests\Acquisition.Agent.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Center.Tests\Acquisition.Center.Tests.csproj -c Release --no-restore
dotnet test .\tests\Acquisition.Agent.Setup.Tests\Acquisition.Agent.Setup.Tests.csproj -c Release --no-restore
node --test .\tests\Acquisition.Center.Frontend.Tests\app.logic.test.cjs
dotnet build .\MachineDataAcquisitionSystem.sln -c Release --no-restore
```

`MachineDataAcquisitionSystem.Tests` 运行时需要测试输出目录中存在 `Data\采集记录.db`；若出现 `unable to open database file`，应先检查测试输出与数据库文件准备情况，不能直接判定为业务回归。

内网一期可运行隔离闭环验收：

```powershell
.\scripts\Test-InternalLanPhaseOne.ps1 -BundleRoot 'F:\...\outputs\internal-lan-bundles\internal-lan-时间戳\'
```

该脚本使用随机 LocalDB、`127.0.0.1` 和临时目录，验证首次设置、管理员登录、设备 ZIP、一次性注册、设备凭据、首次心跳及 ZIP 契约。它不安装真实 Windows 服务，也不证明 WinForms 热重载、真实设备采集或生产数据库已通过验收。

## 安全与运维注意事项

- 设备 ZIP 包含一次性注册材料，不要复制给其他设备或长期保留。
- 正式部署应启用 HTTPS，并完成证书、服务器防火墙、SQL Server 备份与恢复演练。
- `config.json`、`Data\采集记录.db`、Center 运行配置、Agent 身份文件及日志可能包含路径或连接信息，不应提交到代码仓库或发送到无关环境。
- 删除映射、清理测试数据或修改数据库前，应先确认数据库、Agent、Center 和 WinForms 均指向测试环境。
- 图片共享目录应使用最小权限账户；如需严格防止重复入库，应在业务表为稳定业务键或图片路径增加唯一约束。

## 当前验证边界

以下事项仍需在目标现场单独验收：

- 真实 UAC、`sc.exe` 服务安装、失败回退、延迟启动和开机重启。
- 远端设备上的 WinForms bridge、配置热重载确认、文件采集、断网补传和恢复。
- 图片 UNC 共享目录在实际服务账户下的权限、网络中断和同名文件策略。
- HTTPS 证书、生产 SQL Server、内网防火墙、备份恢复和上线演练。
- Center 当前使用 `EnsureCreated` 配合幂等 DDL 初始化数据库，尚未形成完整的 EF Migration 流程。

## 补充文档

- [`docs/machine-auto-start.md`](docs/machine-auto-start.md)：逐机台启动后自动采集说明。
- [`docs/CSV字段映射功能开发计划书.md`](docs/CSV字段映射功能开发计划书.md)：CSV 映射的设计背景、契约和边界。
