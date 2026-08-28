# Spec：基础配置与系统托盘驻留

## 目标

在 WinForms 主程序的“配置”窗口新增“基础配置”页，让当前 Windows 用户选择程序是否开机自启；主窗体右上角关闭按钮不再直接退出，而是隐藏到 Windows 系统托盘，避免采集任务因误关闭界面而停止。只有从托盘右键菜单选择“退出程序”，或 Windows 正在关机/注销时，才执行现有停止机台、刷新批次和日志队列的清理流程并真正退出。

## 已采用的交互假设

1. “基础配置”作为配置窗口的第一个页签。
2. 本期只提供“开机自启”复选框和独立的“保存基础配置”按钮；关闭到托盘是程序固定行为，不再增加开关。
3. 程序运行期间托盘图标始终可见；单击鼠标左键恢复并激活主界面。
4. 托盘右键菜单包含“打开主界面”和“退出程序”；“退出程序”才触发现有的异步安全关闭流程。
5. Windows 关机、注销或应用程序级退出不得被拦截为托盘隐藏。
6. 开机自启仅针对当前 Windows 用户，使用 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不请求管理员权限；注册值使用带引号的当前 EXE 完整路径。
7. 保存“开机自启”时立即同步注册表与 `appsettings.json`；写注册表或配置文件任一失败都提示失败，不显示虚假的成功消息。

## 技术栈与项目结构

- `.NET Framework 4.8`、WinForms、Windows 注册表、现有 Newtonsoft.Json 配置。
- `MachineDataAcquisitionSystem/Core/StartupRegistrationService.cs`：当前用户开机自启注册与路径格式化。
- `MachineDataAcquisitionSystem/Core/TrayClosePolicy.cs`：决定本次关闭是隐藏到托盘还是真正退出。
- `MachineDataAcquisitionSystem/Forms/ConfigForm.*`：新增“基础配置”页和保存交互。
- `MachineDataAcquisitionSystem/Forms/Form1.cs`：托盘图标、恢复、退出及关闭原因处理。
- `MachineDataAcquisitionSystem/Helpers/SettingsHelper.cs`：只保存基础配置，保留配置文件中的其他节点。
- `tests/MachineDataAcquisitionSystem.Tests/`：开机自启、基础配置持久化和关闭策略测试。

## 代码风格

沿用项目现有 C# 风格，将可测试规则放在窗体事件之外：

```csharp
if (TrayClosePolicy.ShouldHide(closeReason, exitRequested))
{
    e.Cancel = true;
    HideMainWindowToTray();
    return;
}
```

用户可见提示使用中文；不新增第三方依赖；不在测试中修改真实用户的开机启动项。

## 测试策略

- 小型测试：启用时写入带引号的 EXE 路径、停用时删除启动项、不同 `CloseReason` 的隐藏/退出决策。
- 配置持久化测试：只更新基础配置字段，并保留数据库、AI 映射等已有配置。
- 构建验证：运行 WinForms 项目现有 Release 测试与 Release 构建。
- 手工验证：点击主窗体关闭按钮后进程仍在且托盘图标可用；左键恢复；右键退出后进程结束。

## 命令

```powershell
dotnet test .\tests\MachineDataAcquisitionSystem.Tests\MachineDataAcquisitionSystem.Tests.csproj -c Release
dotnet build .\MachineDataAcquisitionSystem.sln -c Release --no-restore
```

## 边界

- 总是：保留现有未提交的主从表和映射相关改动，只追加本功能所需的局部修改。
- 总是：真正退出时继续复用现有机台停止、剩余批次刷新、日志队列收尾流程。
- 总是：托盘组件在真正退出时隐藏并释放，避免残留幽灵图标。
- 询问后再做：改为所有用户开机自启、创建 Windows 服务、启动后自动隐藏、增加托盘气泡通知。
- 不做：不写 `HKLM`、不创建计划任务、不请求管理员权限、不改变采集启停策略。

## 成功标准

1. “配置”窗口第一个页签显示“基础配置”，能读取、修改和保存当前用户的“开机自启”。
2. 启用后，当前用户注册表启动项指向当前 EXE；停用后只删除本程序自己的启动项。
3. 点击主窗体关闭按钮后界面隐藏、任务栏按钮消失、进程和采集任务继续运行。
4. 左键单击托盘图标可恢复并激活主界面；右键菜单可打开主界面或真正退出。
5. Windows 关机/注销不会被托盘逻辑阻止。
6. 新增针对性测试通过，现有 Release 测试与 Release 构建结果如实记录。

## 确认记录

- 2026-08-24：用户已确认上述 7 条交互假设，进入实现与验证阶段。
