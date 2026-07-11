# 文件数据采集系统 (Machine Data Acquisition System)

> **原创作者：禤总**  
> 感谢禤总的卓越设计与原创贡献，为设备数据采集领域提供了高效、稳定的解决方案。

## 项目概述

文件数据采集系统是一套基于 .NET Framework 4.8 + WinForms 的桌面应用程序，用于**自动监控、采集、解析和存储**设备（机床等）产生的数据文件。系统支持多设备并发监控、多种文件格式解析、脚本匹配引擎以及数据可视化查询。

通过实时监控指定目录中的文件变化，自动解析文件内容并写入数据库，实现对设备运行数据的自动化采集与存储。

## 主要功能

- **文件实时监控** — 多线程并发监控多个设备的文件目录，自动识别新增文件
- **文件解析引擎** — 支持 JSON、TXT 等格式解析，内置可扩展的解析器接口 (`IParser`)
- **脚本匹配引擎** — 支持通过脚本规则对文件内容进行匹配与提取
- **动态建表** — 根据设备配置自动生成对应的数据表结构
- **多数据库支持** — 集成 SQLite（本地存储）与 MySQL（远程存储），通过 SqlSugar ORM 统一访问
- **数据查询与统计** — 提供可视化查询界面，支持按设备、时间范围检索与统计
- **处理队列管理** — 实时展示文件处理队列状态、处理速度、成功/失败统计
- **日志记录** — 完整的操作日志记录与查看功能

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET Framework 4.8 |
| 界面 | Windows Forms (WinForms) |
| ORM | SqlSugar 5.1.4 |
| 数据库 | SQLite (本地) / MySQL (远程) |
| JSON 解析 | Newtonsoft.Json 13.0.4 |
| Excel 导出 | NPOI 2.8.0 |
| ID 生成 | Yitter.IdGenerator (雪花算法) |
| 图表绘制 | SkiaSharp |

## 项目结构

```
MachineDataAcquisitionSystem/
├── Core/                     # 核心逻辑
│   ├── FileWatcher.cs        # 文件监控器
│   ├── FileParser.cs         # 文件解析器
│   ├── Machine.cs            # 设备模型
│   ├── MachineManager.cs     # 设备管理器
│   ├── ScriptEngine.cs       # 脚本引擎
│   ├── ScriptMatcher.cs      # 脚本匹配器
│   └── Parser/               # 解析器接口与实现
│       ├── IParser.cs        # 解析器接口
│       ├── JsonParser.cs     # JSON 解析器
│       └── TxtParser.cs      # TXT 解析器
├── Data/                     # 数据访问层
│   ├── IDataRepository.cs    # 仓储接口
│   └── MockRepository.cs     # Mock 实现
├── Forms/                    # 界面窗体
│   ├── Form1.cs              # 主窗体
│   ├── ConfigForm.cs         # 配置窗体
│   ├── QueryForm.cs          # 查询窗体
│   ├── LogViewerForm.cs      # 日志查看器
│   └── ModelConfigForm.cs    # 模型配置窗体
├── Helpers/                  # 辅助工具类
│   ├── ConfigHelper.cs       # 配置帮助类
│   ├── DatabaseHelper.cs     # 数据库初始化
│   ├── FileHelper.cs         # 文件操作帮助类
│   ├── LogHelper.cs          # 日志帮助类
│   └── SettingsHelper.cs     # 设置帮助类
├── Models/                   # 数据模型
│   ├── AppSettings.cs        # 应用配置
│   ├── DatabaseConfig.cs     # 数据库配置
│   ├── MachineConfig.cs      # 设备配置
│   ├── MachineStatus.cs      # 设备状态
│   ├── ParseScript.cs        # 解析脚本
│   └── ProcessResult.cs      # 处理结果
└── Resources/                # 资源文件
    └── Images/               # 图标与图片资源
```

## 使用说明

### 环境要求

- Windows 7 / 10 / 11
- .NET Framework 4.8 运行时
- Visual Studio 2022（如需编译）

### 启动

1. 使用 Visual Studio 打开 `MachineDataAcquisitionSystem.sln`
2. 在 NuGet 包管理器中还原依赖包
3. 按 `F5` 直接运行

### 配置流程

1. **设备配置** — 在配置界面中添加设备，设置设备名称、监控目录、成功/失败目录
2. **解析配置** — 配置文件解析规则，选择解析器类型（JSON / TXT）
3. **脚本配置** — 编写匹配脚本，定义数据提取规则
4. **启动监控** — 返回主界面，启动文件监控，系统自动采集数据

## 许可

本项目仅供学习和参考。