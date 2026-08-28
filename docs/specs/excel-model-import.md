# Spec：数据模型 Excel 批量导入

## 目标

在 WinForms“模型设计 > 数据模型”页增加 Excel 批量导入入口，减少逐个创建模型和字段的重复录入。用户先选择 `.xlsx`/`.xls` 文件，系统完整解析并校验，展示待导入模型数、字段数和错误；仅在无错误且用户确认后，一次性写入本地 SQLite。

## Excel 契约

工作表名称固定为 `数据模型`，第 1 行为表头，从第 2 行开始每行表示一个字段。同一模型的模型级信息须在每一行重复填写。

| 列名 | 必填 | 规则 |
|---|---:|---|
| 模型名称 | 是 | 合法且唯一的 C# 标识符，最多 256 个字符 |
| 表名 | 是 | 非空，最多 256 个字符；文件内及数据库中不得与其他模型重复 |
| 模型说明 | 否 | 文本，最多 2,048 个字符 |
| 父模型 | 否 | 留空表示无；`BaseEntity` 表示基类；也可填写同批或数据库中已有模型名 |
| 启用 | 否 | `是/否`、`true/false`、`1/0`；留空默认“否” |
| 字段名称 | 是 | 同一模型内合法且唯一的 C# 标识符，最多 256 个字符 |
| 字段类型 | 是 | `string/int/long/decimal/float/double/datetime/bool` |
| 字段长度 | 否 | 大于等于 0 的整数；留空为 0 |
| 必填 | 否 | 布尔值；留空为“否” |
| 主键 | 否 | 布尔值；留空为“否” |
| 自增 | 否 | 布尔值；留空为“否” |
| 字段说明 | 否 | 文本，最多 2,048 个字符 |

文件最多 5 MB、最多 1,000 个模型、20,000 个字段。拒绝隐藏公式执行或宏文件：仅接受 `.xlsx` 和 `.xls`，所有单元格只按显示值读取。

## 行为与边界

- 总是：先完整校验后写入；使用参数化 SQL；所有模型与字段共用一个 SQLite 事务；任一写入失败则全部回滚。
- 总是：导入仅新增模型，不修改或覆盖已有模型；模型名或表名与数据库冲突时整批拒绝。
- 总是：文件内部相同模型的模型级信息必须一致；字段名在模型内不得重复；父模型引用必须可解析且不得形成循环。
- 总是：确认窗口展示文件名、模型数、字段数、启用模型数和校验错误；有错误时不提供写入确认。
- 总是：成功后刷新模型列表，并选中首个导入模型。
- 总是：成功后沿用现有手工保存模型的规则，为每个模型生成 `GeneratedModels` 源文件；单个源文件生成失败会明确列出，可在选择模型后重新保存生成。
- 不做：不创建业务目标表、不生成解析规则、不绑定机台、不发布配置。
- 不做：不自动覆盖、不部分成功、不静默跳过错误行。
- 询问后再做：未来如需“覆盖已有模型”或导入后自动建业务表，另行设计迁移和发布保护。

## 技术栈与项目结构

- `.NET Framework 4.8`、WinForms、`System.Data.SQLite`。
- 复用项目现有 `NPOI 2.8.0` 读取 `.xlsx`/`.xls`，不新增依赖。
- `MachineDataAcquisitionSystem/Core/ModelExcelImportService.cs`：解析、规范化、校验和原子写入。
- `MachineDataAcquisitionSystem/Forms/ModelConfigForm.cs`：文件选择、汇总确认、调用服务、刷新界面。
- `tests/MachineDataAcquisitionSystem.Tests/ModelExcelImportServiceTests.cs`：解析和数据库集成测试。

## 代码风格

沿用现有 C# 风格和显式资源释放：

```csharp
using (var connection = new SQLiteConnection(connectionString))
using (var transaction = connection.BeginTransaction())
{
    // 只使用参数化命令写入。
    transaction.Commit();
}
```

面向用户的错误使用中文，并包含 Excel 行号和列名；内部异常不在校验阶段直接显示堆栈。

## 测试策略

- 小型测试：表头识别、布尔值/数字解析、标识符、类型白名单、模型聚合、冲突和父模型循环。
- 中型测试：真实临时 `.xlsx`/`.xls` + 临时 SQLite；验证成功写入、重复冲突零写入、某行失败整批零写入。
- UI 保持薄层，不把校验规则写入事件处理器；通过服务测试覆盖核心行为。
- 不修改或跳过现有失败测试。

## 命令

```powershell
dotnet test .\tests\MachineDataAcquisitionSystem.Tests\MachineDataAcquisitionSystem.Tests.csproj -c Release
dotnet test .\MachineDataAcquisitionSystem.sln -c Release
dotnet build .\MachineDataAcquisitionSystem.sln -c Release --no-restore
```

## 成功标准

1. 用户可从“数据模型”页选择 `.xlsx` 或 `.xls`，一次导入多个模型及其字段。
2. 导入前能看到汇总；错误精确到工作表行和列，且错误文件不会写入任何模型或字段。
3. 数据库既有模型不会被覆盖，重复模型名/表名会被明确阻止。
4. 成功导入的数据与逐个新增保存的 `DataModels`、`ModelFields` 字段粒度一致。
5. 新增测试、现有测试和 Release 构建全部通过。

## 待确认

- 是否接受“导入模型默认停用；只有 Excel 明确填写启用才启用”。
- 是否接受“发现任何冲突或错误时整批拒绝，不做部分导入”。
- 是否需要同时提供一个可下载的空白 Excel 模板按钮；本期默认包含，以降低表头填写错误。
