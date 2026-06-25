namespace material_box_storage_detection_system_Net
{
    partial class ConfigForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            propertyGrid_Config = new PropertyGrid();
            panel_Buttons = new Panel();
            btn_Cancel = new Button();
            btn_Save = new Button();
            panel_Buttons.SuspendLayout();
            SuspendLayout();
            // 
            // propertyGrid_Config
            // 
            propertyGrid_Config.Dock = DockStyle.Fill;
            propertyGrid_Config.HelpVisible = true;
            propertyGrid_Config.Location = new Point(0, 0);
            propertyGrid_Config.Name = "propertyGrid_Config";
            propertyGrid_Config.Size = new Size(584, 511);
            propertyGrid_Config.TabIndex = 0;
            propertyGrid_Config.ViewBackColor = Color.White;
            propertyGrid_Config.ViewForeColor = Color.FromArgb(30, 50, 80);
            // 
            // panel_Buttons
            // 
            panel_Buttons.BackColor = Color.FromArgb(235, 240, 245);
            panel_Buttons.Controls.Add(btn_Cancel);
            panel_Buttons.Controls.Add(btn_Save);
            panel_Buttons.Dock = DockStyle.Bottom;
            panel_Buttons.Location = new Point(0, 511);
            panel_Buttons.Name = "panel_Buttons";
            panel_Buttons.Size = new Size(584, 50);
            panel_Buttons.TabIndex = 1;
            // 
            // btn_Cancel
            // 
            btn_Cancel.FlatStyle = FlatStyle.Flat;
            btn_Cancel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_Cancel.ForeColor = Color.FromArgb(120, 130, 140);
            btn_Cancel.Location = new Point(466, 10);
            btn_Cancel.Name = "btn_Cancel";
            btn_Cancel.Size = new Size(100, 30);
            btn_Cancel.TabIndex = 1;
            btn_Cancel.Text = "取消";
            btn_Cancel.UseVisualStyleBackColor = true;
            btn_Cancel.Click += btn_Cancel_Click;
            // 
            // btn_Save
            // 
            btn_Save.BackColor = Color.FromArgb(0, 120, 215);
            btn_Save.FlatStyle = FlatStyle.Flat;
            btn_Save.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_Save.ForeColor = Color.White;
            btn_Save.Location = new Point(350, 10);
            btn_Save.Name = "btn_Save";
            btn_Save.Size = new Size(100, 30);
            btn_Save.TabIndex = 0;
            btn_Save.Text = "保存配置";
            btn_Save.UseVisualStyleBackColor = false;
            btn_Save.Click += btn_Save_Click;
            // 
            // ConfigForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(584, 561);
            Controls.Add(propertyGrid_Config);
            Controls.Add(panel_Buttons);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ConfigForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "系统算法参数配置 (Parameter Settings)";
            Load += ConfigForm_Load;
            panel_Buttons.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.PropertyGrid propertyGrid_Config;
        private System.Windows.Forms.Panel panel_Buttons;
        private System.Windows.Forms.Button btn_Cancel;
        private System.Windows.Forms.Button btn_Save;
    }
}

