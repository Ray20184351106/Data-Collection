using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Mapping;
using SqlSugar;

namespace MachineDataAcquisitionSystem.Core
{
    public sealed class MasterDetailPersistenceService
    {
        public async Task<MasterDetailPersistenceResult> PersistAsync(
            SqlSugarClient db,
            MasterDetailParseResult aggregate,
            TargetTableDefinition masterDefinition,
            TargetTableDefinition detailDefinition,
            Func<long> nextId,
            CancellationToken cancellationToken)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
            if (masterDefinition == null) throw new ArgumentNullException(nameof(masterDefinition));
            if (detailDefinition == null) throw new ArgumentNullException(nameof(detailDefinition));
            if (nextId == null) throw new ArgumentNullException(nameof(nextId));
            if (aggregate.Master == null)
                throw new InvalidOperationException("主子表结果缺少主表记录。");
            if (aggregate.Details == null || aggregate.Details.Count == 0)
                throw new InvalidOperationException("主子表结果没有有效子表记录，禁止只插入主表。");

            cancellationToken.ThrowIfCancellationRequested();
            ValidateAggregate(aggregate);
            MarkParentCidColumn(detailDefinition, aggregate.ParentCidField);

            var provisioner = new TargetTableProvisioner();
            provisioner.EnsureTable(db, aggregate.Master.GetType(), masterDefinition);
            provisioner.EnsureTable(db, aggregate.Details[0].GetType(), detailDefinition);

            cancellationToken.ThrowIfCancellationRequested();
            ModelIdentityInitializer.EnsureCid(aggregate.Master, nextId);
            long masterCid = GetCid(aggregate.Master);
            PropertyInfo parentProperty = GetParentProperty(
                aggregate.Details[0].GetType(),
                aggregate.ParentCidField);

            var details = new List<object>(aggregate.Details.Count);
            foreach (object detail in aggregate.Details)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ModelIdentityInitializer.EnsureCid(detail, nextId);
                parentProperty.SetValue(detail, masterCid, null);
                details.Add(detail);
            }

            db.Ado.BeginTran();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                int masterRows = await db.InsertableByObject(aggregate.Master).ExecuteCommandAsync();
                if (masterRows != 1)
                    throw new InvalidOperationException("主表插入结果异常，预期 1 条，实际 " + masterRows + " 条。");

                cancellationToken.ThrowIfCancellationRequested();
                int detailRows = await db.InsertableByObject(details).ExecuteCommandAsync();
                if (detailRows != details.Count)
                    throw new InvalidOperationException(
                        "子表插入结果异常，预期 " + details.Count + " 条，实际 " + detailRows + " 条。");

                cancellationToken.ThrowIfCancellationRequested();
                db.Ado.CommitTran();
                return new MasterDetailPersistenceResult(masterCid, detailRows);
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }

        private static void ValidateAggregate(MasterDetailParseResult aggregate)
        {
            Type detailType = aggregate.Details[0].GetType();
            foreach (object detail in aggregate.Details)
            {
                if (detail == null)
                    throw new InvalidOperationException("主子表结果包含空的子表记录。");
                if (detail.GetType() != detailType)
                    throw new InvalidOperationException("主子表结果包含不同类型的子表记录。");
            }
            GetParentProperty(detailType, aggregate.ParentCidField);
        }

        private static void MarkParentCidColumn(
            TargetTableDefinition detailDefinition,
            string parentCidField)
        {
            TargetTableColumnDefinition configured = null;
            foreach (TargetTableColumnDefinition column in detailDefinition.Columns)
            {
                if (!string.Equals(column.FieldName, parentCidField, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (configured != null)
                    throw new InvalidOperationException("子表定义包含重复的关联字段：" + parentCidField);
                configured = column;
            }
            if (configured == null)
                throw new InvalidOperationException("子表定义缺少关联字段：" + parentCidField);
            if (!string.Equals(configured.FieldType, "long", StringComparison.OrdinalIgnoreCase) ||
                configured.IsRequired || configured.IsPrimaryKey || configured.IsIdentity)
                throw new InvalidOperationException("子表关联字段必须是可空 long，且不能是主键或自增字段。");
            configured.IsSystemGenerated = true;
            configured.SystemRole = "ParentCid";
        }

        private static PropertyInfo GetParentProperty(Type detailType, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new InvalidOperationException("主子表规则缺少关联字段。");
            PropertyInfo property = detailType.GetProperty(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            Type valueType = property == null
                ? null
                : Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (property == null || !property.CanWrite || valueType != typeof(long))
                throw new InvalidOperationException("子模型缺少可写的 long 类型关联字段：" + fieldName);
            return property;
        }

        private static long GetCid(object model)
        {
            PropertyInfo property = model.GetType().GetProperty(
                "CID",
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(long) || !property.CanRead)
                throw new InvalidOperationException("主模型必须具有可读写的 long CID 字段。");
            long value = (long)property.GetValue(model, null);
            if (value <= 0)
                throw new InvalidOperationException("主表 CID 必须是正整数。");
            return value;
        }
    }

    public sealed class MasterDetailPersistenceResult
    {
        public MasterDetailPersistenceResult(long masterCid, int detailCount)
        {
            MasterCid = masterCid;
            DetailCount = detailCount;
        }

        public long MasterCid { get; private set; }
        public int DetailCount { get; private set; }
    }
}
