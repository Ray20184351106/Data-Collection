namespace MachineDataAcquisitionSystem.Forms
{
    partial class ConfigForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConfigForm));
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.Id = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Names = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MonitorPath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SuccessPath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ErrorPath = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.tabPage5 = new System.Windows.Forms.TabPage();
            this.btnSave = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.txtSearchDb = new System.Windows.Forms.TextBox();
            this.btnAddDb = new System.Windows.Forms.Button();
            this.listViewDb = new System.Windows.Forms.ListView();
            this.colName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colStatus = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colInfo = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.propertyGridDb = new System.Windows.Forms.PropertyGrid();
            this.lblDbCount = new System.Windows.Forms.Label();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.btnTestConn = new System.Windows.Forms.Button();
            this.btnSaveDb = new System.Windows.Forms.Button();
            this.lblDbStatus = new System.Windows.Forms.Label();
            this.contextMenuDb = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuDelete = new System.Windows.Forms.ToolStripMenuItem();
            this.menuCopy = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSetPrimary = new System.Windows.Forms.ToolStripMenuItem();
            this.menuTestConn = new System.Windows.Forms.ToolStripMenuItem();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.tabPage2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.contextMenuDb.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage4);
            this.tabControl1.Controls.Add(this.tabPage5);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("宋体", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1148, 713);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.panel1);
            this.tabPage1.Controls.Add(this.dataGridView1);
            this.tabPage1.Location = new System.Drawing.Point(4, 26);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage1.Size = new System.Drawing.Size(1140, 683);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "路径配置";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.btnSave);
            this.panel1.Controls.Add(this.btnCancel);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(4, 579);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1132, 100);
            this.panel1.TabIndex = 1;
            // 
            // btnCancel
            // 
            this.btnCancel.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.Location = new System.Drawing.Point(996, 23);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(116, 58);
            this.btnCancel.TabIndex = 0;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // dataGridView1
            // 
            this.dataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView1.BackgroundColor = System.Drawing.Color.White;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Id,
            this.Names,
            this.MonitorPath,
            this.SuccessPath,
            this.ErrorPath});
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(4, 4);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowTemplate.Height = 23;
            this.dataGridView1.Size = new System.Drawing.Size(1132, 675);
            this.dataGridView1.TabIndex = 0;
            // 
            // Id
            // 
            this.Id.FillWeight = 50F;
            this.Id.HeaderText = "机台号";
            this.Id.Name = "Id";
            // 
            // Names
            // 
            this.Names.FillWeight = 50F;
            this.Names.HeaderText = "机台名称";
            this.Names.Name = "Names";
            // 
            // MonitorPath
            // 
            this.MonitorPath.HeaderText = "监控目录";
            this.MonitorPath.MinimumWidth = 10;
            this.MonitorPath.Name = "MonitorPath";
            // 
            // SuccessPath
            // 
            this.SuccessPath.HeaderText = "成功目录";
            this.SuccessPath.MinimumWidth = 10;
            this.SuccessPath.Name = "SuccessPath";
            // 
            // ErrorPath
            // 
            this.ErrorPath.HeaderText = "失败目录";
            this.ErrorPath.MinimumWidth = 10;
            this.ErrorPath.Name = "ErrorPath";
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.splitContainer1);
            this.tabPage2.Location = new System.Drawing.Point(4, 26);
            this.tabPage2.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage2.Size = new System.Drawing.Size(1140, 683);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "数据库配置";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            this.tabPage3.Location = new System.Drawing.Point(4, 26);
            this.tabPage3.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage3.Size = new System.Drawing.Size(1140, 683);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "预警配置";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // tabPage4
            // 
            this.tabPage4.Location = new System.Drawing.Point(4, 26);
            this.tabPage4.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage4.Size = new System.Drawing.Size(1140, 683);
            this.tabPage4.TabIndex = 3;
            this.tabPage4.Text = "日志配置";
            this.tabPage4.UseVisualStyleBackColor = true;
            // 
            // tabPage5
            // 
            this.tabPage5.Location = new System.Drawing.Point(4, 26);
            this.tabPage5.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage5.Name = "tabPage5";
            this.tabPage5.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage5.Size = new System.Drawing.Size(1140, 683);
            this.tabPage5.TabIndex = 4;
            this.tabPage5.Text = "高级配置";
            this.tabPage5.UseVisualStyleBackColor = true;
            // 
            // btnSave
            // 
            this.btnSave.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSave.Location = new System.Drawing.Point(822, 23);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(116, 58);
            this.btnSave.TabIndex = 1;
            this.btnSave.Text = "提交";
            this.btnSave.UseVisualStyleBackColor = true;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(4, 4);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.lblDbCount);
            this.splitContainer1.Panel1.Controls.Add(this.listViewDb);
            this.splitContainer1.Panel1.Controls.Add(this.btnAddDb);
            this.splitContainer1.Panel1.Controls.Add(this.txtSearchDb);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.panelButtons);
            this.splitContainer1.Panel2.Controls.Add(this.propertyGridDb);
            this.splitContainer1.Size = new System.Drawing.Size(1132, 675);
            this.splitContainer1.SplitterDistance = 367;
            this.splitContainer1.TabIndex = 0;
            // 
            // txtSearchDb
            // 
            this.txtSearchDb.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtSearchDb.Location = new System.Drawing.Point(0, 0);
            this.txtSearchDb.Name = "txtSearchDb";
            this.txtSearchDb.Size = new System.Drawing.Size(367, 26);
            this.txtSearchDb.TabIndex = 0;
            // 
            // btnAddDb
            // 
            this.btnAddDb.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnAddDb.Location = new System.Drawing.Point(0, 26);
            this.btnAddDb.Name = "btnAddDb";
            this.btnAddDb.Size = new System.Drawing.Size(367, 32);
            this.btnAddDb.TabIndex = 2;
            this.btnAddDb.Text = "+ 添加数据库";
            this.btnAddDb.UseVisualStyleBackColor = true;
            // 
            // listViewDb
            // 
            this.listViewDb.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colName,
            this.colStatus,
            this.colInfo});
            this.listViewDb.ContextMenuStrip = this.contextMenuDb;
            this.listViewDb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listViewDb.FullRowSelect = true;
            this.listViewDb.HideSelection = false;
            this.listViewDb.Location = new System.Drawing.Point(0, 58);
            this.listViewDb.MultiSelect = false;
            this.listViewDb.Name = "listViewDb";
            this.listViewDb.Size = new System.Drawing.Size(367, 617);
            this.listViewDb.TabIndex = 3;
            this.listViewDb.UseCompatibleStateImageBehavior = false;
            this.listViewDb.View = System.Windows.Forms.View.Details;
            this.listViewDb.SelectedIndexChanged += new System.EventHandler(this.ListViewDb_SelectedIndexChanged);
            // 
            // colName
            // 
            this.colName.Text = "名称";
            this.colName.Width = 80;
            // 
            // colStatus
            // 
            this.colStatus.Text = "状态";
            this.colStatus.Width = 70;
            // 
            // colInfo
            // 
            this.colInfo.Text = "信息";
            this.colInfo.Width = 232;
            // 
            // propertyGridDb
            // 
            this.propertyGridDb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.propertyGridDb.Location = new System.Drawing.Point(0, 0);
            this.propertyGridDb.Name = "propertyGridDb";
            this.propertyGridDb.PropertySort = System.Windows.Forms.PropertySort.Categorized;
            this.propertyGridDb.Size = new System.Drawing.Size(761, 675);
            this.propertyGridDb.TabIndex = 0;
            this.propertyGridDb.ToolbarVisible = false;
            // 
            // lblDbCount
            // 
            this.lblDbCount.AutoSize = true;
            this.lblDbCount.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblDbCount.Location = new System.Drawing.Point(0, 659);
            this.lblDbCount.Name = "lblDbCount";
            this.lblDbCount.Size = new System.Drawing.Size(95, 16);
            this.lblDbCount.TabIndex = 4;
            this.lblDbCount.Text = "共0个数据库";
            this.lblDbCount.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panelButtons
            // 
            this.panelButtons.Controls.Add(this.lblDbStatus);
            this.panelButtons.Controls.Add(this.btnSaveDb);
            this.panelButtons.Controls.Add(this.btnTestConn);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.Location = new System.Drawing.Point(0, 580);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(761, 95);
            this.panelButtons.TabIndex = 1;
            // 
            // btnTestConn
            // 
            this.btnTestConn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnTestConn.Location = new System.Drawing.Point(82, 12);
            this.btnTestConn.Name = "btnTestConn";
            this.btnTestConn.Size = new System.Drawing.Size(115, 44);
            this.btnTestConn.TabIndex = 2;
            this.btnTestConn.Text = "测试连接";
            this.btnTestConn.UseVisualStyleBackColor = true;
            this.btnTestConn.Click += new System.EventHandler(this.btnTestConn_Click);
            // 
            // btnSaveDb
            // 
            this.btnSaveDb.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSaveDb.Location = new System.Drawing.Point(329, 12);
            this.btnSaveDb.Name = "btnSaveDb";
            this.btnSaveDb.Size = new System.Drawing.Size(115, 44);
            this.btnSaveDb.TabIndex = 3;
            this.btnSaveDb.Text = "确定";
            this.btnSaveDb.UseVisualStyleBackColor = true;
            this.btnSaveDb.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // lblDbStatus
            // 
            this.lblDbStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblDbStatus.ForeColor = System.Drawing.Color.Blue;
            this.lblDbStatus.Location = new System.Drawing.Point(0, 65);
            this.lblDbStatus.Name = "lblDbStatus";
            this.lblDbStatus.Size = new System.Drawing.Size(761, 30);
            this.lblDbStatus.TabIndex = 4;
            this.lblDbStatus.Text = "就绪";
            // 
            // contextMenuDb
            // 
            this.contextMenuDb.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuEdit,
            this.menuDelete,
            this.menuCopy,
            this.toolStripMenuItem1,
            this.menuSetPrimary,
            this.menuTestConn});
            this.contextMenuDb.Name = "contextMenuDb";
            this.contextMenuDb.Size = new System.Drawing.Size(125, 136);
            // 
            // menuEdit
            // 
            this.menuEdit.Name = "menuEdit";
            this.menuEdit.Size = new System.Drawing.Size(124, 22);
            this.menuEdit.Text = "编辑";
            // 
            // menuDelete
            // 
            this.menuDelete.Name = "menuDelete";
            this.menuDelete.Size = new System.Drawing.Size(124, 22);
            this.menuDelete.Text = "删除";
            // 
            // menuCopy
            // 
            this.menuCopy.Name = "menuCopy";
            this.menuCopy.Size = new System.Drawing.Size(124, 22);
            this.menuCopy.Text = "复制";
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(124, 22);
            this.toolStripMenuItem1.Text = "-";
            // 
            // menuSetPrimary
            // 
            this.menuSetPrimary.Name = "menuSetPrimary";
            this.menuSetPrimary.Size = new System.Drawing.Size(124, 22);
            this.menuSetPrimary.Text = "设为默认";
            // 
            // menuTestConn
            // 
            this.menuTestConn.Name = "menuTestConn";
            this.menuTestConn.Size = new System.Drawing.Size(124, 22);
            this.menuTestConn.Text = "测试连接";
            // 
            // ConfigForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 14F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1148, 713);
            this.Controls.Add(this.tabControl1);
            this.Font = new System.Drawing.Font("宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "ConfigForm";
            this.Text = "系统配置";
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.tabPage2.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panelButtons.ResumeLayout(false);
            this.contextMenuDb.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TabPage tabPage4;
        private System.Windows.Forms.TabPage tabPage5;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.DataGridViewTextBoxColumn Id;
        private System.Windows.Forms.DataGridViewTextBoxColumn Names;
        private System.Windows.Forms.DataGridViewTextBoxColumn MonitorPath;
        private System.Windows.Forms.DataGridViewTextBoxColumn SuccessPath;
        private System.Windows.Forms.DataGridViewTextBoxColumn ErrorPath;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TextBox txtSearchDb;
        private System.Windows.Forms.ListView listViewDb;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colStatus;
        private System.Windows.Forms.ColumnHeader colInfo;
        private System.Windows.Forms.Button btnAddDb;
        private System.Windows.Forms.PropertyGrid propertyGridDb;
        private System.Windows.Forms.Label lblDbCount;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Button btnSaveDb;
        private System.Windows.Forms.Button btnTestConn;
        private System.Windows.Forms.Label lblDbStatus;
        private System.Windows.Forms.ContextMenuStrip contextMenuDb;
        private System.Windows.Forms.ToolStripMenuItem menuEdit;
        private System.Windows.Forms.ToolStripMenuItem menuDelete;
        private System.Windows.Forms.ToolStripMenuItem menuCopy;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem menuSetPrimary;
        private System.Windows.Forms.ToolStripMenuItem menuTestConn;
    }
}