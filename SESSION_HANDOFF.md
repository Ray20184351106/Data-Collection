# Codex 会话交接：文件数据采集集中管理平台

> 更新时间：2026-07-11  
> 工作目录：`F:\mzh_leijl\Projects\文件数据采集系统`

## 1. 项目目标

将原有单机 WinForms 文件采集程序扩展为公司局域网内使用的集中管理平台，目标规模约 10～50 台设备电脑。

当前采用：

```text
设备电脑（WinForms采集程序 + Windows Agent + 本地SQLite）
                 │ HTTP / AgentId + RegistrationKey
                 ▼
中心服务（ASP.NET Core + SignalR + SQL Server）
                 │
                 ▼
             Web管理看板
```

核心目标是采集稳定、中心可见、断网不丢记录和配置可集中同步，不建设互联网级安全平台。

## 2. 当前项目组成

- `MachineDataAcquisitionSystem`：原有 .NET Framework 4.8 WinForms 采集程序。
- `Acquisition.Contracts`：中心和Agent共享的通信契约。
- `Acquisition.Agent`：.NET 8 Windows服务，负责心跳、SQLite离线队列、补传、命令和配置同步。
- `Acquisition.Center`：.NET 8 ASP.NET Core中心服务、SQL Server持久化、SignalR和Web看板。
- `tests`：契约、Agent SQLite和中心 SQL Server LocalDB测试。

## 3. 已完成的重要改动

### WinForms采集可靠性

- 修复解决方案中的错误项目路径，完整解决方案可以构建。
- 同一设备文件串行处理，不同设备并行，全局并发上限为4。
- 目标数据库失败自动重试3次并指数退避。
- 只有目标数据库确认提交后，文件才移入 `Success`。
- 数据库失败或解析结果为空时，文件进入 `Error`，不再出现“内存入队即成功”。
- 数据库密码和完整连接串保存时使用 Windows DPAPI，加密旧明文配置。
- `RemoteAgentBridge` 通过共享 Agent SQLite 执行启停、刷新、配置重载并上报本机设备状态。

### Agent

- Windows服务方式运行，每10秒向中心发送心跳。
- 本地 SQLite 保存未上传采集记录、已执行命令、配置版本和桥接命令。
- 中心不可用时保留记录，恢复后每批最多500条补传。
- 从原 `采集记录.db/FileProcessRecord` 增量导入历史记录。
- 命令按 `CommandId` 幂等；崩溃后的终态回执可重放，超时执行中命令可安全重领。
- 配置写入临时文件，WinForms在10秒内确认重载；未确认自动恢复 `.previous`。
- 当前内网认证为请求头 `X-Agent-Id` + `X-Registration-Key`。

### 中心服务

- Agent心跳、设备状态、历史采集摘要、命令、配置和告警存入 SQL Server。
- 提供 `/api/agent/v1` 版本化Agent接口。
- Web看板显示在线数、运行设备、队列、成功失败和告警。
- 支持远程启动、停止和刷新状态。
- 配置使用“保存并同步到全部Agent”，不再经过草稿、审批和灰度发布。
- Web默认无需登录；`InternalLan.Enabled=true` 时局域网直接访问。
- 保留参数校验、参数化数据库查询、CSRF校验和统一错误结构。
- 30秒未收到心跳判定离线。

## 4. 本次需求收敛后的必要项

生产部署只要求：

1. 一台长期运行的 Windows Server 或固定Windows电脑。
2. 正式 SQL Server 数据库，不能使用 LocalDB。
3. 中心固定内网IP和开放端口，例如 `5080`。
4. 中心和所有Agent使用相同的 `RegistrationKey`。
5. 每台设备电脑使用唯一 `AgentId`。
6. 每台设备安装 Agent Windows服务，并配置原采集数据库和 `config.json` 路径。

暂不实施：HTTPS、企业CA、客户端证书、Web用户角色、审批、灰度发布和Agent自动升级。

## 5. 关键配置

中心：

```powershell
$env:ASPNETCORE_URLS='http://0.0.0.0:5080'
$env:ConnectionStrings__CenterDatabase='Server=SQL服务器;Database=AcquisitionCenter;Integrated Security=true;Encrypt=false'
$env:InternalLan__Enabled='true'
$env:InternalLan__RegistrationKey='公司自定义注册码'
```

Agent：

```json
{
  "Agent": {
    "AgentId": "LINE01-PC01",
    "CenterBaseUrl": "http://192.168.1.100:5080",
    "RegistrationKey": "公司自定义注册码",
    "LocalDatabasePath": "C:\\ProgramData\\AcquisitionAgent\\agent.db",
    "LegacyDatabasePath": "D:\\采集程序\\Data\\采集记录.db",
    "LegacyConfigPath": "D:\\采集程序\\config.json",
    "HeartbeatSeconds": 10
  }
}
```

更完整说明见根目录 `README.md`。

## 6. 已执行验证

- 4项通信契约测试通过。
- 3项Agent真实SQLite测试通过。
- 8项中心真实SQL Server LocalDB测试通过。
- 50个Agent并发心跳测试通过。
- 中心数据库首次启动迁移测试通过。
- 内网HTTP无登录访问通过。
- Agent使用注册码接入并显示在线。
- Release完整解决方案构建成功。
- 已验证现有SQLite采集记录可补传到中心。
- 联调进程和联调数据库已清理。

构建命令：

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' .\MachineDataAcquisitionSystem.sln /t:Build /p:Configuration=Release
dotnet test .\tests\Acquisition.Contracts.Tests\Acquisition.Contracts.Tests.csproj
dotnet test .\tests\Acquisition.Agent.Tests\Acquisition.Agent.Tests.csproj
dotnet test .\tests\Acquisition.Center.Tests\Acquisition.Center.Tests.csproj
```

## 7. 已知事项

- 现有 NPOI 2.8.0 构建时提示 OSMF EULA，需要公司决定接受或后续替换；当前不影响构建。
- 当前 WinForms界面仍固定展示6台机台；中心与Agent数据结构支持动态Agent和设备，后续可继续把本地界面动态化。
- 中心默认配置中的 LocalDB 仅用于开发；Production环境检测到 LocalDB 会拒绝启动。
- 默认注册码 `factory-registration-code` 仅用于开发，生产必须改成公司自定义值。
- Agent与WinForms共享 `C:\ProgramData\AcquisitionAgent\agent.db` 时，需要确保两个运行账号都有目录修改权限。

## 8. 建议的下一步

1. 在正式 Windows Server/SQL Server 上做一次真实部署演练。
2. 制作中心和Agent的发布目录及Windows服务安装脚本。
3. 先选1台设备进行24小时断网、重启、数据库异常试运行。
4. 完成5台试点后再扩展到全部设备。
5. 根据现场文件格式补齐CSV、JSON、Excel声明式解析模板界面。
