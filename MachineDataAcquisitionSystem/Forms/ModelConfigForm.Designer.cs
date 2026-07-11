namespace MachineDataAcquisitionSystem.Forms
{
    partial class ModelConfigForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ModelConfigForm));
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPageModel = new System.Windows.Forms.TabPage();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.btnAddModel = new System.Windows.Forms.Button();
            this.listBoxModels = new System.Windows.Forms.ListBox();
            this.cmbParentModel = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.btnSaveModel = new System.Windows.Forms.Button();
            this.btnAddField = new System.Windows.Forms.Button();
            this.dgvFields = new System.Windows.Forms.DataGridView();
            this.colFieldName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFieldType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFieldLength = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIsRequired = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIsPrimaryKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIsIdentity = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDescription = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.chkIsActive = new System.Windows.Forms.CheckBox();
            this.txtDescription = new System.Windows.Forms.TextBox();
            this.txtTableName = new System.Windows.Forms.TextBox();
            this.txtModelName = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.tabPageScript = new System.Windows.Forms.TabPage();
            this.splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.listBoxScripts = new System.Windows.Forms.ListBox();
            this.btnAddScript = new System.Windows.Forms.Button();
            this.flowLayoutMachines = new System.Windows.Forms.FlowLayoutPanel();
            this.btnTxtTemplate = new System.Windows.Forms.Button();
            this.btnJsonTemplate = new System.Windows.Forms.Button();
            this.btnCsvTemplate = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnSaveScript = new System.Windows.Forms.Button();
            this.btnTestScript = new System.Windows.Forms.Button();
            this.btnExcelTemplate = new System.Windows.Forms.Button();
            this.rtxtScriptCode = new System.Windows.Forms.RichTextBox();
            this.chkScriptEnabled = new System.Windows.Forms.CheckBox();
            this.cmbScriptFileType = new System.Windows.Forms.ComboBox();
            this.cmbScriptModel = new System.Windows.Forms.ComboBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.txtScriptName = new System.Windows.Forms.TextBox();
            this.tabPageMapping = new System.Windows.Forms.TabPage();
            this.sqLiteCommandBuilder1 = new System.Data.SQLite.SQLiteCommandBuilder();
            this.errorProvider1 = new System.Windows.Forms.ErrorProvider(this.components);
            this.splitContainer3 = new System.Windows.Forms.SplitContainer();
            this.btnNewMappingScript = new System.Windows.Forms.Button();
            this.listBoxMappingScripts = new System.Windows.Forms.ListBox();
            this.panel2 = new System.Windows.Forms.Panel();
            this.panel3 = new System.Windows.Forms.Panel();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.tabControl1.SuspendLayout();
            this.tabPageModel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvFields)).BeginInit();
            this.tabPageScript.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).BeginInit();
            this.splitContainer2.Panel1.SuspendLayout();
            this.splitContainer2.Panel2.SuspendLayout();
            this.splitContainer2.SuspendLayout();
            this.panel1.SuspendLayout();
            this.tabPageMapping.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer3)).BeginInit();
            this.splitContainer3.Panel1.SuspendLayout();
            this.splitContainer3.Panel2.SuspendLayout();
            this.splitContainer3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPageModel);
            this.tabControl1.Controls.Add(this.tabPageScript);
            this.tabControl1.Controls.Add(this.tabPageMapping);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1255, 716);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPageModel
            // 
            this.tabPageModel.Controls.Add(this.splitContainer1);
            this.tabPageModel.Location = new System.Drawing.Point(4, 31);
            this.tabPageModel.Name = "tabPageModel";
            this.tabPageModel.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageModel.Size = new System.Drawing.Size(1247, 681);
            this.tabPageModel.TabIndex = 0;
            this.tabPageModel.Text = "数据模型";
            this.tabPageModel.UseVisualStyleBackColor = true;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(3, 3);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.btnAddModel);
            this.splitContainer1.Panel1.Controls.Add(this.listBoxModels);
            this.splitContainer1.Panel1MinSize = 200;
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.cmbParentModel);
            this.splitContainer1.Panel2.Controls.Add(this.label4);
            this.splitContainer1.Panel2.Controls.Add(this.btnSaveModel);
            this.splitContainer1.Panel2.Controls.Add(this.btnAddField);
            this.splitContainer1.Panel2.Controls.Add(this.dgvFields);
            this.splitContainer1.Panel2.Controls.Add(this.chkIsActive);
            this.splitContainer1.Panel2.Controls.Add(this.txtDescription);
            this.splitContainer1.Panel2.Controls.Add(this.txtTableName);
            this.splitContainer1.Panel2.Controls.Add(this.txtModelName);
            this.splitContainer1.Panel2.Controls.Add(this.label3);
            this.splitContainer1.Panel2.Controls.Add(this.label2);
            this.splitContainer1.Panel2.Controls.Add(this.label1);
            this.splitContainer1.Panel2MinSize = 400;
            this.splitContainer1.Size = new System.Drawing.Size(1241, 675);
            this.splitContainer1.SplitterDistance = 271;
            this.splitContainer1.TabIndex = 0;
            // 
            // btnAddModel
            // 
            this.btnAddModel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnAddModel.Location = new System.Drawing.Point(0, 620);
            this.btnAddModel.Name = "btnAddModel";
            this.btnAddModel.Size = new System.Drawing.Size(271, 55);
            this.btnAddModel.TabIndex = 1;
            this.btnAddModel.Text = "+ 新建模型";
            this.btnAddModel.UseVisualStyleBackColor = true;
            // 
            // listBoxModels
            // 
            this.listBoxModels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxModels.FormattingEnabled = true;
            this.listBoxModels.ItemHeight = 22;
            this.listBoxModels.Location = new System.Drawing.Point(0, 0);
            this.listBoxModels.Name = "listBoxModels";
            this.listBoxModels.Size = new System.Drawing.Size(271, 675);
            this.listBoxModels.TabIndex = 0;
            // 
            // cmbParentModel
            // 
            this.cmbParentModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbParentModel.FormattingEnabled = true;
            this.cmbParentModel.Location = new System.Drawing.Point(386, 162);
            this.cmbParentModel.Name = "cmbParentModel";
            this.cmbParentModel.Size = new System.Drawing.Size(313, 30);
            this.cmbParentModel.TabIndex = 12;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(320, 167);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(74, 22);
            this.label4.TabIndex = 11;
            this.label4.Text = "继承来：";
            // 
            // btnSaveModel
            // 
            this.btnSaveModel.Location = new System.Drawing.Point(774, 628);
            this.btnSaveModel.Name = "btnSaveModel";
            this.btnSaveModel.Size = new System.Drawing.Size(120, 38);
            this.btnSaveModel.TabIndex = 10;
            this.btnSaveModel.Text = "保存模型";
            this.btnSaveModel.UseVisualStyleBackColor = true;
            // 
            // btnAddField
            // 
            this.btnAddField.Location = new System.Drawing.Point(91, 620);
            this.btnAddField.Name = "btnAddField";
            this.btnAddField.Size = new System.Drawing.Size(120, 38);
            this.btnAddField.TabIndex = 9;
            this.btnAddField.Text = "+ 添加字段";
            this.btnAddField.UseVisualStyleBackColor = true;
            // 
            // dgvFields
            // 
            this.dgvFields.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvFields.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvFields.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colFieldName,
            this.colFieldType,
            this.colFieldLength,
            this.colIsRequired,
            this.colIsPrimaryKey,
            this.colIsIdentity,
            this.colDescription});
            this.dgvFields.Location = new System.Drawing.Point(44, 215);
            this.dgvFields.Name = "dgvFields";
            this.dgvFields.RowTemplate.Height = 23;
            this.dgvFields.Size = new System.Drawing.Size(876, 395);
            this.dgvFields.TabIndex = 8;
            // 
            // colFieldName
            // 
            this.colFieldName.HeaderText = "字段名";
            this.colFieldName.Name = "colFieldName";
            // 
            // colFieldType
            // 
            this.colFieldType.HeaderText = "类型";
            this.colFieldType.Name = "colFieldType";
            // 
            // colFieldLength
            // 
            this.colFieldLength.HeaderText = "长度";
            this.colFieldLength.Name = "colFieldLength";
            // 
            // colIsRequired
            // 
            this.colIsRequired.HeaderText = "必填";
            this.colIsRequired.Name = "colIsRequired";
            // 
            // colIsPrimaryKey
            // 
            this.colIsPrimaryKey.HeaderText = "主键";
            this.colIsPrimaryKey.Name = "colIsPrimaryKey";
            // 
            // colIsIdentity
            // 
            this.colIsIdentity.HeaderText = "自增";
            this.colIsIdentity.Name = "colIsIdentity";
            // 
            // colDescription
            // 
            this.colDescription.HeaderText = "说明";
            this.colDescription.Name = "colDescription";
            // 
            // chkIsActive
            // 
            this.chkIsActive.AutoSize = true;
            this.chkIsActive.Location = new System.Drawing.Point(157, 166);
            this.chkIsActive.Name = "chkIsActive";
            this.chkIsActive.Size = new System.Drawing.Size(93, 26);
            this.chkIsActive.TabIndex = 7;
            this.chkIsActive.Text = "是否启用";
            this.chkIsActive.UseVisualStyleBackColor = true;
            // 
            // txtDescription
            // 
            this.txtDescription.Location = new System.Drawing.Point(250, 126);
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.Size = new System.Drawing.Size(449, 29);
            this.txtDescription.TabIndex = 6;
            // 
            // txtTableName
            // 
            this.txtTableName.Location = new System.Drawing.Point(250, 81);
            this.txtTableName.Name = "txtTableName";
            this.txtTableName.Size = new System.Drawing.Size(449, 29);
            this.txtTableName.TabIndex = 5;
            // 
            // txtModelName
            // 
            this.txtModelName.Location = new System.Drawing.Point(250, 35);
            this.txtModelName.Name = "txtModelName";
            this.txtModelName.Size = new System.Drawing.Size(449, 29);
            this.txtModelName.TabIndex = 4;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(153, 126);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(58, 22);
            this.label3.TabIndex = 2;
            this.label3.Text = "说明：";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(153, 81);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(58, 22);
            this.label2.TabIndex = 1;
            this.label2.Text = "表名：";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(153, 35);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(90, 22);
            this.label1.TabIndex = 0;
            this.label1.Text = "模型名称：";
            // 
            // tabPageScript
            // 
            this.tabPageScript.Controls.Add(this.splitContainer2);
            this.tabPageScript.Location = new System.Drawing.Point(4, 31);
            this.tabPageScript.Name = "tabPageScript";
            this.tabPageScript.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageScript.Size = new System.Drawing.Size(1247, 681);
            this.tabPageScript.TabIndex = 1;
            this.tabPageScript.Text = "解析脚本";
            this.tabPageScript.UseVisualStyleBackColor = true;
            // 
            // splitContainer2
            // 
            this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer2.Location = new System.Drawing.Point(3, 3);
            this.splitContainer2.Name = "splitContainer2";
            // 
            // splitContainer2.Panel1
            // 
            this.splitContainer2.Panel1.Controls.Add(this.listBoxScripts);
            this.splitContainer2.Panel1.Controls.Add(this.btnAddScript);
            // 
            // splitContainer2.Panel2
            // 
            this.splitContainer2.Panel2.Controls.Add(this.flowLayoutMachines);
            this.splitContainer2.Panel2.Controls.Add(this.btnTxtTemplate);
            this.splitContainer2.Panel2.Controls.Add(this.btnJsonTemplate);
            this.splitContainer2.Panel2.Controls.Add(this.btnCsvTemplate);
            this.splitContainer2.Panel2.Controls.Add(this.panel1);
            this.splitContainer2.Panel2.Controls.Add(this.btnExcelTemplate);
            this.splitContainer2.Panel2.Controls.Add(this.rtxtScriptCode);
            this.splitContainer2.Panel2.Controls.Add(this.chkScriptEnabled);
            this.splitContainer2.Panel2.Controls.Add(this.cmbScriptFileType);
            this.splitContainer2.Panel2.Controls.Add(this.cmbScriptModel);
            this.splitContainer2.Panel2.Controls.Add(this.label8);
            this.splitContainer2.Panel2.Controls.Add(this.label7);
            this.splitContainer2.Panel2.Controls.Add(this.label6);
            this.splitContainer2.Panel2.Controls.Add(this.label5);
            this.splitContainer2.Panel2.Controls.Add(this.txtScriptName);
            this.splitContainer2.Size = new System.Drawing.Size(1241, 675);
            this.splitContainer2.SplitterDistance = 244;
            this.splitContainer2.TabIndex = 0;
            // 
            // listBoxScripts
            // 
            this.listBoxScripts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxScripts.FormattingEnabled = true;
            this.listBoxScripts.ItemHeight = 22;
            this.listBoxScripts.Location = new System.Drawing.Point(0, 0);
            this.listBoxScripts.Name = "listBoxScripts";
            this.listBoxScripts.Size = new System.Drawing.Size(244, 615);
            this.listBoxScripts.TabIndex = 1;
            // 
            // btnAddScript
            // 
            this.btnAddScript.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnAddScript.Location = new System.Drawing.Point(0, 615);
            this.btnAddScript.Name = "btnAddScript";
            this.btnAddScript.Size = new System.Drawing.Size(244, 60);
            this.btnAddScript.TabIndex = 0;
            this.btnAddScript.Text = "+ 新建脚本";
            this.btnAddScript.UseVisualStyleBackColor = true;
            // 
            // flowLayoutMachines
            // 
            this.flowLayoutMachines.AutoScroll = true;
            this.flowLayoutMachines.Location = new System.Drawing.Point(533, 9);
            this.flowLayoutMachines.Name = "flowLayoutMachines";
            this.flowLayoutMachines.Size = new System.Drawing.Size(427, 100);
            this.flowLayoutMachines.TabIndex = 17;
            // 
            // btnTxtTemplate
            // 
            this.btnTxtTemplate.Location = new System.Drawing.Point(688, 542);
            this.btnTxtTemplate.Name = "btnTxtTemplate";
            this.btnTxtTemplate.Size = new System.Drawing.Size(147, 41);
            this.btnTxtTemplate.TabIndex = 16;
            this.btnTxtTemplate.Text = "TXT模板";
            this.btnTxtTemplate.UseVisualStyleBackColor = true;
            this.btnTxtTemplate.Click += new System.EventHandler(this.btnTxtTemplate_Click);
            // 
            // btnJsonTemplate
            // 
            this.btnJsonTemplate.Location = new System.Drawing.Point(462, 542);
            this.btnJsonTemplate.Name = "btnJsonTemplate";
            this.btnJsonTemplate.Size = new System.Drawing.Size(147, 41);
            this.btnJsonTemplate.TabIndex = 15;
            this.btnJsonTemplate.Text = "JSON模板";
            this.btnJsonTemplate.UseVisualStyleBackColor = true;
            this.btnJsonTemplate.Click += new System.EventHandler(this.btnJsonTemplate_Click);
            // 
            // btnCsvTemplate
            // 
            this.btnCsvTemplate.Location = new System.Drawing.Point(231, 542);
            this.btnCsvTemplate.Name = "btnCsvTemplate";
            this.btnCsvTemplate.Size = new System.Drawing.Size(147, 41);
            this.btnCsvTemplate.TabIndex = 14;
            this.btnCsvTemplate.Text = "CSV模板";
            this.btnCsvTemplate.UseVisualStyleBackColor = true;
            this.btnCsvTemplate.Click += new System.EventHandler(this.btnCsvTemplate_Click);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.btnSaveScript);
            this.panel1.Controls.Add(this.btnTestScript);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 606);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(993, 69);
            this.panel1.TabIndex = 13;
            // 
            // btnSaveScript
            // 
            this.btnSaveScript.Location = new System.Drawing.Point(612, 19);
            this.btnSaveScript.Name = "btnSaveScript";
            this.btnSaveScript.Size = new System.Drawing.Size(147, 41);
            this.btnSaveScript.TabIndex = 18;
            this.btnSaveScript.Text = "保存脚本";
            this.btnSaveScript.UseVisualStyleBackColor = true;
            // 
            // btnTestScript
            // 
            this.btnTestScript.Location = new System.Drawing.Point(77, 19);
            this.btnTestScript.Name = "btnTestScript";
            this.btnTestScript.Size = new System.Drawing.Size(147, 41);
            this.btnTestScript.TabIndex = 17;
            this.btnTestScript.Text = "测试运行";
            this.btnTestScript.UseVisualStyleBackColor = true;
            // 
            // btnExcelTemplate
            // 
            this.btnExcelTemplate.Location = new System.Drawing.Point(22, 542);
            this.btnExcelTemplate.Name = "btnExcelTemplate";
            this.btnExcelTemplate.Size = new System.Drawing.Size(147, 41);
            this.btnExcelTemplate.TabIndex = 12;
            this.btnExcelTemplate.Text = "Excel模板";
            this.btnExcelTemplate.UseVisualStyleBackColor = true;
            this.btnExcelTemplate.Click += new System.EventHandler(this.btnExcelTemplate_Click);
            // 
            // rtxtScriptCode
            // 
            this.rtxtScriptCode.Location = new System.Drawing.Point(22, 121);
            this.rtxtScriptCode.Name = "rtxtScriptCode";
            this.rtxtScriptCode.Size = new System.Drawing.Size(938, 397);
            this.rtxtScriptCode.TabIndex = 11;
            this.rtxtScriptCode.Text = "";
            // 
            // chkScriptEnabled
            // 
            this.chkScriptEnabled.AutoSize = true;
            this.chkScriptEnabled.Location = new System.Drawing.Point(441, 79);
            this.chkScriptEnabled.Name = "chkScriptEnabled";
            this.chkScriptEnabled.Size = new System.Drawing.Size(61, 26);
            this.chkScriptEnabled.TabIndex = 8;
            this.chkScriptEnabled.Text = "启用";
            this.chkScriptEnabled.UseVisualStyleBackColor = true;
            // 
            // cmbScriptFileType
            // 
            this.cmbScriptFileType.FormattingEnabled = true;
            this.cmbScriptFileType.Items.AddRange(new object[] {
            ".xlsx",
            ".xls",
            ".csv",
            ".json",
            ".txt"});
            this.cmbScriptFileType.Location = new System.Drawing.Point(114, 83);
            this.cmbScriptFileType.Name = "cmbScriptFileType";
            this.cmbScriptFileType.Size = new System.Drawing.Size(264, 30);
            this.cmbScriptFileType.TabIndex = 7;
            // 
            // cmbScriptModel
            // 
            this.cmbScriptModel.FormattingEnabled = true;
            this.cmbScriptModel.Location = new System.Drawing.Point(114, 41);
            this.cmbScriptModel.Name = "cmbScriptModel";
            this.cmbScriptModel.Size = new System.Drawing.Size(264, 30);
            this.cmbScriptModel.TabIndex = 5;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(17, 83);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(90, 22);
            this.label8.TabIndex = 4;
            this.label8.Text = "文件类型：";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(437, 9);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(90, 22);
            this.label7.TabIndex = 3;
            this.label7.Text = "适用机台：";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(17, 41);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(90, 22);
            this.label6.TabIndex = 2;
            this.label6.Text = "关联模型：";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(18, 9);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(90, 22);
            this.label5.TabIndex = 1;
            this.label5.Text = "脚本名称：";
            // 
            // txtScriptName
            // 
            this.txtScriptName.Location = new System.Drawing.Point(114, 6);
            this.txtScriptName.Name = "txtScriptName";
            this.txtScriptName.Size = new System.Drawing.Size(264, 29);
            this.txtScriptName.TabIndex = 0;
            // 
            // tabPageMapping
            // 
            this.tabPageMapping.Controls.Add(this.splitContainer3);
            this.tabPageMapping.Location = new System.Drawing.Point(4, 31);
            this.tabPageMapping.Name = "tabPageMapping";
            this.tabPageMapping.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageMapping.Size = new System.Drawing.Size(1247, 681);
            this.tabPageMapping.TabIndex = 2;
            this.tabPageMapping.Text = "字段映射";
            this.tabPageMapping.UseVisualStyleBackColor = true;
            // 
            // sqLiteCommandBuilder1
            // 
            this.sqLiteCommandBuilder1.DataAdapter = null;
            this.sqLiteCommandBuilder1.QuoteSuffix = "]";
            // 
            // errorProvider1
            // 
            this.errorProvider1.ContainerControl = this;
            // 
            // splitContainer3
            // 
            this.splitContainer3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer3.Location = new System.Drawing.Point(3, 3);
            this.splitContainer3.Name = "splitContainer3";
            // 
            // splitContainer3.Panel1
            // 
            this.splitContainer3.Panel1.Controls.Add(this.listBoxMappingScripts);
            this.splitContainer3.Panel1.Controls.Add(this.btnNewMappingScript);
            // 
            // splitContainer3.Panel2
            // 
            this.splitContainer3.Panel2.Controls.Add(this.dataGridView1);
            this.splitContainer3.Panel2.Controls.Add(this.panel3);
            this.splitContainer3.Panel2.Controls.Add(this.panel2);
            this.splitContainer3.Size = new System.Drawing.Size(1241, 675);
            this.splitContainer3.SplitterDistance = 253;
            this.splitContainer3.TabIndex = 0;
            // 
            // btnNewMappingScript
            // 
            this.btnNewMappingScript.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnNewMappingScript.Location = new System.Drawing.Point(0, 627);
            this.btnNewMappingScript.Name = "btnNewMappingScript";
            this.btnNewMappingScript.Size = new System.Drawing.Size(253, 48);
            this.btnNewMappingScript.TabIndex = 1;
            this.btnNewMappingScript.Text = "+ 新建脚本";
            this.btnNewMappingScript.UseVisualStyleBackColor = true;
            // 
            // listBoxMappingScripts
            // 
            this.listBoxMappingScripts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxMappingScripts.FormattingEnabled = true;
            this.listBoxMappingScripts.ItemHeight = 22;
            this.listBoxMappingScripts.Location = new System.Drawing.Point(0, 0);
            this.listBoxMappingScripts.Name = "listBoxMappingScripts";
            this.listBoxMappingScripts.Size = new System.Drawing.Size(253, 627);
            this.listBoxMappingScripts.TabIndex = 2;
            // 
            // panel2
            // 
            this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel2.Location = new System.Drawing.Point(0, 0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(984, 88);
            this.panel2.TabIndex = 0;
            // 
            // panel3
            // 
            this.panel3.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel3.Location = new System.Drawing.Point(0, 599);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(984, 76);
            this.panel3.TabIndex = 1;
            // 
            // dataGridView1
            // 
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 88);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowTemplate.Height = 23;
            this.dataGridView1.Size = new System.Drawing.Size(984, 511);
            this.dataGridView1.TabIndex = 2;
            // 
            // ModelConfigForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1255, 716);
            this.Controls.Add(this.tabControl1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "ModelConfigForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "模型设计";
            this.tabControl1.ResumeLayout(false);
            this.tabPageModel.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvFields)).EndInit();
            this.tabPageScript.ResumeLayout(false);
            this.splitContainer2.Panel1.ResumeLayout(false);
            this.splitContainer2.Panel2.ResumeLayout(false);
            this.splitContainer2.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer2)).EndInit();
            this.splitContainer2.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.tabPageMapping.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).EndInit();
            this.splitContainer3.Panel1.ResumeLayout(false);
            this.splitContainer3.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer3)).EndInit();
            this.splitContainer3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPageModel;
        private System.Windows.Forms.TabPage tabPageScript;
        private System.Windows.Forms.TabPage tabPageMapping;
        private System.Data.SQLite.SQLiteCommandBuilder sqLiteCommandBuilder1;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Button btnAddModel;
        private System.Windows.Forms.ListBox listBoxModels;
        private System.Windows.Forms.ErrorProvider errorProvider1;
        private System.Windows.Forms.TextBox txtDescription;
        private System.Windows.Forms.TextBox txtTableName;
        private System.Windows.Forms.TextBox txtModelName;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnSaveModel;
        private System.Windows.Forms.Button btnAddField;
        private System.Windows.Forms.DataGridView dgvFields;
        private System.Windows.Forms.CheckBox chkIsActive;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFieldName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFieldType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFieldLength;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIsRequired;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIsPrimaryKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIsIdentity;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDescription;
        private System.Windows.Forms.ComboBox cmbParentModel;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private System.Windows.Forms.ListBox listBoxScripts;
        private System.Windows.Forms.Button btnAddScript;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtScriptName;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button btnExcelTemplate;
        private System.Windows.Forms.RichTextBox rtxtScriptCode;
        private System.Windows.Forms.CheckBox chkScriptEnabled;
        private System.Windows.Forms.ComboBox cmbScriptFileType;
        private System.Windows.Forms.ComboBox cmbScriptModel;
        private System.Windows.Forms.Button btnTxtTemplate;
        private System.Windows.Forms.Button btnJsonTemplate;
        private System.Windows.Forms.Button btnCsvTemplate;
        private System.Windows.Forms.Button btnSaveScript;
        private System.Windows.Forms.Button btnTestScript;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutMachines;
        private System.Windows.Forms.SplitContainer splitContainer3;
        private System.Windows.Forms.ListBox listBoxMappingScripts;
        private System.Windows.Forms.Button btnNewMappingScript;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Panel panel2;
    }
}