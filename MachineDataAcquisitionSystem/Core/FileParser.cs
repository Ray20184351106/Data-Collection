// Core/FileParser.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Data;
using System.ComponentModel;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// 文件解析方法都放在这里
    /// </summary>
    public class FileParser
    {
        /// <summary>
        /// 解析文件，返回数据行列表
        /// </summary>
        public List<Dictionary<string, object>> Parse(string filePath, out string errorMsg)
        {
            errorMsg = null;
            var result = new List<Dictionary<string, object>>();

            try
            {
                string ext = Path.GetExtension(filePath).ToLower();

                switch (ext)
                {
                    case ".txt":
                        result = ParseTxt(filePath);
                        break;
                    case ".csv":
                        result = ParseCsv(filePath);
                        break;
                    case ".xlsx":
                    case ".xls":
                        //result = ParseExcel(filePath);
                        break;
                    default:
                        errorMsg = $"不支持的文件格式: {ext}";
                        return result;
                }

                if (result.Count == 0)
                {
                    errorMsg = "文件无有效数据";
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"解析失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 解析 TXT 文件（假设每行格式：字段1|字段2|字段3）
        /// </summary>
        private List<Dictionary<string, object>> ParseTxt(string filePath)
        {
            var result = new List<Dictionary<string, object>>();
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            if (lines.Length == 0) return result;

            // 第一行是表头
            string[] headers = lines[0].Split('|');

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] values = lines[i].Split('|');
                var row = new Dictionary<string, object>();

                for (int j = 0; j < headers.Length && j < values.Length; j++)
                {
                    row[headers[j].Trim()] = values[j].Trim();
                }

                if (row.Count > 0)
                    result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// 解析 CSV 文件
        /// </summary>
        private List<Dictionary<string, object>> ParseCsv(string filePath)
        {
            var result = new List<Dictionary<string, object>>();
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);

            if (lines.Length == 0) return result;

            string[] headers = lines[0].Split(',');

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] values = lines[i].Split(',');
                var row = new Dictionary<string, object>();

                for (int j = 0; j < headers.Length && j < values.Length; j++)
                {
                    row[headers[j].Trim()] = values[j].Trim();
                }

                if (row.Count > 0)
                    result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// 解析 Excel 文件（需要安装 EPPlus 包）
        /// </summary>
        //private List<Dictionary<string, object>> ParseExcel(string filePath)
        //{
        //    var result = new List<Dictionary<string, object>>();

        //    // 设置 EPPlus 许可（非商业使用）
        //    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        //    using (var package = new ExcelPackage(new FileInfo(filePath)))
        //    {
        //        var worksheet = package.Workbook.Worksheets[0]; // 第一个工作表
        //        if (worksheet == null || worksheet.Dimension == null) return result;

        //        int rowCount = worksheet.Dimension.Rows;
        //        int colCount = worksheet.Dimension.Columns;

        //        if (rowCount < 2) return result;

        //        // 读取表头
        //        var headers = new string[colCount];
        //        for (int col = 1; col <= colCount; col++)
        //        {
        //            headers[col - 1] = worksheet.Cells[1, col].Text;
        //        }

        //        // 读取数据
        //        for (int row = 2; row <= rowCount; row++)
        //        {
        //            var rowData = new Dictionary<string, object>();

        //            for (int col = 1; col <= colCount; col++)
        //            {
        //                string header = headers[col - 1];
        //                string value = worksheet.Cells[row, col].Text;

        //                if (!string.IsNullOrEmpty(header))
        //                {
        //                    rowData[header] = value;
        //                }
        //            }

        //            if (rowData.Count > 0)
        //                result.Add(rowData);
        //        }
        //    }

        //    return result;
        //}
    }
}