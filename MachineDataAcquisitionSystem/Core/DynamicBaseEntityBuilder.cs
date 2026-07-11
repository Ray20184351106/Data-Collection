using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using System.Reflection.Emit;
using MachineDataAcquisitionSystem.Helpers;
using Yitter.IdGenerator;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// 动态基类生成器
    /// 根据 BaseFields 表配置动态生成 BaseEntity 类
    /// </summary>
    public static class DynamicBaseEntityBuilder
    {
        private static Type _baseEntityType;
        private static readonly object _lockObj = new object();

        /// <summary>
        /// 获取动态生成的 BaseEntity 类型
        /// </summary>
        public static Type GetBaseEntityType()
        {
            if (_baseEntityType == null)
            {
                lock (_lockObj)
                {
                    if (_baseEntityType == null)
                    {
                        _baseEntityType = CreateBaseEntityType();
                    }
                }
            }
            return _baseEntityType;
        }

        /// <summary>
        /// 动态创建 BaseEntity 类型
        /// </summary>
        private static Type CreateBaseEntityType()
        {
            // 1. 定义程序集和模块
            AssemblyName assemblyName = new AssemblyName("DynamicBaseEntityAssembly");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicBaseEntityModule");

            // 2. 定义类型：public class BaseEntity
            TypeBuilder typeBuilder = moduleBuilder.DefineType(
                "MachineDataAcquisitionSystem.Models.BaseEntity",
                TypeAttributes.Public | TypeAttributes.Class
            );

            // 3. 从数据库读取基类字段配置
            List<BaseFieldConfig> baseFields = LoadBaseFieldConfigs();

            foreach (var fieldConfig in baseFields)
            {
                // 4. 确定字段的 .NET 类型
                Type propertyType = GetFieldType(fieldConfig.FieldType);

                // 5. 创建私有字段 (backing field)
                FieldBuilder fieldBuilder = typeBuilder.DefineField(
                    "_" + fieldConfig.FieldName,
                    propertyType,
                    FieldAttributes.Private
                );

                // 6. 创建公共属性 (Property)
                PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
                    fieldConfig.FieldName,
                    PropertyAttributes.None,
                    propertyType,
                    null
                );

                // 7. 创建 get 方法
                MethodBuilder getMethod = typeBuilder.DefineMethod(
                    "get_" + fieldConfig.FieldName,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    propertyType,
                    Type.EmptyTypes
                );
                ILGenerator getIl = getMethod.GetILGenerator();
                getIl.Emit(OpCodes.Ldarg_0);
                getIl.Emit(OpCodes.Ldfld, fieldBuilder);
                getIl.Emit(OpCodes.Ret);

                // 8. 创建 set 方法
                MethodBuilder setMethod = typeBuilder.DefineMethod(
                    "set_" + fieldConfig.FieldName,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    null,
                    new Type[] { propertyType }
                );
                ILGenerator setIl = setMethod.GetILGenerator();
                setIl.Emit(OpCodes.Ldarg_0);
                setIl.Emit(OpCodes.Ldarg_1);
                setIl.Emit(OpCodes.Stfld, fieldBuilder);
                setIl.Emit(OpCodes.Ret);

                // 9. 将 get/set 方法绑定到属性
                propertyBuilder.SetGetMethod(getMethod);
                propertyBuilder.SetSetMethod(setMethod);

                // 10. 如果是只读字段，标记为只读
                if (fieldConfig.IsReadOnly)
                {
                    // 可以添加特性标记，这里简单处理
                }
            }

            // 11. 创建默认构造函数
            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes
            );
            ILGenerator constructorIl = constructorBuilder.GetILGenerator();
            constructorIl.Emit(OpCodes.Ldarg_0);
            constructorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));

            // 设置默认值
            SetDefaultValues(constructorIl, typeBuilder, baseFields);

            constructorIl.Emit(OpCodes.Ret);

            // 12. 创建类型
            return typeBuilder.CreateType();
        }

        /// <summary>
        /// 设置属性的默认值
        /// </summary>
        private static void SetDefaultValues(ILGenerator il, TypeBuilder typeBuilder, List<BaseFieldConfig> baseFields)
        {
            foreach (var fieldConfig in baseFields)
            {
                if (string.IsNullOrEmpty(fieldConfig.DefaultValue))
                    continue;

                // 获取属性
                PropertyInfo propInfo = typeBuilder.CreateType().GetProperty(fieldConfig.FieldName);
                if (propInfo == null) continue;

                // 加载当前实例 (this)
                il.Emit(OpCodes.Ldarg_0);

                // 根据类型生成默认值
                switch (fieldConfig.FieldType.ToLower())
                {
                    case "string":
                        il.Emit(OpCodes.Ldstr, fieldConfig.DefaultValue.Replace("\"", ""));
                        break;
                    case "long":
                        if (fieldConfig.DefaultValue.Contains("YitIdHelper.NextId()"))
                        {
                            // 调用 YitIdHelper.NextId() 方法
                            MethodInfo method = typeof(YitIdHelper).GetMethod("NextId");
                            il.Emit(OpCodes.Call, method);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldc_I8, long.Parse(fieldConfig.DefaultValue));
                        }
                        break;
                    case "int":
                        il.Emit(OpCodes.Ldc_I4, int.Parse(fieldConfig.DefaultValue));
                        break;
                    case "datetime":
                        if (fieldConfig.DefaultValue == "DateTime.Now")
                        {
                            MethodInfo method = typeof(DateTime).GetProperty("Now").GetGetMethod();
                            il.Emit(OpCodes.Call, method);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldstr, fieldConfig.DefaultValue);
                            MethodInfo method = typeof(DateTime).GetMethod("Parse", new Type[] { typeof(string) });
                            il.Emit(OpCodes.Call, method);
                        }
                        break;
                    case "bool":
                        il.Emit(OpCodes.Ldc_I4, fieldConfig.DefaultValue == "true" ? 1 : 0);
                        break;
                    default:
                        il.Emit(OpCodes.Ldstr, fieldConfig.DefaultValue);
                        break;
                }

                // 调用属性的 SetValue 方法
                MethodInfo setMethod = propInfo.GetSetMethod();
                il.Emit(OpCodes.Callvirt, setMethod);
            }
        }

        /// <summary>
        /// 从数据库加载基类字段配置
        /// </summary>
        private static List<BaseFieldConfig> LoadBaseFieldConfigs()
        {
            var fields = new List<BaseFieldConfig>();

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    string sql = "SELECT FieldName, FieldType, DefaultValue, IsRequired, IsReadOnly, SortOrder, Description FROM BaseFields ORDER BY SortOrder";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            fields.Add(new BaseFieldConfig
                            {
                                FieldName = reader.GetString(0),
                                FieldType = reader.GetString(1),
                                DefaultValue = reader.IsDBNull(2) ? null : reader.GetString(2),
                                IsRequired = reader.GetInt32(3) == 1,
                                IsReadOnly = reader.GetInt32(4) == 1,
                                SortOrder = reader.GetInt32(5),
                                Description = reader.IsDBNull(6) ? null : reader.GetString(6)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载基类字段失败: {ex.Message}");
            }

            return fields;
        }

        /// <summary>
        /// 字符串类型转 .NET 类型
        /// </summary>
        private static Type GetFieldType(string typeName)
        {
            switch (typeName.ToLower())
            {
                case "string": return typeof(string);
                case "int": return typeof(int);
                case "long": return typeof(long);
                case "decimal": return typeof(decimal);
                case "float": return typeof(float);
                case "double": return typeof(double);
                case "datetime": return typeof(DateTime);
                case "bool": return typeof(bool);
                default: return typeof(string);
            }
        }

        /// <summary>
        /// 基类字段配置
        /// </summary>
        private class BaseFieldConfig
        {
            public string FieldName { get; set; }
            public string FieldType { get; set; }
            public string DefaultValue { get; set; }
            public bool IsRequired { get; set; }
            public bool IsReadOnly { get; set; }
            public int SortOrder { get; set; }
            public string Description { get; set; }
        }
    }
}