using System.Drawing;
using System.Windows.Forms;

namespace pallet_storage_detection_system_Net_V2
{
    /// <summary>
    /// 工具中心 — 统一管理所有调试/标定工具的入口窗体。
    /// 外层 TableLayoutPanel 分列，卡片内使用 Dock+手动布局确保按钮始终可见。
    /// </summary>
    public partial class ToolHubForm : Form
    {
        private RoiTunerForm? _roiTunerForm;
        private CalibrationForm? _calibrationForm;
        private StackerOffsetTunerForm? _stackerOffsetTunerForm;
        private RackDeformationTunerForm? _rackDeformationTunerForm;
        private CameraControlForm? _cameraControlForm;
        private Camera2DCalibrationForm? _camera2DCalibrationForm;

        public ToolHubForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                ColumnCount = 6,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(36, 32, 36, 32),
                BackColor = Color.FromArgb(240, 242, 248),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // ── 标题（跨列）──
            var titlePanel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

            titlePanel.Controls.Add(new Label
            {
                Text = "🧰  工具管理",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 45, 72),
                AutoSize = true,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.BottomLeft,
            }, 0, 0);

            titlePanel.Controls.Add(new Label
            {
                Text = "选择需要使用的调试或标定工具",
                Font = new Font("Microsoft YaHei UI", 11F),
                ForeColor = Color.FromArgb(130, 140, 160),
                AutoSize = true,
                Dock = DockStyle.Left,
                Padding = new Padding(2, 4, 0, 0),
            }, 0, 1);

            root.Controls.Add(titlePanel, 0, 0);
            root.SetColumnSpan(titlePanel, 6);

            // ── 三张卡片 ──
            root.Controls.Add(BuildCard("📐", "ROI 区域调窗",
                "实时预览 3D 点云，调整槽位检测区域，\n验证有货/无货效果",
                Color.FromArgb(0, 122, 204), OpenRoiTuner), 0, 1);

            root.Controls.Add(BuildCard("🎯", "3D 相机外参标定",
                "对 3D 相机进行墙面或地面对齐标定，\n输出旋转矩阵 R 与平移向量 T",
                Color.FromArgb(0, 153, 136), OpenCalibration), 1, 1);

            root.Controls.Add(BuildCard("📏", "堆垛机偏移调参",
                "调整 DepthMin/Max 深度区间，\n查看梁检测效果与偏移计算",
                Color.FromArgb(156, 39, 176), OpenStackerOffsetTuner), 2, 1);

            root.Controls.Add(BuildCard("🏢", "变形检测可视化",
                "调整 ROI 并查看 3D 点云彩色分割，\n实时测试货架变形与托臂下垂指标",
                Color.FromArgb(255, 120, 0), OpenRackDeformationTuner), 3, 1);

            root.Controls.Add(BuildCard("📷", "相机批量控制",
                "一键开启/关闭所有 2D/3D 相机，\n实时查看各设备运行状态",
                Color.FromArgb(40, 167, 69), OpenCameraControl), 4, 1);

            root.Controls.Add(BuildCard("🔬", "2D 相机内参标定",
                "加载棋盘格图片，输入标定板参数，\n计算海康相机内参矩阵与畸变系数",
                Color.FromArgb(200, 80, 0), OpenCamera2DCalibration), 5, 1);

            Controls.Add(root);
            ClientSize = new Size(2040, 650);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "工具管理";
            BackColor = Color.FromArgb(240, 242, 248);
            ResumeLayout(false);
        }

        /// <summary>
        /// 卡片使用简单的纵向 Dock 排列：图标→标题→描述→弹簧→按钮。
        /// 弹簧（空白 Panel）自动撑开按钮和描述之间的空间，确保按钮始终在底部可见。
        /// </summary>
        private Panel BuildCard(string icon, string title, string desc, Color accent, Action onClick)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(15, 15, 15, 15),
                BackColor = Color.White,
            };

            /* Dock 规则：同方向 Dock 时，后添加的控件占据更外侧位置。
               Dock.Top 需要倒序添加（先加底部控件，后加顶部控件）。
               Dock.Bottom 也需要倒序（先加靠上控件，后加靠下控件）。 */

            // 描述 — Dock.Top，实际在图标/标题/色条下方
            var lblDesc = new Label
            {
                Text = desc,
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(110, 120, 140),
                AutoSize = false,
                Height = 80,
                TextAlign = ContentAlignment.TopLeft,
                Dock = DockStyle.Top,
                Padding = new Padding(24, 0, 24, 0),
            };
            card.Controls.Add(lblDesc);

            // 标题 — 在描述上方
            card.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 45, 72),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(24, 0, 0, 6),
            });

            // 图标 — 在标题上方
            card.Controls.Add(new Label
            {
                Text = icon,
                Font = new Font("Segoe UI", 32F),
                ForeColor = Color.FromArgb(50, 50, 60),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(24, 14, 0, 4),
            });

            // 顶部色条 — 在图标上方（最后添加的 Dock.Top = 最顶部）
            card.Controls.Add(new Panel { Height = 5, Dock = DockStyle.Top, BackColor = accent });

            // ★ 弹簧：Dock.Fill，占据 Dock.Top 和 Dock.Bottom 之间的剩余空间
            card.Controls.Add(new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            });

            // 按钮下方留白 — Dock.Bottom，最底部
            card.Controls.Add(new Panel { Height = 18, Dock = DockStyle.Bottom, BackColor = Color.White });

            // 按钮 — Dock.Bottom，在留白上方
            var btn = new Button
            {
                Text = "🚀  打开",
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = accent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 40),
                Dock = DockStyle.Bottom,
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, __) => onClick();
            card.Controls.Add(btn);

            // 边框
            card.Paint += (_, e) =>
            {
                var r = card.ClientRectangle;
                r.Width--; r.Height--;
                using var p = new Pen(Color.FromArgb(200, 205, 215));
                e.Graphics.DrawRectangle(p, r);
            };

            return card;
        }

        private void OpenRoiTuner()
        {
            if (_roiTunerForm == null || _roiTunerForm.IsDisposed)
            {
                _roiTunerForm = new RoiTunerForm();
                _roiTunerForm.FormClosed += (_, __) => _roiTunerForm = null;
                _roiTunerForm.Show(Owner ?? this);
                return;
            }
            if (!_roiTunerForm.Visible) _roiTunerForm.Show(Owner ?? this);
            _roiTunerForm.BringToFront();
            _roiTunerForm.Focus();
        }

        private void OpenCalibration()
        {
            if (_calibrationForm == null || _calibrationForm.IsDisposed)
            {
                _calibrationForm = new CalibrationForm();
                _calibrationForm.FormClosed += (_, __) => _calibrationForm = null;
                _calibrationForm.Show(Owner ?? this);
                return;
            }
            if (!_calibrationForm.Visible) _calibrationForm.Show(Owner ?? this);
            _calibrationForm.BringToFront();
            _calibrationForm.Focus();
        }

        private void OpenStackerOffsetTuner()
        {
            if (_stackerOffsetTunerForm == null || _stackerOffsetTunerForm.IsDisposed)
            {
                _stackerOffsetTunerForm = new StackerOffsetTunerForm();
                _stackerOffsetTunerForm.FormClosed += (_, __) => _stackerOffsetTunerForm = null;
                _stackerOffsetTunerForm.Show(Owner ?? this);
                return;
            }
            if (!_stackerOffsetTunerForm.Visible) _stackerOffsetTunerForm.Show(Owner ?? this);
            _stackerOffsetTunerForm.BringToFront();
            _stackerOffsetTunerForm.Focus();
        }

        private void OpenRackDeformationTuner()
        {
            if (_rackDeformationTunerForm == null || _rackDeformationTunerForm.IsDisposed)
            {
                _rackDeformationTunerForm = new RackDeformationTunerForm();
                _rackDeformationTunerForm.FormClosed += (_, __) => _rackDeformationTunerForm = null;
                _rackDeformationTunerForm.Show(Owner ?? this);
                return;
            }
            if (!_rackDeformationTunerForm.Visible) _rackDeformationTunerForm.Show(Owner ?? this);
            _rackDeformationTunerForm.BringToFront();
            _rackDeformationTunerForm.Focus();
        }

        private void OpenCameraControl()
        {
            if (_cameraControlForm == null || _cameraControlForm.IsDisposed)
            {
                _cameraControlForm = new CameraControlForm();
                _cameraControlForm.FormClosed += (_, __) => _cameraControlForm = null;
                _cameraControlForm.Show(Owner ?? this);
                return;
            }
            if (!_cameraControlForm.Visible) _cameraControlForm.Show(Owner ?? this);
            _cameraControlForm.BringToFront();
            _cameraControlForm.Focus();
        }

        private void OpenCamera2DCalibration()
        {
            if (_camera2DCalibrationForm == null || _camera2DCalibrationForm.IsDisposed)
            {
                _camera2DCalibrationForm = new Camera2DCalibrationForm();
                _camera2DCalibrationForm.FormClosed += (_, __) => _camera2DCalibrationForm = null;
                _camera2DCalibrationForm.Show(Owner ?? this);
                return;
            }
            if (!_camera2DCalibrationForm.Visible) _camera2DCalibrationForm.Show(Owner ?? this);
            _camera2DCalibrationForm.BringToFront();
            _camera2DCalibrationForm.Focus();
        }
    }
}
