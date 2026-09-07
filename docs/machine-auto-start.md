# 每台机台默认启动

## 目标与验收

用户确认：在现有开机自启（基础配置）页，每台机台分别勾选是否默认启动。

- 保留“Windows 登录后自动启动本程序”，新增独立的机台勾选列表。
- 勾选按机台 ID 保存到 `appsettings.json` 的 `AutoStartMachineIds`，而非按显示名称或行号。
- 默认空列表；旧配置（包括旧 `AutoStartMonitor=true`）不隐式开启任何机台。
- 程序初始化完成后执行一次；手动打开程序同样生效。恢复托盘窗口、保存配置不触发采集启动。
- 仅启动当前已配置且在现有界面支持范围 1–6 内的勾选机台；重复 ID 不重复启动。
- 启动失败记录错误，不阻断后续勾选机台。沿用现有手动启动的采集流程。
- 基础配置保存保留磁盘中的其他配置，不夹带其他页面的未保存编辑；失败不更新内存中的启动选择。

## 技术与结构

C# / WinForms / .NET Framework 4.8，Newtonsoft.Json，xUnit。
模型与持久化位于 `MachineDataAcquisitionSystem/Models/AppSettings.cs`、`Helpers/SettingsHelper.cs`；
界面位于 `Forms/ConfigForm.cs`，启动入口位于 `Forms/Form1.cs`，单次启动调度位于 `Core/MachineAutoStartService.cs`。
测试位于 `tests/MachineDataAcquisitionSystem.Tests`。

## 风格

沿用 PascalCase 属性、下划线私有字段、中文界面与日志，不增加依赖。

```csharp
public List<int> AutoStartMachineIds { get; set; } = new List<int>();
```

## 实施顺序与验证

1. 模型、基础配置窄范围保存及测试：验证旧配置、重新加载、保存失败及其他配置保留。
2. 勾选列表与界面测试：验证名称/ID对应、勾选回显、未保存状态和取消勾选。
3. 启动调度与入口：验证只启动勾选项、单次执行、异常隔离、无全局旧开关隐式启动。
4. 差异审查及相关回归；保留既有图片原名归档修改。

```powershell
dotnet test tests\MachineDataAcquisitionSystem.Tests\MachineDataAcquisitionSystem.Tests.csproj -c Release --no-restore -p:OutputPath=bin\MachineAutoStartVerification\ --filter "FullyQualifiedName~MachineAutoStart|FullyQualifiedName~ConfigFormBasicSettingsTests|FullyQualifiedName~SettingsHelperPersistenceTests|FullyQualifiedName~StartupRegistrationServiceTests|FullyQualifiedName~ImageFileAcquisitionTests" --verbosity normal
git diff --check
```

测试构建使用独立输出目录。WinForms 控件测试在 STA 线程执行；配置测试只使用独立临时文件，不启动真实采集或改动用户开机注册表。

## 边界

- 必须：保留已有修改；区分源码/测试与已部署、真实重启验证。
- 先询问：部署、修改现用配置或数据库、实际开启采集、安装依赖。
- 禁止：批量删除、重命名历史图片、擅自开启所有机台或提交凭据。
- 本次不改变 Windows 登录自启机制、Agent 服务启动机制、远程重载策略及已有目录初始化流程。
- 现场验证待部署后执行：登录 Windows 后程序出现，只有勾选机台运行；检查日志与采集结果。
