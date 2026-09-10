using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingLayoutTests
    {
        [Fact]
        public void Mapping_header_reserves_two_lines_for_machine_choices()
        {
            float machineRowHeight = 0;
            int headerHeight = 0;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        var headerField = typeof(ModelConfigForm).GetField(
                            "panel2",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        var header = Assert.IsType<Panel>(headerField.GetValue(form));
                        var layout = Assert.IsType<TableLayoutPanel>(header.Controls[0]);

                        machineRowHeight = layout.RowStyles[3].Height;
                        headerHeight = header.Height;
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
            Assert.True(machineRowHeight >= 64F, "适用机台区域应至少容纳两行复选框。");
            Assert.True(headerHeight >= 170, "顶部配置区应完整包含适用机台区域。");
        }

        [Fact]
        public void Mapping_sample_area_is_between_header_and_footer()
        {
            int headerBottom = 0;
            int sampleTop = 0;
            int sampleBottom = 0;
            int footerTop = 0;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var header = Assert.IsType<Panel>(
                            typeof(ModelConfigForm).GetField("panel2", Flags).GetValue(form));
                        var footer = Assert.IsType<Panel>(
                            typeof(ModelConfigForm).GetField("panel3", Flags).GetValue(form));
                        var sampleArea = Assert.IsType<SplitContainer>(
                            typeof(ModelConfigForm).GetField("_mappingCenterSplit", Flags).GetValue(form));
                        var editorPanel = Assert.IsType<SplitContainer>(
                            typeof(ModelConfigForm).GetField("splitContainer3", Flags).GetValue(form)).Panel2;

                        editorPanel.PerformLayout();
                        headerBottom = header.Bottom;
                        sampleTop = sampleArea.Top;
                        sampleBottom = sampleArea.Bottom;
                        footerTop = footer.Top;
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
            Assert.True(sampleTop >= headerBottom, "样本区域不得被顶部配置区遮挡。");
            Assert.True(sampleBottom <= footerTop, "样本区域不得进入底部保存栏。");
        }

        [Fact]
        public void Mapping_sample_drag_detects_all_four_scroll_edges()
        {
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo method = typeof(ModelConfigForm).GetMethod(
                "GetMappingSampleAutoScrollDirection",
                Flags);
            Assert.NotNull(method);

            var viewport = new Rectangle(56, 24, 320, 200);
            Assert.Equal(new Point(-1, 0), InvokeDirection(method, new Point(60, 100), viewport));
            Assert.Equal(new Point(1, 0), InvokeDirection(method, new Point(372, 100), viewport));
            Assert.Equal(new Point(0, -1), InvokeDirection(method, new Point(200, 28), viewport));
            Assert.Equal(new Point(0, 1), InvokeDirection(method, new Point(200, 220), viewport));
            Assert.Equal(Point.Empty, InvokeDirection(method, new Point(200, 100), viewport));
        }

        [Fact]
        public void Mapping_sample_drag_keeps_a_rectangular_selection()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var grid = Assert.IsType<DataGridView>(
                            typeof(ModelConfigForm).GetField("_mappingSampleGrid", Flags).GetValue(form));
                        for (int column = 0; column < 8; column++)
                            grid.Columns.Add("C" + column, "C" + column);
                        grid.Rows.Add(4);

                        MethodInfo method = typeof(ModelConfigForm).GetMethod(
                            "SelectMappingSampleRange",
                            Flags);
                        Assert.NotNull(method);
                        method.Invoke(form, new object[] { 1, 2, 2, 6 });

                        Assert.Equal(10, grid.SelectedCells.Count);
                        Assert.True(grid.Rows[1].Cells[2].Selected);
                        Assert.True(grid.Rows[2].Cells[6].Selected);
                        Assert.False(grid.Rows[0].Cells[2].Selected);
                        Assert.False(grid.Rows[1].Cells[7].Selected);
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
        }

        [Fact]
        public void Repeated_row_mapping_defaults_follow_model_field_order()
        {
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo method = typeof(ModelConfigForm).GetMethod(
                "GetRepeatedRowDefaultTarget",
                Flags);
            Assert.NotNull(method);

            var modelFieldsInConfiguredOrder = new List<string>
            {
                "FirstConfiguredField",
                "SecondConfiguredField",
                "ThirdConfiguredField"
            };

            Assert.Equal("FirstConfiguredField", InvokeDefaultTarget(method, modelFieldsInConfiguredOrder, 0));
            Assert.Equal("SecondConfiguredField", InvokeDefaultTarget(method, modelFieldsInConfiguredOrder, 1));
            Assert.Equal("ThirdConfiguredField", InvokeDefaultTarget(method, modelFieldsInConfiguredOrder, 2));
            Assert.Equal("（忽略）", InvokeDefaultTarget(method, modelFieldsInConfiguredOrder, 3));
        }

        [Fact]
        public void Filename_locator_built_by_editor_does_not_keep_worksheet_offsets()
        {
            MappingLocator locator = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var grid = Assert.IsType<DataGridView>(
                            typeof(ModelConfigForm).GetField("dataGridView1", Flags).GetValue(form));
                        int rowIndex = grid.Rows.Add();
                        DataGridViewRow row = grid.Rows[rowIndex];
                        row.Cells["MappingTargetField"].Value = "FileName";
                        row.Cells["MappingTargetType"].Value = "string";
                        row.Cells["MappingRequired"].Value = true;
                        row.Cells["MappingDescription"].Value = "file";
                        row.Cells["MappingScope"].Value = "公共字段";
                        row.Cells["MappingLocatorType"].Value = "fileNameFull";
                        row.Cells["MappingLocatorValue"].Value = string.Empty;
                        row.Cells["MappingRowOffset"].Value = "7";
                        row.Cells["MappingColumnOffset"].Value = "1";
                        row.Cells["MappingValueColumn"].Value = "B";
                        row.Cells["MappingDataRowOffset"].Value = "1";
                        row.Cells["MappingTransforms"].Value = string.Empty;
                        row.Cells["MappingValueMap"].Value = string.Empty;
                        row.Cells["MappingDefaultValue"].Value = string.Empty;

                        MethodInfo method = typeof(ModelConfigForm).GetMethod(
                            "CreateMappingFieldRule",
                            Flags);
                        Assert.NotNull(method);
                        var field = Assert.IsType<FieldMappingRule>(method.Invoke(form, new object[] { row }));
                        locator = field.Locator;
                    }
                }
                catch (TargetInvocationException ex)
                {
                    failure = ex.InnerException ?? ex;
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
            Assert.NotNull(locator);
            Assert.Equal(0, locator.RowOffset);
            Assert.Equal(0, locator.ColumnOffset);
            Assert.Equal(0, locator.DataRowOffset);
            Assert.True(string.IsNullOrEmpty(locator.ValueColumn));
        }

        [Fact]
        public void Mapping_editor_exposes_image_filename_mode_and_archive_settings()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var mode = Assert.IsType<ComboBox>(
                            typeof(ModelConfigForm).GetField("_mappingModeCombo", Flags).GetValue(form));
                        Assert.Contains(mode.Items.Cast<object>(), item =>
                            string.Equals(item.ToString(), "图片文件名", StringComparison.Ordinal));
                        Assert.IsType<TextBox>(
                            typeof(ModelConfigForm).GetField("_mappingImageRootTextBox", Flags).GetValue(form));
                        Assert.IsType<ComboBox>(
                            typeof(ModelConfigForm).GetField("_mappingImagePathFieldCombo", Flags).GetValue(form));
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
        }

        [Fact]
        public void Mapping_editor_exposes_explicit_csv_encoding_and_delimiter_settings()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var encoding = Assert.IsType<ComboBox>(
                            typeof(ModelConfigForm).GetField("_mappingCsvEncodingCombo", Flags).GetValue(form));
                        var delimiter = Assert.IsType<ComboBox>(
                            typeof(ModelConfigForm).GetField("_mappingCsvDelimiterCombo", Flags).GetValue(form));
                        var headerRow = Assert.IsType<NumericUpDown>(
                            typeof(ModelConfigForm).GetField("_mappingCsvHeaderRowNumber", Flags).GetValue(form));
                        var firstDataRow = Assert.IsType<NumericUpDown>(
                            typeof(ModelConfigForm).GetField("_mappingCsvFirstDataRowNumber", Flags).GetValue(form));

                        Assert.Equal(new[] { "UTF-8", "GB18030", "GBK" },
                            encoding.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                        Assert.Equal(new[] { "逗号 (,)", "分号 (;)", "Tab", "竖线 (|)" },
                            delimiter.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                        Assert.Equal(1m, headerRow.Value);
                        Assert.Equal(2m, firstDataRow.Value);
                        Assert.Equal(2000m, headerRow.Maximum);
                        Assert.Equal(2000m, firstDataRow.Maximum);
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
        }

        private static Point InvokeDirection(MethodInfo method, Point pointer, Rectangle viewport)
        {
            return (Point)method.Invoke(null, new object[] { pointer, viewport, 24 });
        }

        private static string InvokeDefaultTarget(
            MethodInfo method,
            IList<string> targetNames,
            int sourcePosition)
        {
            return (string)method.Invoke(null, new object[] { targetNames, sourcePosition });
        }
    }
}
