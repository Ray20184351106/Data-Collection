namespace MachineDataAcquisitionSystem.Forms
{
    partial class BaseFieldConfigForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BaseFieldConfigForm));
            this.dgvBaseFields = new System.Windows.Forms.DataGridView();
            this.colFieldName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFieldType = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.colDefaultValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIsRequired = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colIsReadOnly = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSortOrder = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDescription = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.panel1 = new System.Windows.Forms.Panel();
            this.btnAddField = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgvBaseFields)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // dgvBaseFields
            // 
            this.dgvBaseFields.AllowUserToAddRows = false;
            this.dgvBaseFields.AllowUserToDeleteRows = false;
            this.dgvBaseFields.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvBaseFields.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvBaseFields.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colFieldName,
            this.colFieldType,
            this.colDefaultValue,
            this.colIsRequired,
            this.colIsReadOnly,
            this.colSortOrder,
            this.colDescription});
            this.dgvBaseFields.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvBaseFields.Location = new System.Drawing.Point(0, 0);
            this.dgvBaseFields.Name = "dgvBaseFields";
            this.dgvBaseFields.RowHeadersVisible = false;
            this.dgvBaseFields.RowTemplate.Height = 23;
            this.dgvBaseFields.Size = new System.Drawing.Size(1100, 603);
            this.dgvBaseFields.TabIndex = 0;
            // 
            // colFieldName
            // 
            this.colFieldName.HeaderText = "字段名";
            this.colFieldName.Name = "colFieldName";
            // 
            // colFieldType
            // 
            this.colFieldType.HeaderText = "类型";
            this.colFieldType.Items.AddRange(new object[] {
            "string",
            "int",
            "long",
            "decimal",
            "float",
            "double",
            "datetime",
            "bool"});
            this.colFieldType.Name = "colFieldType";
            this.colFieldType.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.colFieldType.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // colDefaultValue
            // 
            this.colDefaultValue.HeaderText = "默认值";
            this.colDefaultValue.Name = "colDefaultValue";
            // 
            // colIsRequired
            // 
            this.colIsRequired.HeaderText = "必填";
            this.colIsRequired.Name = "colIsRequired";
            // 
            // colIsReadOnly
            // 
            this.colIsReadOnly.HeaderText = "只读";
            this.colIsReadOnly.Name = "colIsReadOnly";
            // 
            // colSortOrder
            // 
            this.colSortOrder.HeaderText = "排序";
            this.colSortOrder.Name = "colSortOrder";
            // 
            // colDescription
            // 
            this.colDescription.HeaderText = "说明";
            this.colDescription.Name = "colDescription";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.btnSave);
            this.panel1.Controls.Add(this.btnAddField);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 509);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1100, 94);
            this.panel1.TabIndex = 1;
            // 
            // btnAddField
            // 
            this.btnAddField.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddField.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAddField.Location = new System.Drawing.Point(223, 28);
            this.btnAddField.Name = "btnAddField";
            this.btnAddField.Size = new System.Drawing.Size(162, 54);
            this.btnAddField.TabIndex = 0;
            this.btnAddField.Text = "+ 添加字段";
            this.btnAddField.UseVisualStyleBackColor = true;
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSave.Location = new System.Drawing.Point(787, 28);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(162, 54);
            this.btnSave.TabIndex = 1;
            this.btnSave.Text = "保存配置";
            this.btnSave.UseVisualStyleBackColor = true;
            // 
            // BaseFieldConfigForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 603);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.dgvBaseFields);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "BaseFieldConfigForm";
            this.Text = "基类字段配置";
            ((System.ComponentModel.ISupportInitialize)(this.dgvBaseFields)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dgvBaseFields;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFieldName;
        private System.Windows.Forms.DataGridViewComboBoxColumn colFieldType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDefaultValue;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIsRequired;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIsReadOnly;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSortOrder;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDescription;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button btnAddField;
        private System.Windows.Forms.Button btnSave;
    }
}