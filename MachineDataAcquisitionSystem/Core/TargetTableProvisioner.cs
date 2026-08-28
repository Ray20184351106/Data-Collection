using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using SqlSugar;

namespace MachineDataAcquisitionSystem.Core
{
    public sealed class TargetTableProvisioner
    {
        private static readonly ConcurrentDictionary<string, object> TableLocks =
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

        public TargetTableProvisionResult EnsureTable(
            SqlSugarClient db,
            Type modelType,
            TargetTableDefinition definition)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (modelType == null) throw new ArgumentNullException(nameof(modelType));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definition.Validate();

            SugarTable tableAttribute = modelType
                .GetCustomAttributes(typeof(SugarTable), true)
                .OfType<SugarTable>()
                .SingleOrDefault();
            if (tableAttribute == null || string.IsNullOrWhiteSpace(tableAttribute.TableName))
            {
                throw new InvalidOperationException(
                    "解析结果模型缺少已配置的目标表映射，禁止自动创建数据库表。");
            }

            string tableName = tableAttribute.TableName.Trim();
            if (!string.Equals(tableName, definition.TableName, StringComparison.Ordinal))
                throw new InvalidOperationException("解析结果模型的目标表与数据模型配置不一致。");
            foreach (TargetTableColumnDefinition column in definition.Columns)
            {
                if (modelType.GetProperty(column.FieldName, BindingFlags.Instance | BindingFlags.Public) == null)
                    throw new InvalidOperationException("解析结果模型缺少配置字段：" + column.FieldName);
            }
            if (db.DbMaintenance.IsAnyTable(tableName, false))
                return new TargetTableProvisionResult(
                    tableName,
                    false,
                    ReconcileExistingTable(db, definition));

            string lockKey = string.Concat(
                db.CurrentConnectionConfig.DbType,
                "|",
                db.CurrentConnectionConfig.ConnectionString,
                "|",
                tableName);
            object tableLock = TableLocks.GetOrAdd(lockKey, _ => new object());
            lock (tableLock)
            {
                if (db.DbMaintenance.IsAnyTable(tableName, false))
                    return new TargetTableProvisionResult(
                        tableName,
                        false,
                        ReconcileExistingTable(db, definition));

                if (db.CurrentConnectionConfig.MoreSettings == null)
                    db.CurrentConnectionConfig.MoreSettings = new ConnMoreSettings();
                db.CurrentConnectionConfig.MoreSettings.IsNoReadXmlDescription = true;
                db.CodeFirst.InitTables(TargetTableTypeBuilder.Build(definition));
                if (!db.DbMaintenance.IsAnyTable(tableName, false))
                    throw new InvalidOperationException("自动创建目标表失败：" + tableName);

                EnsureSystemIndexes(db, definition);
                return new TargetTableProvisionResult(tableName, true);
            }
        }

        private static int ReconcileExistingTable(
            SqlSugarClient db,
            TargetTableDefinition definition)
        {
            Dictionary<string, DbColumnInfo> existingColumns = db.DbMaintenance
                .GetColumnInfosByTableName(definition.TableName, false)
                .ToDictionary(column => column.DbColumnName, StringComparer.OrdinalIgnoreCase);
            int adjusted = 0;
            foreach (TargetTableColumnDefinition configured in definition.Columns)
            {
                DbColumnInfo existing;
                if (!existingColumns.TryGetValue(configured.FieldName, out existing))
                {
                    if (!IsParentCidColumn(configured))
                        throw new InvalidOperationException(
                            "主数据库目标表缺少配置字段：" + configured.FieldName);

                    if (!db.DbMaintenance.AddColumn(
                        definition.TableName,
                        CreateParentCidColumn(db, definition.TableName, configured)))
                    {
                        throw new InvalidOperationException(
                            "自动增加子表关联字段失败：" + configured.FieldName);
                    }
                    adjusted++;
                    continue;
                }

                bool desiredNullable = !configured.IsRequired && !configured.IsPrimaryKey;
                if (existing.IsPrimarykey != configured.IsPrimaryKey)
                    throw new InvalidOperationException(
                        "主数据库字段的主键配置不一致，不能自动安全修改：" + configured.FieldName);
                if (existing.IsIdentity != configured.IsIdentity)
                    throw new InvalidOperationException(
                        "主数据库字段的自增配置不一致，不能自动安全修改：" + configured.FieldName);
                if (!desiredNullable && existing.IsNullable)
                    throw new InvalidOperationException(
                        "主数据库字段当前允许为空，无法在保留数据的前提下自动改为必填：" + configured.FieldName);

                bool needsUpdate = false;
                if (desiredNullable && !existing.IsNullable)
                {
                    existing.IsNullable = true;
                    needsUpdate = true;
                }
                if (configured.FieldLength > 0 &&
                    existing.Length > 0 &&
                    existing.Length < configured.FieldLength)
                {
                    existing.Length = configured.FieldLength;
                    needsUpdate = true;
                }
                if (!needsUpdate) continue;

                if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                {
                    throw new InvalidOperationException(
                        "SQLite 已有表不支持安全原地修改字段可空性或长度；请人工迁移该表：" + configured.FieldName);
                }

                if (!db.DbMaintenance.UpdateColumn(definition.TableName, existing))
                    throw new InvalidOperationException("自动修正目标表字段失败：" + configured.FieldName);
                adjusted++;
            }

            EnsureSystemIndexes(db, definition);

            return adjusted;
        }

        private static bool IsParentCidColumn(TargetTableColumnDefinition column)
        {
            return column.IsSystemGenerated &&
                string.Equals(column.SystemRole, "ParentCid", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(column.FieldType, "long", StringComparison.OrdinalIgnoreCase) &&
                !column.IsRequired &&
                !column.IsPrimaryKey &&
                !column.IsIdentity;
        }

        private static DbColumnInfo CreateParentCidColumn(
            SqlSugarClient db,
            string tableName,
            TargetTableColumnDefinition column)
        {
            string dataType;
            switch (db.CurrentConnectionConfig.DbType)
            {
                case DbType.Sqlite: dataType = "INTEGER"; break;
                case DbType.MySql: dataType = "BIGINT"; break;
                case DbType.PostgreSQL: dataType = "int8"; break;
                default: dataType = "BIGINT"; break;
            }

            return new DbColumnInfo
            {
                TableName = tableName,
                DbColumnName = column.FieldName,
                PropertyName = column.FieldName,
                PropertyType = typeof(long?),
                DataType = dataType,
                IsNullable = true,
                IsIdentity = false,
                IsPrimarykey = false,
                ColumnDescription = column.Description ?? "主表CID"
            };
        }

        private static void EnsureSystemIndexes(
            SqlSugarClient db,
            TargetTableDefinition definition)
        {
            foreach (TargetTableColumnDefinition column in definition.Columns.Where(IsParentCidColumn))
            {
                string indexName = "IX_" + definition.TableName + "_" + column.FieldName;
                if (db.DbMaintenance.IsAnyIndex(indexName)) continue;
                if (!db.DbMaintenance.CreateIndex(
                    definition.TableName,
                    new[] { column.FieldName },
                    indexName,
                    false))
                {
                    throw new InvalidOperationException("自动创建子表关联索引失败：" + indexName);
                }
            }
        }
    }

    public sealed class TargetTableDefinition
    {
        public TargetTableDefinition()
        {
            Columns = new List<TargetTableColumnDefinition>();
        }

        public string TableName { get; set; }
        public IReadOnlyCollection<TargetTableColumnDefinition> Columns { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TableName))
                throw new InvalidOperationException("数据模型缺少目标表名。");
            if (!IsSafeIdentifier(TableName))
                throw new InvalidOperationException("目标表名不是安全的数据库标识符：" + TableName);
            if (Columns == null || Columns.Count == 0)
                throw new InvalidOperationException("数据模型没有可用于建表的字段。");
            if (Columns.Any(column => column == null || string.IsNullOrWhiteSpace(column.FieldName)))
                throw new InvalidOperationException("数据模型包含无效字段。");
            if (Columns.Any(column => !IsSafeIdentifier(column.FieldName)))
                throw new InvalidOperationException("数据模型包含不安全的字段名。");
            if (Columns.GroupBy(column => column.FieldName, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new InvalidOperationException("数据模型包含重复字段。");
            if (Columns.Any(column => column.IsSystemGenerated &&
                !string.Equals(column.SystemRole, "ParentCid", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("数据模型包含未知的系统字段角色。");
            if (Columns.Any(column => column.IsSystemGenerated &&
                string.Equals(column.SystemRole, "ParentCid", StringComparison.OrdinalIgnoreCase) &&
                (!string.Equals(column.FieldType, "long", StringComparison.OrdinalIgnoreCase) ||
                 column.IsRequired || column.IsPrimaryKey || column.IsIdentity)))
                throw new InvalidOperationException("子表关联字段必须是可空 long，且不能是主键或自增字段。");
        }

        private static bool IsSafeIdentifier(string value)
        {
            return Regex.IsMatch(
                (value ?? string.Empty).Trim(),
                @"^[\p{L}_][\p{L}\p{Nd}_]{0,127}$",
                RegexOptions.CultureInvariant);
        }
    }

    public sealed class TargetTableColumnDefinition
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public int FieldLength { get; set; }
        public bool IsRequired { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public string Description { get; set; }
        public bool IsSystemGenerated { get; set; }
        public string SystemRole { get; set; }
    }

    internal static class TargetTableTypeBuilder
    {
        public static Type Build(TargetTableDefinition definition)
        {
            var assemblyName = new AssemblyName(
                "TargetTableSchema_" + Guid.NewGuid().ToString("N"));
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName,
                AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name);
            TypeBuilder type = module.DefineType(
                "Provisioning." + assemblyName.Name,
                TypeAttributes.Public | TypeAttributes.Class);

            ConstructorInfo tableConstructor = typeof(SugarTable).GetConstructor(new[] { typeof(string) });
            type.SetCustomAttribute(new CustomAttributeBuilder(
                tableConstructor,
                new object[] { definition.TableName }));

            foreach (TargetTableColumnDefinition column in definition.Columns)
                AddProperty(type, column);

            return type.CreateType();
        }

        private static void AddProperty(TypeBuilder type, TargetTableColumnDefinition column)
        {
            Type propertyType = GetFieldType(column.FieldType);
            if (!column.IsRequired && !column.IsPrimaryKey && propertyType.IsValueType)
                propertyType = typeof(Nullable<>).MakeGenericType(propertyType);

            FieldBuilder field = type.DefineField(
                "_" + column.FieldName,
                propertyType,
                FieldAttributes.Private);
            PropertyBuilder property = type.DefineProperty(
                column.FieldName,
                PropertyAttributes.None,
                propertyType,
                null);
            MethodBuilder getter = type.DefineMethod(
                "get_" + column.FieldName,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                propertyType,
                Type.EmptyTypes);
            ILGenerator getIl = getter.GetILGenerator();
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, field);
            getIl.Emit(OpCodes.Ret);
            MethodBuilder setter = type.DefineMethod(
                "set_" + column.FieldName,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null,
                new[] { propertyType });
            ILGenerator setIl = setter.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, field);
            setIl.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);

            ConstructorInfo columnConstructor = typeof(SugarColumn).GetConstructor(Type.EmptyTypes);
            var properties = new List<PropertyInfo>
            {
                typeof(SugarColumn).GetProperty("IsNullable"),
                typeof(SugarColumn).GetProperty("IsPrimaryKey"),
                typeof(SugarColumn).GetProperty("IsIdentity"),
                typeof(SugarColumn).GetProperty("Length"),
                typeof(SugarColumn).GetProperty("ColumnDescription")
            };
            var values = new object[]
            {
                !column.IsRequired && !column.IsPrimaryKey,
                column.IsPrimaryKey,
                column.IsIdentity,
                Math.Max(0, column.FieldLength),
                column.Description ?? string.Empty
            };
            property.SetCustomAttribute(new CustomAttributeBuilder(
                columnConstructor,
                new object[0],
                properties.ToArray(),
                values));
        }

        private static Type GetFieldType(string fieldType)
        {
            switch ((fieldType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "int": return typeof(int);
                case "long": return typeof(long);
                case "decimal": return typeof(decimal);
                case "float": return typeof(float);
                case "double": return typeof(double);
                case "datetime": return typeof(DateTime);
                case "bool": return typeof(bool);
                case "string": return typeof(string);
                default: throw new InvalidOperationException("不支持的数据模型字段类型：" + fieldType);
            }
        }
    }

    public sealed class TargetTableProvisionResult
    {
        public TargetTableProvisionResult(
            string tableName,
            bool created,
            int adjustedColumnCount = 0)
        {
            TableName = tableName;
            Created = created;
            AdjustedColumnCount = adjustedColumnCount;
        }

        public string TableName { get; private set; }
        public bool Created { get; private set; }
        public int AdjustedColumnCount { get; private set; }
    }
}
