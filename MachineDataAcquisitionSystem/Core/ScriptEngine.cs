using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CSharp;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using SqlSugar;
using Yitter.IdGenerator;
using MachineDataAcquisitionSystem.Helpers;
using System.Data.SQLite;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core
{
    public static class ScriptEngine
    {
        // ========== 脚本缓存 ==========
        private static Dictionary<string, object> _scriptCache = new Dictionary<string, object>();
        private static object _cacheLock = new object();

        public static object Execute(string scriptCode, string filePath, int machineId, int modelId)
        {
            // 生成缓存键（脚本代码哈希 + 模型ID）
            string cacheKey = $"{modelId}_{scriptCode.GetHashCode()}";

            // ========== 1. 检查缓存 ==========
            if (_scriptCache.TryGetValue(cacheKey, out object cachedExecutor))
            {
                System.Diagnostics.Debug.WriteLine($"使用缓存的脚本执行器: {cacheKey}");

                // cachedExecutor 是 ScriptExecutor 的实例
                var methods = cachedExecutor.GetType().GetMethod("Execute");
                return methods.Invoke(cachedExecutor, new object[] { filePath, machineId });
            }

            System.Diagnostics.Debug.WriteLine($"编译新脚本: {cacheKey}");

            // 读取 GeneratedModels 文件夹中的所有 .cs 文件
            string generatedModelsCode = GetGeneratedModelsCode(modelId);

            // 构建完整类代码
            string fullCode = $@"
                    using System;
                    using System.IO;
                    using System.Collections.Generic;
                    using NPOI.SS.UserModel;
                    using NPOI.XSSF.UserModel;
                    using NPOI.HSSF.UserModel;
                    using Newtonsoft.Json;
                    using Yitter.IdGenerator;

                    namespace ScriptNamespace
                    {{
                        {generatedModelsCode}
                        public class ScriptExecutor
                        {{
                            public object Execute(string filePath, int machineId)
                            {{
                                {scriptCode}
                            }}
                        }}
                    }}";

            // 获取 NPOI 相关程序集路径
            string npoiDir = Path.GetDirectoryName(Assembly.GetAssembly(typeof(XSSFWorkbook)).Location);

            // 编译参数
            var compilerParams = new CompilerParameters();
            compilerParams.ReferencedAssemblies.Add("System.dll");
            compilerParams.ReferencedAssemblies.Add("System.Core.dll");
            compilerParams.ReferencedAssemblies.Add("System.IO.dll");
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(XSSFWorkbook)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(HSSFWorkbook)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(NPOI.OpenXml4Net.OPC.OPCPackage)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(NPOI.OpenXmlFormats.Spreadsheet.CT_Workbook)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(Newtonsoft.Json.JsonConvert)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);

            // 添加 Yitter.IdGenerator 程序集引用
            try
            {
                var yitterAssembly = Assembly.GetAssembly(typeof(YitIdHelper));
                if (yitterAssembly != null)
                {
                    compilerParams.ReferencedAssemblies.Add(yitterAssembly.Location);
                    System.Diagnostics.Debug.WriteLine($"已添加 Yitter 引用: {yitterAssembly.Location}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Yitter 程序集未找到");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加 Yitter 引用失败: {ex.Message}");
            }

            // 添加 SqlSugar 程序集引用
            try
            {
                var sqlSugarAssembly = Assembly.GetAssembly(typeof(SqlSugarClient));
                if (sqlSugarAssembly != null)
                {
                    compilerParams.ReferencedAssemblies.Add(sqlSugarAssembly.Location);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加 SqlSugar 引用失败: {ex.Message}");
            }

            compilerParams.GenerateInMemory = true;

            // 添加 NPOI 依赖的其他 DLL
            string openXmlPath = Path.Combine(npoiDir, "NPOI.OpenXml4Net.dll");
            string ooxmlPath = Path.Combine(npoiDir, "NPOI.OOXML.dll");

            if (File.Exists(openXmlPath))
                compilerParams.ReferencedAssemblies.Add(openXmlPath);
            if (File.Exists(ooxmlPath))
                compilerParams.ReferencedAssemblies.Add(ooxmlPath);

            // 编译
            var provider = new CSharpCodeProvider();
            var result = provider.CompileAssemblyFromSource(compilerParams, fullCode);

            if (result.Errors.HasErrors)
            {
                string errors = "";
                foreach (CompilerError error in result.Errors)
                {
                    errors += error.ErrorText + "\n";
                }
                throw new Exception($"脚本编译失败: {errors}");
            }

            // 执行
            var assembly = result.CompiledAssembly;
            var type = assembly.GetType("ScriptNamespace.ScriptExecutor");
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Execute");

            // ========== 2. 存入缓存 ==========
            lock (_cacheLock)
            {
                if (!_scriptCache.ContainsKey(cacheKey))
                {
                    _scriptCache[cacheKey] = instance;
                }
            }

            return method.Invoke(instance, new object[] { filePath, machineId });
        }

        /// <summary>
        /// 清除所有脚本缓存
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _scriptCache.Clear();
                System.Diagnostics.Debug.WriteLine("脚本缓存已清除");
            }
        }

        /// <summary>
        /// 清除指定脚本的缓存
        /// </summary>
        public static void ClearCache(int modelId, string scriptCode)
        {
            string cacheKey = $"{modelId}_{scriptCode.GetHashCode()}";
            lock (_cacheLock)
            {
                if (_scriptCache.ContainsKey(cacheKey))
                {
                    _scriptCache.Remove(cacheKey);
                    System.Diagnostics.Debug.WriteLine($"清除脚本缓存: {cacheKey}");
                }
            }
        }

        /// <summary>
        /// 根据模型ID获取Model类代码（只提取 public class 部分）
        /// </summary>
        private static string GetGeneratedModelsCode(int modelId)
        {
            try
            {
                // 1. 根据 modelId 查询模型名称
                string modelName = "";
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    string sql = "SELECT ModelName FROM DataModels WHERE Id = @Id";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", modelId);
                        var result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            modelName = result.ToString();
                        }
                        else
                        {
                            return "";
                        }
                    }
                }

                // 2. 读取对应的 Model 文件
                string modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels");
                string modelFile = Path.Combine(modelsDir, $"{modelName}.cs");

                if (!File.Exists(modelFile))
                {
                    return "";
                }

                string content = File.ReadAllText(modelFile);

                // 3. 提取 public class 开始的内容
                int classStart = content.IndexOf("public class");
                if (classStart < 0)
                {
                    return "";
                }

                // 找到匹配的结束大括号
                string classCode = ExtractClassBody(content, classStart);

                return classCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Model代码失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 提取从 public class 开始的完整类定义
        /// </summary>
        private static string ExtractClassBody(string content, int startIndex)
        {
            int braceCount = 0;
            int endIndex = startIndex;
            bool foundStart = false;

            for (int i = startIndex; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (content[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                    {
                        endIndex = i + 1;
                        break;
                    }
                }
            }

            return content.Substring(startIndex, endIndex - startIndex);
        }

        private static string ExtractClass(string content, int start)
        {
            int braceCount = 0;
            int end = start;
            bool foundStart = false;

            for (int i = start; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (content[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                    {
                        end = i + 1;
                        break;
                    }
                }
            }

            return content.Substring(start, end - start);
        }

        private static string GetModelNameById(int modelId)
        {
            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT ModelName FROM DataModels WHERE Id = @Id";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", modelId);
                    var result = cmd.ExecuteScalar();
                    return result?.ToString() ?? "";
                }
            }
        }
    }
}
