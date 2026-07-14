using System;
using Microsoft.CSharp;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// 生成一个最小的三字段 Excel 解析脚本，供模型配置界面直接使用。
    /// Excel 第一行为表头，第二行为数据：ProductCode、Measurement、Result。
    /// </summary>
    public static class ExcelDemoScriptTemplate
    {
        public static string Create(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("模型名称不能为空。", nameof(modelName));

            using (var provider = new CSharpCodeProvider())
            {
                if (!provider.IsValidIdentifier(modelName))
                    throw new ArgumentException("模型名称不是合法的 C# 标识符。", nameof(modelName));
            }

            return $@"// Excel 三字段解析示例
// 第一行表头：ProductCode | Measurement | Result
// 第二行数据：产品编号 | 测量值 | 判定结果
using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
{{
    IWorkbook workbook = Path.GetExtension(filePath).Equals("".xls"", StringComparison.OrdinalIgnoreCase)
        ? (IWorkbook)new HSSFWorkbook(stream)
        : new XSSFWorkbook(stream);

    var sheet = workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
    var row = sheet == null ? null : sheet.GetRow(1);
    if (row == null)
        throw new InvalidDataException(""Excel 第二行没有可解析的数据。"");

    var model = new {modelName}();
    var productCodeCell = row.GetCell(0);
    model.ProductCode = productCodeCell == null ? """" : productCodeCell.ToString().Trim();

    var measurementCell = row.GetCell(1);
    decimal measurement;
    if (measurementCell != null && measurementCell.CellType == CellType.Numeric)
    {{
        measurement = Convert.ToDecimal(measurementCell.NumericCellValue);
    }}
    else if (!decimal.TryParse(measurementCell == null ? null : measurementCell.ToString(), out measurement))
    {{
        throw new InvalidDataException(""Measurement 不是有效数字。"");
    }}
    model.Measurement = measurement;
    var resultCell = row.GetCell(2);
    model.Result = resultCell == null ? """" : resultCell.ToString().Trim();

    if (string.IsNullOrWhiteSpace(model.ProductCode))
        throw new InvalidDataException(""ProductCode 不能为空。"");

    return model;
}}";
        }
    }
}
