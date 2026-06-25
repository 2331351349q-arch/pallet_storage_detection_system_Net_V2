using System;
using System.Drawing;
using System.Windows.Forms;
using material_box_storage_detection_system_Net.Config;

namespace material_box_storage_detection_system_Net
{
    public partial class ConfigForm : Form
    {
        public ConfigForm()
        {
            InitializeComponent();
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            this.Text = "核心算法阈值设置 (Threshold Settings)";

            // 优化：直接绑定到算法阈值层级，减少用户点击次数
            if (ConfigManager.Instance != null)
            {
                propertyGrid_Config.SelectedObject = ConfigManager.Instance.Algorithms;
            }
            
            // 优化：全量展开所有 A/B/C/D 门限，确保一眼看全
            propertyGrid_Config.ExpandAllGridItems();
            
            propertyGrid_Config.PropertySort = PropertySort.Categorized;
        }

        private void btn_Save_Click(object sender, EventArgs e)
        {
            // 持久化到本地 config.yaml
            ConfigManager.SaveConfig();
            
            MessageBox.Show("参数配置已成功保存！\n部分核心参数（如Redis连接）可能需要重启程序以生效。", 
                "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btn_Cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}

