# 文件数据采集集中管理平台（公司内网版）

本版本面向同一公司局域网内约 10～50 台设备电脑，目标是稳定采集、集中查看、断网不丢记录和统一修改配置。

## 必要组成

- `MachineDataAcquisitionSystem`：设备电脑上的 WinForms 采集和本地诊断界面。
- `Acquisition.Agent`：设备电脑上的 Windows 服务，负责心跳、SQLite 离线缓存、记录补传、远程命令和配置同步。
- `Acquisition.Center`：中心 API、SignalR、Web 看板和集中配置。
- SQL Server：保存 Agent、设备状态、采集记录、命令、配置和告警。

内网第一版不要求 HTTPS、客户端证书、用户角色、配置审批和灰度发布。Agent 使用 `AgentId + RegistrationKey` 接入，中心配置保存后立即同步到全部选中 Agent。

## 开发环境启动

启动中心：

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
dotnet run --project .\Acquisition.Center\Acquisition.Center.csproj
```

启动 Agent：

```powershell
$env:DOTNET_ENVIRONMENT='Development'
$env:Agent__AgentId='PC-001'
$env:Agent__CenterBaseUrl='http://127.0.0.1:5080'
$env:Agent__RegistrationKey='factory-registration-code'
$env:Agent__LegacyDatabasePath='完整路径\Data\采集记录.db'
$env:Agent__LegacyConfigPath='完整路径\config.json'
dotnet run --project .\Acquisition.Agent\Acquisition.Agent.csproj
```

浏览器访问 `http://127.0.0.1:5080`，无需登录。

## 生产环境最低准备

### 中心服务器

1. 准备一台可长期运行的 Windows Server 或固定 Windows 电脑。
2. 安装 .NET 8 Hosting Bundle/Runtime。
3. 准备正式 SQL Server 数据库；不要使用 LocalDB。
4. 为中心服务分配固定内网 IP，例如 `192.168.1.100`。
5. 在防火墙开放中心端口，例如 TCP `5080`。

中心配置示例：

```powershell
$env:ASPNETCORE_URLS='http://0.0.0.0:5080'
$env:ConnectionStrings__CenterDatabase='Server=SQL服务器;Database=AcquisitionCenter;Integrated Security=true;Encrypt=false'
$env:InternalLan__Enabled='true'
$env:InternalLan__RegistrationKey='公司自定义注册码'
```

中心首次启动会自动创建必要表。SQL Server账号只需授予 `AcquisitionCenter` 数据库的读写和建表权限，不需要使用 `sa`。

### 每台设备电脑

1. 发布并安装 `Acquisition.Agent`。
2. 为每台电脑配置唯一 `AgentId`，例如 `LINE01-PC01`。
3. `CenterBaseUrl` 指向中心内网地址，例如 `http://192.168.1.100:5080`。
4. `RegistrationKey` 与中心一致。
5. 配置原 WinForms 的 SQLite记录库和 `config.json` 路径。
6. 将 Agent 注册成自动启动的 Windows 服务。

示例配置：

```json
{
  "Agent": {
    "AgentId": "LINE01-PC01",
    "CenterBaseUrl": "http://192.168.1.100:5080",
    "RegistrationKey": "公司自定义注册码",
    "LocalDatabasePath": "C:\\ProgramData\\AcquisitionAgent\\agent.db",
    "LegacyDatabasePath": "D:\\采集程序\\Data\\采集记录.db",
    "LegacyConfigPath": "D:\\采集程序\\config.json",
    "Site": "一厂一楼",
    "HeartbeatSeconds": 10
  }
}
```

Agent 与 WinForms 默认共享 `C:\ProgramData\AcquisitionAgent\agent.db`。两个程序的运行账号都需要该目录的修改权限，也可以通过 `ACQUISITION_AGENT_DB` 为 WinForms 指定相同路径。

## 集中配置

Web页面使用“保存并同步到全部Agent”。中心先校验 JSON，再生成不可覆盖的新版本并立即下发。Agent写入临时文件，WinForms在10秒内确认重载后才算成功；未确认则恢复 `.previous` 文件。

最小配置结构：

```json
{
  "machines": [
    {
      "id": 1,
      "name": "机台1",
      "monitorPath": "D:\\Test\\Machine1\\Incoming",
      "successPath": "D:\\Test\\Machine1\\Success",
      "errorPath": "D:\\Test\\Machine1\\Error"
    }
  ]
}
```

中心不会下发任意 C# 脚本，只同步声明式设备和解析配置。

## 现场上线顺序

1. 部署中心和 SQL Server。
2. 先安装 1 台 Agent，确认心跳、历史记录补传和远程刷新。
3. 断开中心网络，确认本地采集继续运行；恢复后记录自动补传。
4. 再扩展到 5 台、10 台，最后扩展到全部设备。
5. SQL Server每天至少做一次完整备份。

## 构建和测试

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' .\MachineDataAcquisitionSystem.sln /t:Build /p:Configuration=Release
dotnet test .\tests\Acquisition.Contracts.Tests\Acquisition.Contracts.Tests.csproj
dotnet test .\tests\Acquisition.Agent.Tests\Acquisition.Agent.Tests.csproj
dotnet test .\tests\Acquisition.Center.Tests\Acquisition.Center.Tests.csproj
```

中心集成测试使用真实 SQL Server LocalDB，Agent集成测试使用真实 SQLite 文件。现有 NPOI 包仍会提示 OSMF EULA，需要公司决定继续使用或后续替换；这不影响本次内网架构调整。

## 暂不实施

- HTTPS、企业CA和Agent客户端证书。
- Web用户、角色和审批流。
- 配置试点/20%/全量灰度流程。
- Agent自动升级。
- 公网访问和跨厂区安全接入。

这些能力如果以后确有需求，可以在当前接口版本上追加，不影响现有 Agent 的采集和离线补传。
