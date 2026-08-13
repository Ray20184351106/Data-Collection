using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingDirtyStateTests
    {
        [Fact]
        public void Unchanged_grid_event_after_loading_does_not_mark_mapping_dirty()
        {
            bool dirty = true;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        DataGridView grid = PrepareSingleMappingRow(form, "labelOffset");
                        Invoke(form, "AcceptMappingEditorStateAsClean");

                        Invoke(form, "MappingGrid_CellValueChanged", grid,
                            new DataGridViewCellEventArgs(grid.Columns["MappingLocatorType"].Index, 0));

                        dirty = GetField<bool>(form, "_mappingDirty");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.False(dirty);
        }

        [Fact]
        public void Changed_grid_value_after_loading_marks_mapping_dirty()
        {
            bool dirty = false;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        DataGridView grid = PrepareSingleMappingRow(form, "labelOffset");
                        Invoke(form, "AcceptMappingEditorStateAsClean");
                        SetField(form, "_mappingSuppressEvents", true);
                        grid.Rows[0].Cells["MappingLocatorType"].Value = "cell";
                        SetField(form, "_mappingSuppressEvents", false);

                        Invoke(form, "MappingGrid_CellValueChanged", grid,
                            new DataGridViewCellEventArgs(grid.Columns["MappingLocatorType"].Index, 0));

                        dirty = GetField<bool>(form, "_mappingDirty");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.True(dirty);
        }

        [Fact]
        public void Nested_sheet_loading_preserves_the_outer_event_suppression_scope()
        {
            bool suppressEvents = false;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        SetField(form, "_mappingSuppressEvents", true);

                        Invoke(form, "LoadMappingSheetChoices", "Data");

                        suppressEvents = GetField<bool>(form, "_mappingSuppressEvents");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.True(suppressEvents);
        }

        private static DataGridView PrepareSingleMappingRow(ModelConfigForm form, string locatorType)
        {
            DataGridView grid = GetField<DataGridView>(form, "dataGridView1");
            SetField(form, "_mappingSuppressEvents", true);
            int rowIndex = grid.Rows.Add();
            grid.Rows[rowIndex].Cells["MappingTargetField"].Value = "SerialNumber";
            grid.Rows[rowIndex].Cells["MappingTargetType"].Value = "string";
            grid.Rows[rowIndex].Cells["MappingLocatorType"].Value = locatorType;
            grid.Rows[rowIndex].Cells["MappingLocatorValue"].Value = "Serial";
            SetField(form, "_mappingSuppressEvents", false);
            SetField(form, "_mappingDirty", false);
            return grid;
        }

        private static void Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(target, arguments);
        }

        private static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }
    }
}
