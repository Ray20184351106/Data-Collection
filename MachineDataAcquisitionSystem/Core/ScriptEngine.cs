using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.CSharp;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using SqlSugar;
using Yitter.IdGenerator;
using MachineDataAcquisitionSystem.Helpers;
using System.Data.SQLite;
using MachineDataAcquisitionSystem.Models;
using MachineDataAcquisitionSystem.Core.Mapping;

namespace MachineDataAcquisitionSystem.Core
{
    public static class ScriptEngine
    {
        public const string EngineAbiVersion = "2";

        private static readonly ConcurrentDictionary<string, Lazy<CompiledScriptExecutor>> ScriptCache =
            new ConcurrentDictionary<string, Lazy<CompiledScriptExecutor>>(StringComparer.Ordinal);

        public static object Execute(string scriptCode, string filePath, int machineId, int modelId)
        {
            ValidateExecutionInput(scriptCode, modelId);
            return Compile(scriptCode, modelId, null).Execute(filePath, machineId);
        }

        public static object Execute(ParseScript script, string filePath, int machineId)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            ValidateExecutionInput(script.ScriptCode, script.ModelId);
            if (!script.ParserVersionId.HasValue)
            {
                return Compile(script.ScriptCode, script.ModelId, null).Execute(filePath, machineId);
            }

            if (script.ParserVersionId.Value <= 0)
            {
                throw new InvalidOperationException("The parse-rule version id is invalid.");
            }
            if (!script.IsEnabled)
            {
                throw new InvalidOperationException("A versioned parse rule must be published and enabled before execution.");
            }
            if (string.IsNullOrWhiteSpace(script.ContentSha256) ||
                string.IsNullOrWhiteSpace(script.ModelSchemaHash))
            {
                throw new InvalidOperationException("The published parse-rule cache metadata is incomplete.");
            }
            IReadOnlyList<ParseScriptModelSnapshot> snapshots = ValidateModelSnapshots(script);
            if (!Enum.IsDefined(
                typeof(MachineDataAcquisitionSystem.Core.Mapping.ParseRuleType),
                script.RuleType))
            {
                throw new InvalidOperationException("The parse-rule type is invalid.");
            }

            string cacheKey = BuildCacheKey(script);
            string versionedScriptCode = script.ScriptCode;
            int versionedModelId = script.ModelId;
            string versionedModelCode = snapshots.Count > 0
                ? string.Join(Environment.NewLine, snapshots.Select(item => item.GeneratedModelCodeSnapshot))
                : script.GeneratedModelCodeSnapshot;
            bool sourcesAreMapped = snapshots.Count > 0;
            Lazy<CompiledScriptExecutor> lazyExecutor = ScriptCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<CompiledScriptExecutor>(
                    () => Compile(
                        versionedScriptCode,
                        versionedModelId,
                        versionedModelCode,
                        sourcesAreMapped),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            CompiledScriptExecutor executor;
            try
            {
                executor = lazyExecutor.Value;
            }
            catch
            {
                Lazy<CompiledScriptExecutor> removed;
                ScriptCache.TryRemove(cacheKey, out removed);
                throw;
            }

            return executor.Execute(filePath, machineId);
        }

        /// <summary>
        /// Compiles a generated script without executing it or adding it to the runtime cache.
        /// Mapping validation uses this after the side-effect-free Excel preview succeeds.
        /// </summary>
        public static void ValidateCompilation(string scriptCode, int modelId)
        {
            ValidateExecutionInput(scriptCode, modelId);
            Compile(scriptCode, modelId, null);
        }

        public static void ValidateCompilation(
            string scriptCode,
            IEnumerable<MappingTargetDefinition> targets)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            MappingTargetDefinition[] targetArray = targets.ToArray();
            if (targetArray.Length == 0)
                throw new ArgumentException("At least one model target is required.", nameof(targets));
            if (targetArray.Any(target => target == null || target.ModelId <= 0 ||
                string.IsNullOrWhiteSpace(target.TargetModelType)))
                throw new ArgumentException("Model target metadata is incomplete.", nameof(targets));
            if (targetArray.Select(target => target.ModelId).Distinct().Count() != targetArray.Length)
                throw new ArgumentException("Model targets must be unique.", nameof(targets));

            ValidateExecutionInput(scriptCode, targetArray[0].ModelId);
            string modelSources = string.Join(
                Environment.NewLine,
                targetArray.Select(target => CaptureConfiguredModelCode(
                    target.ModelId,
                    target.TargetModelType)));
            Compile(scriptCode, targetArray[0].ModelId, modelSources, true);
        }

        private static CompiledScriptExecutor Compile(
            string scriptCode,
            int modelId,
            string generatedModelCodeSnapshot,
            bool snapshotAlreadyMapped = false)
        {
            System.Diagnostics.Debug.WriteLine(
                string.Format(CultureInfo.InvariantCulture, "Compiling parse script for model {0}.", modelId));

            // 读取 GeneratedModels 文件夹中的所有 .cs 文件
            string generatedModelsCode = generatedModelCodeSnapshot ?? GetGeneratedModelsCode(modelId);
            if (!snapshotAlreadyMapped)
                generatedModelsCode = ApplyConfiguredTableMapping(modelId, generatedModelsCode);

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
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(Newtonsoft.Json.JsonConvert)).Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            compilerParams.ReferencedAssemblies.Add(Assembly.GetAssembly(typeof(SugarTable)).Location);

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

            var assembly = result.CompiledAssembly;
            var type = assembly.GetType("ScriptNamespace.ScriptExecutor");
            if (type == null)
            {
                throw new InvalidOperationException("The compiled script executor type was not found.");
            }

            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Execute");
            if (method == null)
            {
                throw new InvalidOperationException("The compiled script executor method was not found.");
            }

            return new CompiledScriptExecutor(instance, method);
        }

        /// <summary>
        /// 清除所有脚本缓存
        /// </summary>
        public static void ClearCache()
        {
            ScriptCache.Clear();
            System.Diagnostics.Debug.WriteLine("脚本缓存已清除");
        }

        /// <summary>
        /// 清除指定发布版本的脚本缓存。
        /// </summary>
        public static void ClearCache(long versionId)
        {
            if (versionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(versionId), "Version id must be positive.");
            }

            string prefix = versionId.ToString(CultureInfo.InvariantCulture) + "|";
            foreach (string cacheKey in ScriptCache.Keys)
            {
                if (!cacheKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                Lazy<CompiledScriptExecutor> removed;
                ScriptCache.TryRemove(cacheKey, out removed);
            }
        }

        /// <summary>
        /// 清除指定脚本的缓存
        /// </summary>
        public static void ClearCache(int modelId, string scriptCode)
        {
            // Unversioned compatibility executions are deliberately not cached.
            // Clearing all versioned entries preserves the historical guarantee
            // that no previously compiled executor remains after this call.
            ClearCache();
        }

        private static string BuildCacheKey(ParseScript script)
        {
            string modelFingerprint = script.ModelSnapshots != null && script.ModelSnapshots.Count > 0
                ? string.Join(",", script.ModelSnapshots
                    .OrderBy(item => item.ModelId)
                    .Select(item => string.Join(":", new[]
                    {
                        item.ModelId.ToString(CultureInfo.InvariantCulture),
                        item.ModelSchemaHash ?? string.Empty,
                        item.GeneratedModelCodeSha256 ?? string.Empty
                    })))
                : script.GeneratedModelCodeSha256;
            return string.Concat(
                script.ParserVersionId.Value.ToString(CultureInfo.InvariantCulture),
                "|",
                script.ContentSha256,
                "|",
                script.ModelSchemaHash,
                "|",
                EngineAbiVersion,
                "|",
                modelFingerprint);
        }

        private static IReadOnlyList<ParseScriptModelSnapshot> ValidateModelSnapshots(ParseScript script)
        {
            if (script.ModelSnapshots != null && script.ModelSnapshots.Count > 0)
            {
                var ids = new HashSet<int>();
                var types = new HashSet<string>(StringComparer.Ordinal);
                foreach (ParseScriptModelSnapshot snapshot in script.ModelSnapshots)
                {
                    if (snapshot == null || snapshot.ModelId <= 0 ||
                        !MappingRuleSerializer.IsIdentifier(snapshot.ModelType) ||
                        string.IsNullOrWhiteSpace(snapshot.ModelSchemaHash) ||
                        string.IsNullOrWhiteSpace(snapshot.GeneratedModelCodeSnapshot) ||
                        string.IsNullOrWhiteSpace(snapshot.GeneratedModelCodeSha256) ||
                        !ids.Add(snapshot.ModelId) || !types.Add(snapshot.ModelType) ||
                        !string.Equals(
                            MappingRuleSerializer.Sha256(snapshot.GeneratedModelCodeSnapshot),
                            snapshot.GeneratedModelCodeSha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The generated model source snapshots are missing or invalid.");
                }
                if (!script.ModelSnapshots.Any(item =>
                    item.ModelId == script.ModelId &&
                    string.Equals(item.ModelType, script.TargetModelType, StringComparison.Ordinal) &&
                    string.Equals(item.ModelSchemaHash, script.ModelSchemaHash, StringComparison.Ordinal)))
                    throw new InvalidOperationException("The primary generated model snapshot does not match the parse rule.");
                return script.ModelSnapshots;
            }

            if (string.IsNullOrWhiteSpace(script.GeneratedModelCodeSnapshot) ||
                string.IsNullOrWhiteSpace(script.GeneratedModelCodeSha256) ||
                !string.Equals(
                    MappingRuleSerializer.Sha256(script.GeneratedModelCodeSnapshot),
                    script.GeneratedModelCodeSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated model source snapshot is missing or invalid.");
            return Array.Empty<ParseScriptModelSnapshot>();
        }

        private static void ValidateExecutionInput(string scriptCode, int modelId)
        {
            if (string.IsNullOrWhiteSpace(scriptCode))
            {
                throw new ArgumentException("Script code is required.", nameof(scriptCode));
            }
            if (modelId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modelId), "Model id must be positive.");
            }
        }

        private sealed class CompiledScriptExecutor
        {
            private readonly object _instance;
            private readonly MethodInfo _method;

            public CompiledScriptExecutor(object instance, MethodInfo method)
            {
                _instance = instance ?? throw new ArgumentNullException(nameof(instance));
                _method = method ?? throw new ArgumentNullException(nameof(method));
            }

            public object Execute(string filePath, int machineId)
            {
                try
                {
                    return _method.Invoke(_instance, new object[] { filePath, machineId });
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
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

                // 2. Capture the exact source that this execution will compile.
                return CaptureGeneratedModelCode(modelName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Model代码失败: {ex.Message}");
                return "";
            }
        }

        private static string ApplyConfiguredTableMapping(int modelId, string modelSource)
        {
            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    string sql = "SELECT TableName FROM DataModels WHERE Id = @Id";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", modelId);
                        object value = cmd.ExecuteScalar();
                        if (value == null || value == DBNull.Value || string.IsNullOrWhiteSpace(value.ToString()))
                            throw new InvalidOperationException("The configured database table name is missing.");
                        return ModelTableMapping.ApplySqlSugarTableAttribute(modelSource, value.ToString());
                    }
                }
            }
            catch (SQLiteException ex)
            {
                // Published snapshots remain executable during configuration-database outages.
                // When the configuration database is available, a missing TableName still fails fast above.
                System.Diagnostics.Debug.WriteLine($"读取模型表映射失败，保留已发布快照: {ex.Message}");
                return modelSource;
            }
        }

        public static string CaptureGeneratedModelCode(string modelName)
        {
            if (!MappingRuleSerializer.IsIdentifier(modelName))
                throw new InvalidOperationException("The generated model name is invalid.");

            string modelsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "GeneratedModels"));
            string modelFile = Path.GetFullPath(Path.Combine(modelsDir, modelName + ".cs"));
            string prefix = modelsDir.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!modelFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(modelFile))
                throw new InvalidOperationException("The generated model source file does not exist.");

            string content = File.ReadAllText(modelFile);
            int classStart = content.IndexOf("public class", StringComparison.Ordinal);
            if (classStart < 0)
                throw new InvalidOperationException("The generated model source does not contain a public class.");
            string classCode = ExtractClassBody(content, classStart);
            if (string.IsNullOrWhiteSpace(classCode))
                throw new InvalidOperationException("The generated model class could not be extracted.");
            return classCode;
        }

        public static string CaptureConfiguredModelCode(int modelId, string modelName)
        {
            if (modelId <= 0) throw new ArgumentOutOfRangeException(nameof(modelId));
            return ApplyConfiguredTableMapping(modelId, CaptureGeneratedModelCode(modelName));
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
