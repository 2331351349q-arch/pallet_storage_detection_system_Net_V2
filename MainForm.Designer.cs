namespace material_box_storage_detection_system_Net
{
    partial class MainForm
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
            panel_Top = new Panel();
            btn_Config = new Button();
            btn_LiveLeft = new Button();
            btn_LiveRight = new Button();
            btn_LiveCodeReader = new Button();
            btn_ToolHub = new Button();
            tableLayoutPanel_Main = new TableLayoutPanel();
            groupBox_Images = new GroupBox();
            tableLayoutPanel_Images = new TableLayoutPanel();
            pictureBox_Camera1 = new PictureBox();
            pictureBox_Camera2 = new PictureBox();
            pictureBox_Camera3 = new PictureBox();
            pictureBox_CodeReader = new PictureBox();
            groupBox_Logs = new GroupBox();
            listBox_Log = new ListBox();
            panel_Top.SuspendLayout();
            tableLayoutPanel_Main.SuspendLayout();
            groupBox_Images.SuspendLayout();
            tableLayoutPanel_Images.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera3).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_CodeReader).BeginInit();
            groupBox_Logs.SuspendLayout();
            SuspendLayout();
            // 
            // panel_Top
            // 
            panel_Top.BackColor = Color.FromArgb(235, 240, 245);
            panel_Top.Controls.Add(btn_Config);
            panel_Top.Controls.Add(btn_LiveLeft);
            panel_Top.Controls.Add(btn_LiveRight);
            panel_Top.Controls.Add(btn_LiveCodeReader);
            panel_Top.Controls.Add(btn_ToolHub);
            panel_Top.Dock = DockStyle.Top;
            panel_Top.Location = new Point(0, 0);
            panel_Top.Name = "panel_Top";
            panel_Top.Size = new Size(1017, 40);
            panel_Top.TabIndex = 2;
            // 
            // btn_Config
            // 
            btn_Config.BackColor = Color.White;
            btn_Config.FlatStyle = FlatStyle.Flat;
            btn_Config.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_Config.ForeColor = Color.FromArgb(30, 50, 80);
            btn_Config.Location = new Point(12, 5);
            btn_Config.Name = "btn_Config";
            btn_Config.Size = new Size(120, 30);
            btn_Config.TabIndex = 0;
            btn_Config.Text = "⚙ 阈值设置";
            btn_Config.UseVisualStyleBackColor = false;
            btn_Config.Click += btn_Config_Click;
            // 
            // btn_LiveLeft
            // 
            btn_LiveLeft.BackColor = Color.White;
            btn_LiveLeft.FlatStyle = FlatStyle.Flat;
            btn_LiveLeft.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_LiveLeft.ForeColor = Color.FromArgb(30, 50, 80);
            btn_LiveLeft.Location = new Point(150, 5);
            btn_LiveLeft.Name = "btn_LiveLeft";
            btn_LiveLeft.Size = new Size(180, 30);
            btn_LiveLeft.TabIndex = 1;
            btn_LiveLeft.Text = "▶ 左侧实时预览 (2台)";
            btn_LiveLeft.UseVisualStyleBackColor = false;
            btn_LiveLeft.Click += btn_LiveLeft_Click;
            // 
            // btn_LiveRight
            // 
            btn_LiveRight.BackColor = Color.White;
            btn_LiveRight.FlatStyle = FlatStyle.Flat;
            btn_LiveRight.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_LiveRight.ForeColor = Color.FromArgb(30, 50, 80);
            btn_LiveRight.Location = new Point(350, 5);
            btn_LiveRight.Name = "btn_LiveRight";
            btn_LiveRight.Size = new Size(180, 30);
            btn_LiveRight.TabIndex = 2;
            btn_LiveRight.Text = "▶ 右侧实时预览 (2台)";
            btn_LiveRight.UseVisualStyleBackColor = false;
            btn_LiveRight.Click += btn_LiveRight_Click;
            // 
            // btn_LiveCodeReader
            // 
            btn_LiveCodeReader.BackColor = Color.White;
            btn_LiveCodeReader.FlatStyle = FlatStyle.Flat;
            btn_LiveCodeReader.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_LiveCodeReader.ForeColor = Color.FromArgb(30, 50, 80);
            btn_LiveCodeReader.Location = new Point(550, 5);
            btn_LiveCodeReader.Name = "btn_LiveCodeReader";
            btn_LiveCodeReader.Size = new Size(180, 30);
            btn_LiveCodeReader.TabIndex = 3;
            btn_LiveCodeReader.Text = "▶ 读码器实时预览";
            btn_LiveCodeReader.UseVisualStyleBackColor = false;
            btn_LiveCodeReader.Click += btn_LiveCodeReader_Click;
            // 
            // btn_ToolHub
            // 
            btn_ToolHub.BackColor = Color.White;
            btn_ToolHub.FlatStyle = FlatStyle.Flat;
            btn_ToolHub.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            btn_ToolHub.ForeColor = Color.FromArgb(30, 50, 80);
            btn_ToolHub.Location = new Point(750, 5);
            btn_ToolHub.Name = "btn_ToolHub";
            btn_ToolHub.Size = new Size(255, 30);
            btn_ToolHub.TabIndex = 4;
            btn_ToolHub.Text = "🧰 工具管理";
            btn_ToolHub.UseVisualStyleBackColor = false;
            btn_ToolHub.Click += btn_ToolHub_Click;
            // 
            // tableLayoutPanel_Main
            // 
            tableLayoutPanel_Main.ColumnCount = 1;
            tableLayoutPanel_Main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel_Main.Controls.Add(groupBox_Images, 0, 0);
            tableLayoutPanel_Main.Controls.Add(groupBox_Logs, 0, 1);
            tableLayoutPanel_Main.Dock = DockStyle.Fill;
            tableLayoutPanel_Main.Location = new Point(0, 40);
            tableLayoutPanel_Main.Name = "tableLayoutPanel_Main";
            tableLayoutPanel_Main.RowCount = 2;
            tableLayoutPanel_Main.RowStyles.Add(new RowStyle(SizeType.Percent, 71.10157F));
            tableLayoutPanel_Main.RowStyles.Add(new RowStyle(SizeType.Percent, 28.8984261F));
            tableLayoutPanel_Main.Size = new Size(1017, 689);
            tableLayoutPanel_Main.TabIndex = 0;
            // 
            // groupBox_Images
            // 
            groupBox_Images.BackColor = Color.Transparent;
            groupBox_Images.Controls.Add(tableLayoutPanel_Images);
            groupBox_Images.Dock = DockStyle.Fill;
            groupBox_Images.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            groupBox_Images.ForeColor = Color.FromArgb(30, 50, 80);
            groupBox_Images.Location = new Point(3, 3);
            groupBox_Images.Name = "groupBox_Images";
            groupBox_Images.Size = new Size(1011, 483);
            groupBox_Images.TabIndex = 3;
            groupBox_Images.TabStop = false;
            groupBox_Images.Text = "视觉监控中心 (Visual Monitoring Center)";
            // 
            // tableLayoutPanel_Images
            // 
            tableLayoutPanel_Images.ColumnCount = 2;
            tableLayoutPanel_Images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Images.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Images.Controls.Add(pictureBox_Camera1, 0, 0);
            tableLayoutPanel_Images.Controls.Add(pictureBox_Camera2, 1, 0);
            tableLayoutPanel_Images.Controls.Add(pictureBox_Camera3, 0, 1);
            tableLayoutPanel_Images.Controls.Add(pictureBox_CodeReader, 1, 1);
            tableLayoutPanel_Images.Dock = DockStyle.Fill;
            tableLayoutPanel_Images.Location = new Point(3, 21);
            tableLayoutPanel_Images.Name = "tableLayoutPanel_Images";
            tableLayoutPanel_Images.RowCount = 2;
            tableLayoutPanel_Images.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Images.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Images.Size = new Size(1005, 459);
            tableLayoutPanel_Images.TabIndex = 0;
            // 
            // pictureBox_Camera1
            // 
            pictureBox_Camera1.BackColor = Color.Black;
            pictureBox_Camera1.Dock = DockStyle.Fill;
            pictureBox_Camera1.Location = new Point(3, 3);
            pictureBox_Camera1.Name = "pictureBox_Camera1";
            pictureBox_Camera1.Size = new Size(496, 223);
            pictureBox_Camera1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox_Camera1.TabIndex = 0;
            pictureBox_Camera1.TabStop = false;
            // 
            // pictureBox_Camera2
            // 
            pictureBox_Camera2.BackColor = Color.Black;
            pictureBox_Camera2.Dock = DockStyle.Fill;
            pictureBox_Camera2.Location = new Point(505, 3);
            pictureBox_Camera2.Name = "pictureBox_Camera2";
            pictureBox_Camera2.Size = new Size(497, 223);
            pictureBox_Camera2.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox_Camera2.TabIndex = 1;
            pictureBox_Camera2.TabStop = false;
            // 
            // pictureBox_Camera3
            // 
            pictureBox_Camera3.BackColor = Color.Black;
            pictureBox_Camera3.Dock = DockStyle.Fill;
            pictureBox_Camera3.Location = new Point(3, 232);
            pictureBox_Camera3.Name = "pictureBox_Camera3";
            pictureBox_Camera3.Size = new Size(496, 224);
            pictureBox_Camera3.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox_Camera3.TabIndex = 2;
            pictureBox_Camera3.TabStop = false;
            // 
            // pictureBox_CodeReader
            // 
            pictureBox_CodeReader.BackColor = Color.Black;
            pictureBox_CodeReader.Dock = DockStyle.Fill;
            pictureBox_CodeReader.Location = new Point(505, 232);
            pictureBox_CodeReader.Name = "pictureBox_CodeReader";
            pictureBox_CodeReader.Size = new Size(497, 224);
            pictureBox_CodeReader.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox_CodeReader.TabIndex = 3;
            pictureBox_CodeReader.TabStop = false;
            // 
            // groupBox_Logs
            // 
            groupBox_Logs.BackColor = Color.Transparent;
            groupBox_Logs.Controls.Add(listBox_Log);
            groupBox_Logs.Dock = DockStyle.Fill;
            groupBox_Logs.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            groupBox_Logs.ForeColor = Color.FromArgb(30, 50, 80);
            groupBox_Logs.Location = new Point(3, 492);
            groupBox_Logs.Name = "groupBox_Logs";
            groupBox_Logs.Size = new Size(1011, 194);
            groupBox_Logs.TabIndex = 4;
            groupBox_Logs.TabStop = false;
            groupBox_Logs.Text = "系统实时日志 (System Event Logs)";
            // 
            // listBox_Log
            // 
            listBox_Log.BackColor = Color.White;
            listBox_Log.BorderStyle = BorderStyle.None;
            listBox_Log.Dock = DockStyle.Fill;
            listBox_Log.ForeColor = Color.FromArgb(30, 30, 35);
            listBox_Log.FormattingEnabled = true;
            listBox_Log.ItemHeight = 19;
            listBox_Log.Location = new Point(3, 21);
            listBox_Log.Name = "listBox_Log";
            listBox_Log.Size = new Size(1005, 170);
            listBox_Log.TabIndex = 1;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(1017, 729);
            Controls.Add(tableLayoutPanel_Main);
            Controls.Add(panel_Top);
            Name = "MainForm";
            Text = "料箱库视觉识别系统";
            Load += MainForm_Load;
            panel_Top.ResumeLayout(false);
            tableLayoutPanel_Main.ResumeLayout(false);
            groupBox_Images.ResumeLayout(false);
            tableLayoutPanel_Images.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera2).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_Camera3).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox_CodeReader).EndInit();
            groupBox_Logs.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.Panel panel_Top;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel_Main;
        private System.Windows.Forms.GroupBox groupBox_Images;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel_Images;
        private System.Windows.Forms.PictureBox pictureBox_Camera1;
        private System.Windows.Forms.PictureBox pictureBox_Camera2;
        private System.Windows.Forms.PictureBox pictureBox_Camera3;
        private System.Windows.Forms.PictureBox pictureBox_CodeReader;
        private System.Windows.Forms.GroupBox groupBox_Logs;
        private System.Windows.Forms.ListBox listBox_Log;
        private System.Windows.Forms.Button btn_Config;
        private System.Windows.Forms.Button btn_LiveLeft;
        private System.Windows.Forms.Button btn_LiveRight;
        private System.Windows.Forms.Button btn_LiveCodeReader;
        private System.Windows.Forms.Button btn_ToolHub;
    }
}

