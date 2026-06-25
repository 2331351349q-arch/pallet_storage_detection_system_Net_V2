using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using material_box_storage_detection_system_Net.Algorithms;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;

namespace material_box_storage_detection_system_Net
{
    /// <summary>
    /// 堆垛机偏移检测调参工具。
    /// 显示 X 轴深度剖面图、梁检测效果、偏移计算结果，
    /// 支持实时调整 DepthMin/DepthMax 深度区间。
    /// </summary>
    public class StackerOffsetTunerForm : Form
    {
        // ---- 控件 ----
        private ComboBox _cmbSide = null!;
        private TextBox _txtCameraSn = null!;
        private NumericUpDown _numYaw = null!;
        private NumericUpDown _numPitch = null!;
        private NumericUpDown _numDepthMin = null!;
        private NumericUpDown _numDepthMax = null!;
        private NumericUpDown _numYMinRoi = null!;
        private NumericUpDown _numYMaxRoi = null!;
        private TrackBar _tbYMinRoi = null!;
        private TrackBar _tbYMaxRoi = null!;
        private Label _lblYMinRoiVal = null!;
        private Label _lblYMaxRoiVal = null!;
        private Label _lblDetectedYRange = null!;    // 显示检测到的 Y 范围
        private NumericUpDown _numXMinRoi = null!;
        private NumericUpDown _numXMaxRoi = null!;
        private TrackBar _tbXMinRoi = null!;
        private TrackBar _tbXMaxRoi = null!;
        private Label _lblXMinRoiVal = null!;
        private Label _lblXMaxRoiVal = null!;
        private Label _lblDetectedXRange = null!;   // 显示检测到的 X 范围
        private TrackBar _tbDepthMin = null!;
        private TrackBar _tbDepthMax = null!;
        private Label _lblDepthMinVal = null!;
        private Label _lblDepthMaxVal = null!;
        private Label _lblResult = null!;
        private PictureBox _pbCloud = null!;
        private PictureBox _pbProfile = null!;
        private PictureBox _pbPreview = null!;
        private TextBox _txtLog = null!;

        // ---- 状态 ----
        private DepthFrameData? _currentFrame;
        private StackerOffsetAlgo.DebugData? _currentDebug;
        private bool _isDragging;
        private Point _lastMouse;
        private double _zoomLevel = 1.0;
        private double _panX, _panY;               // 3D 视图平移偏移 (pixels)
        private Label _lblCalibStatus = null!;      // 标定状态指示

        public StackerOffsetTunerForm()
        {
            InitializeUi();
            LoadConfigValues();
            FormClosed += OnFormClosed;
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            // 停止并断开通过本窗口初始化的相机
            string sn = _txtCameraSn?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sn))
            {
                var cam = DeviceManager.GetCamera(sn);
                if (cam != null)
                {
                    try
                    {
                        if (cam.IsCapturing)
                            cam.StopGrabbing();
                    }
                    catch { /* 忽略关闭时的异常 */ }
                }
            }

            // 释放图像资源
            _pbCloud?.Image?.Dispose();
            _pbProfile?.Image?.Dispose();
            _pbPreview?.Image?.Dispose();
            _currentFrame?.PreviewImage?.Dispose();
        }

        // ============ UI 构建 ============

        private void InitializeUi()
        {
            Text = "堆垛机偏移检测 — 深度区间调参工具";
            Width = 1680;
            Height = 980;
            MinimumSize = new Size(1300, 780);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            BackColor = Color.FromArgb(22, 22, 26);

            // 主分割：左侧控制面板 | 右侧可视化
            var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, BackColor = Color.FromArgb(40, 42, 54) };
            Controls.Add(mainSplit);

            // ====== 左侧：控制面板 ======
            var leftPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true, BackColor = Color.FromArgb(28, 28, 35) };
            mainSplit.Panel1.Controls.Add(leftPnl);

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.FromArgb(28, 28, 35) };
            leftPnl.Controls.Add(flow);

            // 侧位选择
            flow.Controls.Add(SectionLabel("📷 相机配置"));
            _cmbSide = new ComboBox { Width = FlowW, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSide.Items.AddRange(new object[] { "left", "right" });
            _cmbSide.SelectedIndex = 0;
            _cmbSide.SelectedIndexChanged += (_, __) => { UpdateCameraSnFromConfig(); LoadCameraParams(); };
            flow.Controls.Add(_cmbSide);

            // 相机 SN
            flow.Controls.Add(BoldLabel("目标相机 SN"));
            _txtCameraSn = new TextBox { Width = FlowW };
            flow.Controls.Add(_txtCameraSn);

            // 按鈕行
            var btnLine = new FlowLayoutPanel { Width = FlowW, Height = 36 };
            var btnInit = new Button { Text = "初始化相机", Width = (FlowW - 6) / 3, Height = 30, BackColor = Color.FromArgb(55, 60, 75), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnInit.Click += BtnInit_Click;
            var btnGrab = new Button { Text = "抓取一帧", Width = (FlowW - 6) / 3, Height = 30, BackColor = Color.FromArgb(60, 100, 55), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnGrab.Click += BtnGrab_Click;
            var btnSave = new Button { Text = "保存到配置", Width = (FlowW - 6) / 3, Height = 30, BackColor = Color.FromArgb(60, 80, 130), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnSave.Click += BtnSave_Click;
            btnLine.Controls.Add(btnInit);
            btnLine.Controls.Add(btnGrab);
            btnLine.Controls.Add(btnSave);
            flow.Controls.Add(btnLine);

            // 标准位置采集
            var refLine = new FlowLayoutPanel { Width = FlowW, Height = 36 };
            var btnCaptureRef = new Button { Text = "📌 采集标准位置", Width = (FlowW - 6) / 2, Height = 30, BackColor = Color.FromArgb(180, 160, 40), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            btnCaptureRef.Click += BtnCaptureRef_Click;
            var _lblRef = new Label { Text = "标准位置: 未设定", Width = (FlowW - 6) / 2, Height = 30, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Gold, TextAlign = ContentAlignment.MiddleLeft };
            _lblRef.Name = "lblRef";
            refLine.Controls.Add(btnCaptureRef);
            refLine.Controls.Add(_lblRef);
            flow.Controls.Add(refLine);

            // 标定状态
            _lblCalibStatus = new Label { Width = FlowW, Height = 22, Font = new Font("Consolas", 8F), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft };
            flow.Controls.Add(_lblCalibStatus);

            // 自动适配ROI按鈕
            var btnAutoFitRoi = new Button { Text = "🎯 自动适配 ROI", Width = FlowW, Height = 30, BackColor = Color.FromArgb(40, 160, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnAutoFitRoi.Click += BtnAutoFitRoi_Click;
            flow.Controls.Add(btnAutoFitRoi);

            // 视角
            flow.Controls.Add(SectionLabel("🔭 视角控制"));
            var viewRow = new FlowLayoutPanel { Width = FlowW, Height = 26 };
            viewRow.Controls.Add(new Label { Text = "Yaw:", Width = 36, Height = 22, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.LightGray, Font = new Font("Consolas", 8F) });
            _numYaw = new NumericUpDown { Width = (FlowW - 90) / 2, Height = 22, Minimum = -180, Maximum = 180, Value = 20, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numYaw.ValueChanged += (_, __) => RedrawCloud();
            viewRow.Controls.Add(_numYaw);
            viewRow.Controls.Add(new Label { Text = "Pitch:", Width = 46, Height = 22, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.LightGray, Font = new Font("Consolas", 8F) });
            _numPitch = new NumericUpDown { Width = (FlowW - 90) / 2, Height = 22, Minimum = -80, Maximum = 80, Value = -12, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numPitch.ValueChanged += (_, __) => RedrawCloud();
            viewRow.Controls.Add(_numPitch);
            flow.Controls.Add(viewRow);

            // === DepthMin/Max 调节 ===
            flow.Controls.Add(SectionLabel("📏 Z轴 深度范围 (mm)"));
            var dmLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numDepthMin = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 5000, Value = 1000, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numDepthMin.ValueChanged += (_, __) => { SyncTrackbarAndRecalc(_numDepthMin, _tbDepthMin, _lblDepthMinVal); };
            _tbDepthMin = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 5000, Value = 1000, TickFrequency = 200, Height = 28 };
            _tbDepthMin.Scroll += (_, __) => { _numDepthMin.Value = _tbDepthMin.Value; };
            _lblDepthMinVal = new Label { Width = 55, Text = "1000", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            dmLine.Controls.Add(new Label { Text = "Min:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            dmLine.Controls.Add(_numDepthMin);
            dmLine.Controls.Add(_tbDepthMin);
            dmLine.Controls.Add(_lblDepthMinVal);
            flow.Controls.Add(dmLine);

            var dxLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numDepthMax = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 8000, Value = 3000, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numDepthMax.ValueChanged += (_, __) => { SyncTrackbarAndRecalc(_numDepthMax, _tbDepthMax, _lblDepthMaxVal); };
            _tbDepthMax = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 8000, Value = 3000, TickFrequency = 500, Height = 28 };
            _tbDepthMax.Scroll += (_, __) => { _numDepthMax.Value = _tbDepthMax.Value; };
            _lblDepthMaxVal = new Label { Width = 55, Text = "3000", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            dxLine.Controls.Add(new Label { Text = "Max:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            dxLine.Controls.Add(_numDepthMax);
            dxLine.Controls.Add(_tbDepthMax);
            dxLine.Controls.Add(_lblDepthMaxVal);
            flow.Controls.Add(dxLine);

            // === X ROI 范围 ===
            flow.Controls.Add(SectionLabel("↔ X轴 ROI范围 (mm) — 限定货位"));
            _lblDetectedXRange = new Label { Width = FlowW, Height = 18, Text = "检测范围: --", Font = new Font("Consolas", 8F), ForeColor = Color.Cyan, TextAlign = ContentAlignment.MiddleLeft };
            flow.Controls.Add(_lblDetectedXRange);

            var xMinLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numXMinRoi = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 10000, DecimalPlaces = 0, Value = 0, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numXMinRoi.ValueChanged += (_, __) => { if (_numXMinRoi.Value >= _numXMaxRoi.Value) _numXMaxRoi.Value = _numXMinRoi.Value + 50; SyncRoiTrackbarAndRecalc(_numXMinRoi, _tbXMinRoi, _lblXMinRoiVal); };
            _tbXMinRoi = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 10000, Value = 0, TickFrequency = 500, Height = 28 };
            _tbXMinRoi.Scroll += (_, __) => { _numXMinRoi.Value = _tbXMinRoi.Value; };
            _lblXMinRoiVal = new Label { Width = 55, Text = "0", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            xMinLine.Controls.Add(new Label { Text = "Min:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            xMinLine.Controls.Add(_numXMinRoi); xMinLine.Controls.Add(_tbXMinRoi); xMinLine.Controls.Add(_lblXMinRoiVal);
            flow.Controls.Add(xMinLine);

            var xMaxLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numXMaxRoi = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 10000, DecimalPlaces = 0, Value = 0, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numXMaxRoi.ValueChanged += (_, __) => { if (_numXMaxRoi.Value <= _numXMinRoi.Value) _numXMinRoi.Value = _numXMaxRoi.Value - 50; SyncRoiTrackbarAndRecalc(_numXMaxRoi, _tbXMaxRoi, _lblXMaxRoiVal); };
            _tbXMaxRoi = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 10000, Value = 0, TickFrequency = 500, Height = 28 };
            _tbXMaxRoi.Scroll += (_, __) => { _numXMaxRoi.Value = _tbXMaxRoi.Value; };
            _lblXMaxRoiVal = new Label { Width = 55, Text = "0", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            xMaxLine.Controls.Add(new Label { Text = "Max:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            xMaxLine.Controls.Add(_numXMaxRoi); xMaxLine.Controls.Add(_tbXMaxRoi); xMaxLine.Controls.Add(_lblXMaxRoiVal);
            flow.Controls.Add(xMaxLine);
            flow.Controls.Add(new Label { Width = FlowW, Text = "  Min/Max=0 时使用自动检测范围", Font = new Font("Microsoft YaHei UI", 7F), ForeColor = Color.FromArgb(100, 100, 120) });

            // === Y ROI 范围 ===
            flow.Controls.Add(SectionLabel("↕ Y轴 ROI范围 (mm) — 垂直方向"));
            _lblDetectedYRange = new Label { Width = FlowW, Height = 18, Text = "检测范围: --", Font = new Font("Consolas", 8F), ForeColor = Color.Cyan, TextAlign = ContentAlignment.MiddleLeft };
            flow.Controls.Add(_lblDetectedYRange);

            var yMinLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numYMinRoi = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 10000, DecimalPlaces = 0, Value = -800, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numYMinRoi.ValueChanged += (_, __) => { if (_numYMinRoi.Value >= _numYMaxRoi.Value) _numYMaxRoi.Value = _numYMinRoi.Value + 50; SyncRoiTrackbarAndRecalc(_numYMinRoi, _tbYMinRoi, _lblYMinRoiVal); };
            _tbYMinRoi = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 10000, Value = -800, TickFrequency = 500, Height = 28 };
            _tbYMinRoi.Scroll += (_, __) => { _numYMinRoi.Value = _tbYMinRoi.Value; };
            _lblYMinRoiVal = new Label { Width = 55, Text = "-800", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            yMinLine.Controls.Add(new Label { Text = "Min:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            yMinLine.Controls.Add(_numYMinRoi); yMinLine.Controls.Add(_tbYMinRoi); yMinLine.Controls.Add(_lblYMinRoiVal);
            flow.Controls.Add(yMinLine);

            var yMaxLine = new FlowLayoutPanel { Width = FlowW, Height = 28 };
            _numYMaxRoi = new NumericUpDown { Width = 80, Minimum = -10000, Maximum = 10000, DecimalPlaces = 0, Value = 800, Increment = 50, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.LightGray };
            _numYMaxRoi.ValueChanged += (_, __) => { if (_numYMaxRoi.Value <= _numYMinRoi.Value) _numYMinRoi.Value = _numYMaxRoi.Value - 50; SyncRoiTrackbarAndRecalc(_numYMaxRoi, _tbYMaxRoi, _lblYMaxRoiVal); };
            _tbYMaxRoi = new TrackBar { Width = FlowW - 170, Minimum = -10000, Maximum = 10000, Value = 800, TickFrequency = 500, Height = 28 };
            _tbYMaxRoi.Scroll += (_, __) => { _numYMaxRoi.Value = _tbYMaxRoi.Value; };
            _lblYMaxRoiVal = new Label { Width = 55, Text = "800", TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Color.Cyan };
            yMaxLine.Controls.Add(new Label { Text = "Max:", Width = 30, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Font = new Font("Consolas", 8F) });
            yMaxLine.Controls.Add(_numYMaxRoi); yMaxLine.Controls.Add(_tbYMaxRoi); yMaxLine.Controls.Add(_lblYMaxRoiVal);
            flow.Controls.Add(yMaxLine);

            // 结果信息
            flow.Controls.Add(SectionLabel("✨ 实时检测结果"));
            _lblResult = new Label { Width = FlowW, Height = 220, AutoSize = false, Font = new Font("Consolas", 11F, FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(20, 28, 20), ForeColor = Color.Lime };
            flow.Controls.Add(_lblResult);

            // 日志
            flow.Controls.Add(SectionLabel("🗋 日志"));
            _txtLog = new TextBox { Width = FlowW, Height = 130, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = Color.FromArgb(25, 25, 32), ForeColor = Color.FromArgb(180, 180, 200), BorderStyle = BorderStyle.FixedSingle };
            flow.Controls.Add(_txtLog);


            // ====== 右侧：可视化 ======
            var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Color.FromArgb(40, 42, 54) };
            mainSplit.Panel2.Controls.Add(rightSplit);

            // 上方：3D 点云视图
            var cloudPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            _pbCloud = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 16, 20), SizeMode = PictureBoxSizeMode.Normal, Cursor = Cursors.Hand };
            _pbCloud.MouseDown += PbCloud_MouseDown;
            _pbCloud.MouseMove += PbCloud_MouseMove;
            _pbCloud.MouseUp += PbCloud_MouseUp;
            _pbCloud.MouseLeave += (s, e) => { _isDragging = false; (_pbCloud as PictureBox)!.Cursor = Cursors.Hand; };
            _pbCloud.MouseWheel += PbCloud_MouseWheel;
            _pbCloud.SizeChanged += (_, __) => RedrawCloud();
            var cloudTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "  🗂 3D点云视图  (左键旋转 | Shift+拖动平移 | 滚轮缩放 | 右键复位)", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(30, 30, 38), TextAlign = ContentAlignment.MiddleLeft };
            cloudPnl.Controls.Add(_pbCloud);
            cloudPnl.Controls.Add(cloudTitle);
            rightSplit.Panel1.Controls.Add(cloudPnl);

            // 下方：深度剖面图 + 预览图
            var botSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5, BackColor = Color.FromArgb(40, 42, 54) };
            rightSplit.Panel2.Controls.Add(botSplit);

            var profPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 24) };
            var profTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "  📈 X轴深度剖面图  (橙色=立柱区域 | 绿色线=开口中心 | 癬色虚线=标准位置)", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(28, 28, 35), TextAlign = ContentAlignment.MiddleLeft };
            _pbProfile = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 24), SizeMode = PictureBoxSizeMode.Normal };
            _pbProfile.SizeChanged += (_, __) => RedrawProfile();
            profPnl.Controls.Add(_pbProfile);
            profPnl.Controls.Add(profTitle);
            botSplit.Panel1.Controls.Add(profPnl);

            var prevPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            var lblPrev = new Label { Dock = DockStyle.Top, Height = 22, Text = "  📷 深度图预览", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(28, 28, 35), TextAlign = ContentAlignment.MiddleLeft };
            _pbPreview = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            prevPnl.Controls.Add(_pbPreview);
            prevPnl.Controls.Add(lblPrev);
            botSplit.Panel2.Controls.Add(prevPnl);

            // 初始比例
            Shown += (_, __) => {
                ApplyProportions(mainSplit, rightSplit, botSplit);
                RedrawProfile();
                RedrawCloud();
            };

            // 当用户手动拖动分割线时重绘
            rightSplit.SplitterMoved += (_, __) => { RedrawProfile(); RedrawCloud(); };
            botSplit.SplitterMoved += (_, __) => RedrawProfile();
        }

        private const int FlowW = 420; // 左侧控件宽度常量

        private static Label BoldLabel(string text) => new Label
        {
            Text = text, Width = FlowW, Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            AutoSize = false, Height = 20, ForeColor = Color.FromArgb(200, 200, 220),
            Margin = new Padding(0, 4, 0, 0)
        };

        private static Label SectionLabel(string text) => new Label
        {
            Text = text, Width = FlowW, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            AutoSize = false, Height = 26, ForeColor = Color.FromArgb(130, 200, 255),
            Margin = new Padding(0, 8, 0, 2), BorderStyle = BorderStyle.None,
            TextAlign = ContentAlignment.BottomLeft
        };

        private void ApplyProportions(SplitContainer main, SplitContainer right, SplitContainer bot)
        {
            if (main.Width > 100) main.SplitterDistance = Math.Max(440, Math.Min(500, main.Width * 30 / 100));
            if (right.Height > 100) right.SplitterDistance = Math.Max(280, right.Height * 58 / 100);
            if (bot.Width > 100) bot.SplitterDistance = Math.Max(400, bot.Width * 72 / 100);
        }

        private void PbCloud_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _lastMouse = e.Location;
                (_pbCloud as PictureBox)!.Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Right)
            {
                // 右键复位视角和缩放
                _zoomLevel = 1.0;
                _panX = 0; _panY = 0;
                _numYaw.Value = 20;
                _numPitch.Value = -12;
            }
        }

        private void PbCloud_MouseUp(object? sender, MouseEventArgs e)
        {
            _isDragging = false;
            (_pbCloud as PictureBox)!.Cursor = Cursors.Hand;
        }

        private void PbCloud_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            if ((ModifierKeys & Keys.Shift) != 0 || e.Button == MouseButtons.Middle)
            {
                // Shift+拖动 或 中键 = 平移
                _panX += dx;
                _panY += dy;
            }
            else
            {
                // 左键拖动 = 旋转
                _numYaw.Value = (decimal)Math.Clamp((double)_numYaw.Value + dx * 0.6, (double)_numYaw.Minimum, (double)_numYaw.Maximum);
                _numPitch.Value = (decimal)Math.Clamp((double)_numPitch.Value - dy * 0.45, (double)_numPitch.Minimum, (double)_numPitch.Maximum);
            }
            RedrawCloud();
        }

        private void PbCloud_MouseWheel(object? sender, MouseEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            _zoomLevel = Math.Clamp(_zoomLevel * factor, 0.2, 50.0);
            RedrawCloud();
            ((HandledMouseEventArgs)e).Handled = true;
        }

        // ============ 逻辑 ============

        private string CurrentSide() => _cmbSide.SelectedItem?.ToString() ?? "left";

        private void LoadConfigValues()
        {
            var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
            if (cfg == null) return;

            string sn = _txtCameraSn.Text.Trim();
            var camParam = cfg.FindCameraParam(sn);

            if (camParam != null)
            {
                // 有该相机的独立参数，优先使用
                _numDepthMin.Value = Math.Clamp(camParam.ZMin, _numDepthMin.Minimum, _numDepthMin.Maximum);
                _numDepthMax.Value = Math.Clamp(camParam.ZMax, _numDepthMax.Minimum, _numDepthMax.Maximum);
                if (camParam.XMax > camParam.XMin)
                {
                    _numXMinRoi.Value = Math.Clamp((decimal)camParam.XMin, _numXMinRoi.Minimum, _numXMinRoi.Maximum);
                    _numXMaxRoi.Value = Math.Clamp((decimal)camParam.XMax, _numXMaxRoi.Minimum, _numXMaxRoi.Maximum);
                }
                if (camParam.YMax > camParam.YMin)
                {
                    _numYMinRoi.Value = Math.Clamp((decimal)camParam.YMin, _numYMinRoi.Minimum, _numYMinRoi.Maximum);
                    _numYMaxRoi.Value = Math.Clamp((decimal)camParam.YMax, _numYMaxRoi.Minimum, _numYMaxRoi.Maximum);
                }
                AppendLog($"已从配置加载相机 [{sn}] 的独立 ROI 参数: Z=[{camParam.ZMin},{camParam.ZMax}], X=[{camParam.XMin:F0},{camParam.XMax:F0}], Y=[{camParam.YMin:F0},{camParam.YMax:F0}], RefX={camParam.ReferenceX:F1}", false);
            }
            else
            {
                // 回退全局默认值
                _numDepthMin.Value = Math.Clamp(cfg.DepthMin, _numDepthMin.Minimum, _numDepthMin.Maximum);
                _numDepthMax.Value = Math.Clamp(cfg.DepthMax, _numDepthMax.Minimum, _numDepthMax.Maximum);

                // 显示标定状态
                var calib = ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                string calibHint = hasCalib ? "（外参标定 ✓，ROI参数未配置）" : "（无外参标定，无ROI参数）";
                AppendLog($"已加载全局默认深度区间参数 {calibHint}", false);
            }

            UpdateCameraSnFromConfig();
            UpdateRefLabel();
        }

        /// <summary>仅加载当前相机 SN 对应的独立 ROI 参数（不触发完整 LoadConfigValues）。</summary>
        private void LoadCameraParams()
        {
            string sn = _txtCameraSn.Text.Trim();
            var camParam = ConfigManager.Instance?.Algorithms?.StackerOffset?.FindCameraParam(sn);
            if (camParam != null)
            {
                _numDepthMin.Value = Math.Clamp(camParam.ZMin, _numDepthMin.Minimum, _numDepthMin.Maximum);
                _numDepthMax.Value = Math.Clamp(camParam.ZMax, _numDepthMax.Minimum, _numDepthMax.Maximum);
                if (camParam.XMax > camParam.XMin)
                {
                    _numXMinRoi.Value = Math.Clamp((decimal)camParam.XMin, _numXMinRoi.Minimum, _numXMinRoi.Maximum);
                    _numXMaxRoi.Value = Math.Clamp((decimal)camParam.XMax, _numXMaxRoi.Minimum, _numXMaxRoi.Maximum);
                }
                else { _numXMinRoi.Value = 0; _numXMaxRoi.Value = 0; }
                if (camParam.YMax > camParam.YMin)
                {
                    _numYMinRoi.Value = Math.Clamp((decimal)camParam.YMin, _numYMinRoi.Minimum, _numYMinRoi.Maximum);
                    _numYMaxRoi.Value = Math.Clamp((decimal)camParam.YMax, _numYMaxRoi.Minimum, _numYMaxRoi.Maximum);
                }
                else { _numYMinRoi.Value = -800; _numYMaxRoi.Value = 800; }
                AppendLog($"切换到相机 [{sn}]，已加载其独立 ROI 参数", false);
            }
            else
            {
                _numXMinRoi.Value = 0; _numXMaxRoi.Value = 0;
                _numYMinRoi.Value = -800; _numYMaxRoi.Value = 800;

                // 检查外参标定状态，给出更清晰提示
                var calib = ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                if (hasCalib)
                    AppendLog($"相机 [{sn}] 外参标定 ✓，但无独立 ROI 参数，使用默认值（深度范围和X/Y范围）", false);
                else
                    AppendLog($"相机 [{sn}] 无外参标定，无 ROI 参数，使用默认值", false);
            }
            UpdateRefLabel();
        }

        /// <summary>获取当前相机对应的 CameraRoiParam（如果存在）或 null。</summary>
        private CameraRoiParam? GetCurrentCameraParam()
        {
            string sn = _txtCameraSn.Text.Trim();
            return ConfigManager.Instance?.Algorithms?.StackerOffset?.FindCameraParam(sn);
        }

        private void UpdateRefLabel()
        {
            double refVal = GetCurrentCameraParam()?.ReferenceX
                ?? ConfigManager.Instance?.Algorithms?.StackerOffset?.ReferenceGapCenterX ?? 0;
            var lbl = Controls.Find("lblRef", true).FirstOrDefault() as Label;
            if (lbl != null)
                lbl.Text = Math.Abs(refVal) < 0.01 ? "标准位置: 未设定" : $"标准: {refVal:F1} mm";
        }

        private void UpdateCameraSnFromConfig()
        {
            var mapping = ConfigManager.Instance?.Algorithms?.StackerOffset?.CameraMapping;
            if (mapping == null) return;
            string sn = CurrentSide() == "right"
                ? (mapping.RightSideSns?.FirstOrDefault() ?? string.Empty)
                : (mapping.LeftSideSns?.FirstOrDefault() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sn)) _txtCameraSn.Text = sn;
        }

        private void SyncTrackbarAndRecalc(NumericUpDown nud, TrackBar tb, Label lbl)
        {
            if (tb.Value != (int)nud.Value) tb.Value = (int)nud.Value;
            lbl.Text = ((int)nud.Value).ToString();
            // 确保 DepthMin < DepthMax
            if (_numDepthMin.Value >= _numDepthMax.Value)
            {
                if (nud == _numDepthMin) _numDepthMax.Value = _numDepthMin.Value + 100;
                else _numDepthMin.Value = _numDepthMax.Value - 100;
            }
            Recalculate();
        }

        /// <summary>
        /// 同步 ROI 类 TrackBar 与 Label，并触发重新计算。
        /// 不含 DepthMin/DepthMax 互斥约束。
        /// </summary>
        private void SyncRoiTrackbarAndRecalc(NumericUpDown nud, TrackBar tb, Label lbl)
        {
            if (tb.Value != (int)nud.Value) tb.Value = (int)nud.Value;
            lbl.Text = ((int)nud.Value).ToString();
            Recalculate();
        }

        private void Recalculate()
        {
            if (_currentFrame == null) return;

            double zMin = (double)_numDepthMin.Value;
            double zMax = (double)_numDepthMax.Value;
            double xMinRoiNum = (double)_numXMinRoi.Value;
            double xMaxRoiNum = (double)_numXMaxRoi.Value;
            double yMinRoiNum = (double)_numYMinRoi.Value;
            double yMaxRoiNum = (double)_numYMaxRoi.Value;

            // 标准位置：优先用相机独立参数，回退全局
            double refGap = GetCurrentCameraParam()?.ReferenceX
                ?? ConfigManager.Instance?.Algorithms?.StackerOffset?.ReferenceGapCenterX ?? 0;

            // X ROI：Min/Max 组合使用
            double? xMinRoI = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMinRoiNum : null;
            double? xMaxRoI = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMaxRoiNum : null;

            // Y ROI：直接使用当前设定的值
            double? yMinRoI = (Math.Abs(yMinRoiNum) > 0.5 || Math.Abs(yMaxRoiNum) > 0.5) ? yMinRoiNum : null;
            double? yMaxRoI = (Math.Abs(yMinRoiNum) > 0.5 || Math.Abs(yMaxRoiNum) > 0.5) ? yMaxRoiNum : null;

            _currentDebug = StackerOffsetAlgo.RunDebug(_currentFrame, zMin, zMax, refGap, xMinRoI, xMaxRoI, yMinRoI, yMaxRoI);

            // 显示检测到的 X 范围
            if (_currentDebug != null && _currentDebug.Success)
                _lblDetectedXRange.Text = $"检测范围: X=[{_currentDebug.XMin:F0}, {_currentDebug.XMax:F0}] mm, bin数={_currentDebug.BinCount}";
            else
                _lblDetectedXRange.Text = "检测范围: --";

            // 显示检测到的 Y 范围
            if (_currentFrame != null)
                _lblDetectedYRange.Text = $"ROI Y=[{yMinRoiNum:F0}, {yMaxRoiNum:F0}] mm";
            else
                _lblDetectedYRange.Text = "检测范围: --";

            UpdateResultLabel();
            RedrawCloud();
            RedrawProfile();
        }

        private void UpdateResultLabel()
        {
            var d = _currentDebug;
            if (d == null) { _lblResult.Text = "无数据"; return; }

            if (!d.Success)
            {
                _lblResult.Text = $"❌ 检测失败\n{d.ErrorMessage}\n\nROI点数: {d.RoiPointCount}";
                _lblResult.ForeColor = Color.OrangeRed;
                return;
            }

            int beamCount = d.BeamRegions.Count;
            int gapCount = d.AllGaps.Count;
            double refVal = d.ReferenceGapCenterX;
            bool hasRef = Math.Abs(refVal) > 0.01;
            string dirStr = d.LateralOffsetMm >= 0 ? "偏右 →" : "← 偏左";
            string statusStr = Math.Abs(d.LateralOffsetMm) < 5 ? "✓ 对准良好" :
                               Math.Abs(d.LateralOffsetMm) < 20 ? "⚠ 轻微偏移" : "✗ 偏移较大";

            _lblResult.Text =
                $"┌─ 检测结果 ─────────────────\n" +
                $"│ 偏移量:    {d.LateralOffsetMm:F2} mm  {dirStr}\n" +
                $"│ 状态:      {statusStr}\n" +
                $"│ 当前开口中心: {d.GapCenterX:F1} mm\n" +
                (hasRef ? $"│ 标准开口中心: {refVal:F1} mm\n" : "│ 标准开口中心: (未设定)\n") +
                $"│ 主开口宽度: {d.GapWidthMm:F1} mm\n" +
                $"│ 检测到梁:   {beamCount} 根  | 货位: {gapCount} 个\n" +
                $"│ ROI内总点:  {d.RoiPointCount:N0}\n" +
                $"│ 背景Z/梁阈值: {d.BackgroundZ:F0} / {d.BeamZThreshold:F0} mm\n" +
                $"└────────────────────────────";
            _lblResult.ForeColor = Color.White;
        }

        // ============ 3D 点云绘制 ============

        private void RedrawCloud()
        {
            if (_currentFrame == null) return;

            int w = Math.Max(320, _pbCloud.ClientSize.Width);
            int h = Math.Max(240, _pbCloud.ClientSize.Height);
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // 用 GDI 绘制背景 + ROI 框
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(16, 16, 18));
                double yaw = (double)_numYaw.Value * Math.PI / 180.0;
                double pitch = (double)_numPitch.Value * Math.PI / 180.0;
                double zMin = (double)_numDepthMin.Value;
                double zMax = (double)_numDepthMax.Value;
                double yMinViz = (double)_numYMinRoi.Value;
                double yMaxViz = (double)_numYMaxRoi.Value;
                double xMinViz = (double)_numXMinRoi.Value;
                double xMaxViz = (double)_numXMaxRoi.Value;
                DrawDepthRangeCuboid(g, zMin, zMax, xMinViz, xMaxViz, yMinViz, yMaxViz, yaw, pitch, w, h);

                // 叠加文字：标定状态 + 缩放 + 提示
                using var infoFont = new Font("Consolas", 9F, FontStyle.Bold);
                using var hintFont = new Font("Microsoft YaHei UI", 7F);
                string? sn = _txtCameraSn?.Text?.Trim();
                var calib = string.IsNullOrWhiteSpace(sn) ? null : ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                string calibText = hasCalib
                    ? $"✓ 标定已应用 (R|T) | {StackerOffsetAlgo.GetDepthAxisDescription(sn!)}"
                    : "⚠ 无标定 — 显示相机原始坐标";
                Color calibColor = hasCalib ? Color.Lime : Color.OrangeRed;
                g.DrawString(calibText, infoFont, new SolidBrush(calibColor), 10, h - 52);
                g.DrawString($"缩放 ×{_zoomLevel:F1}", infoFont, Brushes.Gray, 10, h - 34);
                g.DrawString("左键旋转 | Shift+拖/中键平移 | 滚轮缩放 | 右键复位", hintFont, Brushes.DimGray, 10, h - 18);
            }

            // ---- 点云渲染（LockBits 高性能） ----
            double yaw2 = (double)_numYaw.Value * Math.PI / 180.0;
            double pitch2 = (double)_numPitch.Value * Math.PI / 180.0;
            double zMin2 = (double)_numDepthMin.Value;
            double zMax2 = (double)_numDepthMax.Value;
            double yLo2 = (double)_numYMinRoi.Value;
            double yHi2 = (double)_numYMaxRoi.Value;
            double xMinRoiNum = (double)_numXMinRoi.Value;
            double xMaxRoiNum = (double)_numXMaxRoi.Value;
            double? xMinRoI2 = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMinRoiNum : null;
            double? xMaxRoI2 = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMaxRoiNum : null;
            double beamThresh = _currentDebug?.BeamZThreshold ?? (zMin2 * 0.8);

            var basePts = StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame);
            if (basePts == null || basePts.Count == 0)
            {
                _pbCloud.Image?.Dispose();
                _pbCloud.Image = bmp;
                UpdateCalibStatus();
                return;
            }

            int step = Math.Max(1, (int)Math.Sqrt(basePts.Count / 120000));
            int totalStep = step * step;

            // 预收集所有投影点，按 Z 排序（从远到近，近点覆盖远点）
            var drawList = new List<(int sx, int sy, byte r, byte g, byte b)>(basePts.Count / totalStep + 100);
            for (int i = 0; i < basePts.Count; i += totalStep)
            {
                var pt = basePts[i];
                if (!ProjectPt(pt.X, -pt.Y, pt.Z, yaw2, pitch2, w, h, out int sx, out int sy))
                    continue;

                bool inRoi = pt.Y >= yLo2 && pt.Y <= yHi2 && pt.Z >= zMin2 && pt.Z <= zMax2;
                if (inRoi && xMinRoI2.HasValue && pt.X < xMinRoI2.Value) inRoi = false;
                if (inRoi && xMaxRoI2.HasValue && pt.X > xMaxRoI2.Value) inRoi = false;
                
                if (inRoi)
                {
                    bool isBeam = pt.Z < beamThresh;
                    bool isBracket = false;
                    var d = _currentDebug;
                    if (isBeam && d != null && !double.IsNaN(d.RefinedLeftBeamInnerX))
                    {
                        if (pt.X >= d.RefinedLeftBeamInnerX && pt.X <= d.RefinedLeftBeamInnerX + d.LeftBracketWidthMm) isBracket = true;
                        if (pt.X >= d.RefinedRightBeamInnerX - d.RightBracketWidthMm && pt.X <= d.RefinedRightBeamInnerX) isBracket = true;
                    }
                    
                    if (isBracket)
                        drawList.Add((sx, sy, 0, 191, 255)); // 蓝色显示托臂
                    else
                        drawList.Add((sx, sy, (byte)(isBeam ? 255 : 70), (byte)(isBeam ? 140 : 180), (byte)(isBeam ? 50 : 240)));
                }
                else
                {
                    drawList.Add((sx, sy, (byte)105, (byte)105, (byte)105));
                }
            }

            // LockBits 直接写入像素
            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = bmpData.Stride;
                int bufSize = stride * h;
                byte[] buffer = new byte[bufSize];

                // 读取当前 bmp 内容（保留背景文字 + ROI 框）
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, bufSize);

                void SetPixel(byte[] buf, int px, int py, byte r, byte g, byte b)
                {
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) return;
                    int off = py * stride + px * 4;
                    buf[off] = b;     // B
                    buf[off + 1] = g; // G
                    buf[off + 2] = r; // R
                    buf[off + 3] = 255;
                }

                // 根据缩放级别动态调整点的大小（最少1像素，最多放大到7x7）
                int dynamicSz = (int)(Math.Max(1.0, _zoomLevel * 0.8));
                int dotSz = step <= 1 ? dynamicSz : 1;
                int hsz = dotSz / 2;

                foreach (var (sx, sy, r, g, b) in drawList)
                {
                    for (int dy = -hsz; dy <= hsz; dy++)
                        for (int dx = -hsz; dx <= hsz; dx++)
                            SetPixel(buffer, sx + dx, sy + dy, r, g, b);
                }

                // 写回 bitmap
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, bufSize);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            _pbCloud.Image?.Dispose();
            _pbCloud.Image = bmp;
            UpdateCalibStatus();
        }

        private void UpdateCalibStatus()
        {
            string? sn = _txtCameraSn?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(sn))
            {
                _lblCalibStatus.Text = "📷 标定: — 未指定相机";
                _lblCalibStatus.ForeColor = Color.Gray;
                return;
            }
            var calib = ConfigManager.GetCalibration(sn);
            bool hasCalib = calib != null && calib.IsValid;
            if (hasCalib)
            {
                string desc = StackerOffsetAlgo.GetDepthAxisDescription(sn);
                _lblCalibStatus.Text = $"📷 标定: ✓ 已激活 | {desc} | T={calib!.TranslationVector[0]:F0},{calib.TranslationVector[1]:F0},{calib.TranslationVector[2]:F0}";
                _lblCalibStatus.ForeColor = Color.LimeGreen;
            }
            else
            {
                _lblCalibStatus.Text = "📷 标定: ✗ 未激活 — 深度=SDK_X (未变换)";
                _lblCalibStatus.ForeColor = Color.OrangeRed;
            }
        }

        private void DrawDepthRangeCuboid(Graphics g, double zMin, double zMax, double xMin, double xMax, double yMin, double yMax, double yaw, double pitch, int w, int h)
        {
            // X ROI 为 0 时使用自动检测范围（优先 _currentDebug 的 XMin/XMax，回退 ±600）
            double xLo, xHi;
            bool xAuto = Math.Abs(xMin) < 0.5 && Math.Abs(xMax) < 0.5;
            if (xAuto && _currentDebug != null && _currentDebug.XMax > _currentDebug.XMin)
            {
                xLo = _currentDebug.XMin;
                xHi = _currentDebug.XMax;
            }
            else if (xAuto)
            {
                xLo = -600;
                xHi = 600;
            }
            else
            {
                xLo = xMin;
                xHi = xMax;
            }

            // 构建立方体 8 个角点
            float[] xs = { (float)xLo, (float)xHi, (float)xHi, (float)xLo, (float)xLo, (float)xHi, (float)xHi, (float)xLo };
            float[] ys = { (float)yMin, (float)yMin, (float)yMax, (float)yMax, (float)yMin, (float)yMin, (float)yMax, (float)yMax };
            float[] zs = { (float)zMin, (float)zMin, (float)zMin, (float)zMin, (float)zMax, (float)zMax, (float)zMax, (float)zMax };

            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                ProjectPt(xs[i], -ys[i], zs[i], yaw, pitch, w, h, out int sx, out int sy);
                pts[i] = new PointF(sx, sy);
            }

            using var pen = new Pen(Color.FromArgb(100, 200, 255, 100), 1.5f) { DashStyle = DashStyle.Dash };
            int[][] edges = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };
            foreach (var e in edges) g.DrawLine(pen, pts[e[0]], pts[e[1]]);

            // Z 标签
            using var f = new Font("Consolas", 9F);
            float yLabel = (float)yMin - 50;
            ProjectPt(0, (float)-yLabel, (float)zMin, yaw, pitch, w, h, out int lx, out int ly);
            using var bLo = new SolidBrush(Color.FromArgb(200, 255, 100));
            g.DrawString($"Z≈{zMin:F0}", f, bLo, lx, ly);
            ProjectPt(0, (float)-yLabel, (float)zMax, yaw, pitch, w, h, out lx, out ly);
            g.DrawString($"Z≈{zMax:F0}", f, Brushes.LightGreen, lx, ly);

            // X 标签
            float xLabelY = (float)yMin - 100;
            ProjectPt((float)xMin, (float)-xLabelY, (float)zMin, yaw, pitch, w, h, out int xx1, out _);
            ProjectPt((float)xMax, (float)-xLabelY, (float)zMin, yaw, pitch, w, h, out int xx2, out _);
            using var bx = new SolidBrush(Color.FromArgb(200, 200, 255));
            g.DrawString($"X={xMin:F0}", f, bx, xx1 - 20, ly + 16);
            g.DrawString($"X={xMax:F0}", f, bx, xx2 - 20, ly + 16);
        }

        private bool ProjectPt(double x, double y, double z, double yaw, double pitch, int w, int h, out int sx, out int sy)
        {
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
            double x1 = x * cosY + z * sinY;
            double z1 = -x * sinY + z * cosY;
            double y2 = y * cosP - z1 * sinP;
            double z2 = y * sinP + z1 * cosP;
            double dist = 4000.0;
            double d = z2 + dist;
            if (d <= 10) { sx = sy = 0; return false; }
            double f = 900.0 * _zoomLevel;
            sx = (int)(w * 0.5 + x1 * f / d + _panX);
            sy = (int)(h * 0.5 - y2 * f / d + _panY);
            return sx >= -10 && sx < w + 10 && sy >= -10 && sy < h + 10;
        }

        // ============ 深度剖面图 ============

        private void RedrawProfile()
        {
            int w = Math.Max(320, _pbProfile.ClientSize.Width);
            int h = Math.Max(180, _pbProfile.ClientSize.Height);
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(20, 20, 24));

            var d = _currentDebug;
            if (d == null || d.BinCount == 0 || d.DepthProfile.Length == 0)
            {
                using var ff = new Font("Microsoft YaHei UI", 11F);
                g.DrawString("无数据 — 请先抓取一帧", ff, Brushes.Gray, 30, h / 2 - 15);
                _pbProfile.Image = bmp;
                return;
            }

            int marginL = 55, marginR = 30, marginT = 30, marginB = 40;
            int pw = w - marginL - marginR, ph = h - marginT - marginB;
            if (pw <= 0 || ph <= 0) { _pbProfile.Image = bmp; return; }

            // 坐标范围
            double zMin = (double)_numDepthMin.Value;
            double zMax = (double)_numDepthMax.Value;
            double xMin = d.XMin, xMax = d.XMax;
            double xRange = xMax - xMin;
            double zRange = zMax - zMin;
            if (xRange < 1 || zRange < 1) { _pbProfile.Image = bmp; return; }

            // 网格
            using var gridPen = new Pen(Color.FromArgb(40, 45, 55), 0.5f);
            for (int i = 0; i <= 8; i++)
            {
                int gy = marginT + ph * i / 8;
                g.DrawLine(gridPen, marginL, gy, marginL + pw, gy);
            }
            for (int i = 0; i <= 10; i++)
            {
                int gx = marginL + pw * i / 10;
                g.DrawLine(gridPen, gx, marginT, gx, marginT + ph);
            }

            // 坐标轴标签
            using var axisFont = new Font("Consolas", 8F);
            using var axisBrush = new SolidBrush(Color.FromArgb(150, 155, 170));
            for (int i = 0; i <= 8; i += 2)
            {
                double zv = zMax - zRange * i / 8;
                g.DrawString($"{zv:F0}", axisFont, axisBrush, 2, marginT + ph * i / 8 - 7);
            }
            for (int i = 0; i <= 10; i += 2)
            {
                double xv = xMin + xRange * i / 10;
                var s = $"{xv:F0}";
                var sz = g.MeasureString(s, axisFont);
                g.DrawString(s, axisFont, axisBrush, marginL + pw * i / 10 - sz.Width / 2, marginT + ph + 4);
            }
            // 轴标签
            using var lblFont = new Font("Consolas", 9F, FontStyle.Bold);
            g.DrawString("Z(mm)↓", lblFont, Brushes.Gray, 4, marginT - 3);
            g.DrawString("→ X(mm) 导轨方向", lblFont, Brushes.Gray, marginL + pw - 120, marginT + ph + 18);

            // 深度曲线
            var curvePts = new List<PointF>();
            for (int i = 0; i < d.BinCount; i++)
            {
                if (d.DepthProfile[i] >= double.MaxValue * 0.5) continue;
                float cx = marginL + (float)((d.BinXCenters[i] - xMin) / xRange * pw);
                float cy = marginT + (float)((zMax - d.DepthProfile[i]) / zRange * ph);
                curvePts.Add(new PointF(cx, cy));
            }
            if (curvePts.Count > 1)
            {
                using var curvePen = new Pen(Color.FromArgb(80, 200, 255), 1.8f);
                g.DrawLines(curvePen, curvePts.ToArray());
            }

            // 梁判定阈值线
            float threshY = marginT + (float)((zMax - d.BeamZThreshold) / zRange * ph);
            if (d.BeamZThreshold > 0 && threshY > marginT && threshY < marginT + ph)
            {
                using var threshPen = new Pen(Color.FromArgb(255, 200, 50), 1.2f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 8f, 4f } };
                g.DrawLine(threshPen, marginL, threshY, marginL + pw, threshY);
                g.DrawString($"梁阈值={d.BeamZThreshold:F0}", axisFont, Brushes.Gold, marginL + 4, threshY - 16);
            }

            // 梁区域高亮
            foreach (var (lx, rx, _, _) in d.BeamRegions)
            {
                float blx = marginL + (float)((lx - xMin) / xRange * pw);
                float brx = marginL + (float)((rx - xMin) / xRange * pw);
                using var beamBrush = new SolidBrush(Color.FromArgb(50, 255, 140, 50));
                g.FillRectangle(beamBrush, blx, marginT, brx - blx, ph);
                using var beamPen = new Pen(Color.FromArgb(255, 140, 50), 1.5f);
                g.DrawLine(beamPen, blx, marginT, blx, marginT + ph);
                g.DrawLine(beamPen, brx, marginT, brx, marginT + ph);
            }

            // 精化边缘可视化：用蓝色标注托臂被排除的区域
            if (!double.IsNaN(d.RefinedLeftBeamInnerX) && !double.IsNaN(d.RefinedRightBeamInnerX))
            {
                // 找到主开口两侧梁的原始内边缘（最靠近开口的边缘）
                float rawGapL = float.NaN, rawGapR = float.NaN;
                if (d.AllGaps.Count > 0)
                {
                    // 找最宽开口
                    var mainGap = d.AllGaps.OrderByDescending(g2 => g2.width).First();
                    rawGapL = marginL + (float)((mainGap.leftX  - xMin) / xRange * pw);
                    rawGapR = marginL + (float)((mainGap.rightX - xMin) / xRange * pw);
                }

                float refinedLX = marginL + (float)((d.RefinedLeftBeamInnerX  - xMin) / xRange * pw);
                float refinedRX = marginL + (float)((d.RefinedRightBeamInnerX - xMin) / xRange * pw);

                // 蓝色半透明：托臂所在区域（精化边缘到原始边缘之间）
                if (!float.IsNaN(rawGapL) && rawGapL > refinedLX)
                {
                    using var bracketBrush = new SolidBrush(Color.FromArgb(80, 60, 160, 255));
                    g.FillRectangle(bracketBrush, refinedLX, marginT, rawGapL - refinedLX, ph);
                    using var bracketPen = new Pen(Color.FromArgb(180, 60, 160, 255), 2f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 5f, 3f } };
                    g.DrawLine(bracketPen, refinedLX, marginT + 5, refinedLX, marginT + ph - 5);
                    g.DrawString($"←{d.LeftBracketWidthMm:F0}mm托臂", axisFont, new SolidBrush(Color.FromArgb(160, 200, 255)), refinedLX + 2, marginT + 22);
                }
                if (!float.IsNaN(rawGapR) && rawGapR < refinedRX)
                {
                    using var bracketBrush = new SolidBrush(Color.FromArgb(80, 60, 160, 255));
                    g.FillRectangle(bracketBrush, rawGapR, marginT, refinedRX - rawGapR, ph);
                    using var bracketPen = new Pen(Color.FromArgb(180, 60, 160, 255), 2f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 5f, 3f } };
                    g.DrawLine(bracketPen, refinedRX, marginT + 5, refinedRX, marginT + ph - 5);
                    g.DrawString($"托臂{d.RightBracketWidthMm:F0}mm→", axisFont, new SolidBrush(Color.FromArgb(160, 200, 255)), refinedRX - 70, marginT + 22);
                }
            }

            // 开口区域高亮 + 中心标记
            foreach (var (gl, gr, gc, _) in d.AllGaps)
            {
                float glx = marginL + (float)((gl - xMin) / xRange * pw);
                float grx = marginL + (float)((gr - xMin) / xRange * pw);

                bool isMain = Math.Abs(gc - d.GapWidthMm) < 0.1 || d.AllGaps.Count == 1 ||
                              d.AllGaps.All(g2 => g2.width <= (gr - gl));

                // 用最宽间隙判断主间隙
                isMain = (gr - gl) >= d.AllGaps.Max(g2 => g2.width) - 0.1;

                if (isMain)
                {
                    using var gapBrush = new SolidBrush(Color.FromArgb(40, 0, 255, 100));
                    g.FillRectangle(gapBrush, glx, marginT, grx - glx, ph);

                    // 精化后的中心线（蓝白色）
                    if (!double.IsNaN(d.RefinedGapCenterX))
                    {
                        float refGcx = marginL + (float)((d.RefinedGapCenterX - xMin) / xRange * pw);
                        using var refinedCenterPen = new Pen(Color.FromArgb(100, 220, 255), 2.5f);
                        g.DrawLine(refinedCenterPen, refGcx, marginT + 5, refGcx, marginT + ph - 5);
                        g.DrawString($"精化中心", axisFont, new SolidBrush(Color.FromArgb(100, 220, 255)), refGcx + 3, marginT + 40);
                    }

                    // 原始中心（淡绿）
                    float gcx = marginL + (float)((gc - xMin) / xRange * pw);
                    using var centerPen = new Pen(Color.FromArgb(130, 220, 130), 1.2f) { DashStyle = DashStyle.Dot };
                    g.DrawLine(centerPen, gcx, marginT + 5, gcx, marginT + ph - 5);

                    // 参考线：标准位置开口中心
                    double refVal = d.ReferenceGapCenterX;
                    float refX = marginL + (float)((refVal - xMin) / xRange * pw);
                    if (refX > marginL && refX < marginL + pw)
                    {
                        using var refPen = new Pen(Color.White, 1.2f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 4f } };
                        g.DrawLine(refPen, refX, marginT, refX, marginT + ph);
                        string refLabel = Math.Abs(refVal) > 0.01 ? $"标准={refVal:F0}mm" : "标准(未设)";
                        g.DrawString(refLabel, axisFont, Brushes.White, refX + 2, marginT + 2);
                    }

                    // 偏移量标注
                    float finalCx = !double.IsNaN(d.RefinedGapCenterX)
                        ? marginL + (float)((d.RefinedGapCenterX - xMin) / xRange * pw)
                        : gcx;
                    if (Math.Abs(d.LateralOffsetMm) > 0.1 && refX > marginL && refX < marginL + pw)
                    {
                        float arrowY = marginT + ph / 2;
                        using var arrowPen = new Pen(Color.FromArgb(100, 220, 255), 2f) { EndCap = LineCap.ArrowAnchor };
                        g.DrawLine(arrowPen, refX, arrowY, finalCx, arrowY);
                        string offStr = $"偏移 {d.LateralOffsetMm:F1}mm";
                        var offSz = g.MeasureString(offStr, axisFont);
                        float midX = (refX + finalCx) / 2;
                        g.DrawString(offStr, axisFont, new SolidBrush(Color.FromArgb(100, 220, 255)), midX - offSz.Width / 2, arrowY - 18);
                    }
                }
                else
                {
                    using var gapBrush = new SolidBrush(Color.FromArgb(20, 100, 100, 100));
                    g.FillRectangle(gapBrush, glx, marginT, grx - glx, ph);
                }
            }


            // 背景深度参考线
            float bgY = marginT + (float)((zMax - d.BackgroundZ) / zRange * ph);
            if (d.BackgroundZ > 0 && bgY > marginT && bgY < marginT + ph)
            {
                using var bgPen = new Pen(Color.FromArgb(120, 120, 120), 0.8f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 2f, 6f } };
                g.DrawLine(bgPen, marginL, bgY, marginL + pw, bgY);
                g.DrawString($"背景Z={d.BackgroundZ:F0}", axisFont, Brushes.DarkGray, marginL + pw - 110, bgY - 14);
            }

            // 图例
            int legendX = marginL + pw - 180;
            using var legendBrush = new SolidBrush(Color.FromArgb(80, 200, 255));
            g.FillRectangle(legendBrush, legendX, marginT + 4, 12, 3);
            g.DrawString("深度剖面", new Font("Consolas", 7F), Brushes.LightGray, legendX + 16, marginT);
            using var beamBrush2 = new SolidBrush(Color.FromArgb(255, 140, 50));
            g.FillRectangle(beamBrush2, legendX, marginT + 14, 12, 3);
            g.DrawString("竖直梁区域", new Font("Consolas", 7F), Brushes.LightGray, legendX + 16, marginT + 10);

            _pbProfile.Image?.Dispose();
            _pbProfile.Image = bmp;
        }

        // ============ 相机操作 ============

        private async void BtnInit_Click(object? sender, EventArgs e)
        {
            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrWhiteSpace(sn)) { AppendLog("请先填写相机SN", true); return; }
            if (DeviceManager.GetCamera(sn) != null) { AppendLog($"相机已在池中: {sn}", false); return; }

            var cfg = ConfigManager.Instance?.Cameras?.FirstOrDefault(c =>
                string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase) &&
                (c.Type?.Contains("Tycam", StringComparison.OrdinalIgnoreCase) == true ||
                 c.Type?.Contains("Percipio", StringComparison.OrdinalIgnoreCase) == true));
            if (cfg == null) { AppendLog($"配置中未找到该SN的3D相机: {sn}", true); return; }

            await Task.Run(() => DeviceManager.Initialize(new List<CameraConfig> { cfg }, m => BeginInvoke(new Action(() => AppendLog(m, false)))));

            bool initOk = DeviceManager.GetCamera(sn) != null;
            if (initOk)
            {
                var calib = ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                AppendLog($"相机初始化成功: {sn} | 外参标定: {(hasCalib ? "✓" : "✗ 未标定")} | ROI参数: {(ConfigManager.Instance?.Algorithms?.StackerOffset?.FindCameraParam(sn) != null ? "✓" : "待配置")}", false);
            }
            else
            {
                AppendLog($"相机初始化失败: {sn}", true);
            }
        }

        private async void BtnGrab_Click(object? sender, EventArgs e)
        {
            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrWhiteSpace(sn)) { AppendLog("请先填写相机SN", true); return; }
            var cam = DeviceManager.GetCamera(sn);
            if (cam == null) { AppendLog("相机不在设备池，请先初始化", true); return; }
            if (!cam.IsConnected) { AppendLog("相机未连接，请检查硬件或重新初始化", true); return; }

            try
            {
                var obj = await cam.GrabFrameAsync();
                if (obj is Bitmap bmp)
                {
                    // 超时返回的红色错误图（640x480），说明相机取流超时
                    if (bmp.Width == 640 && bmp.Height == 480)
                    {
                        AppendLog("抓图超时(5秒内无深度帧回调)，请检查: 1)相机是否正常取流 2)是否被其他程序占用 3)重新初始化后重试", true);
                    }
                    else
                    {
                        AppendLog("抓图返回2D图像而非深度帧，请确认相机类型为3D深度相机", true);
                    }
                    bmp.Dispose();
                    return;
                }
                if (obj is not DepthFrameData depth) { AppendLog("抓图返回非深度帧", true); return; }

                _currentFrame = depth;
                _pbPreview.Image?.Dispose();
                _pbPreview.Image = (Image)depth.PreviewImage.Clone();

                // 显示标定状态
                var calib = ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                string calibStatus = hasCalib ? "标定✓" : "未标定";
                AppendLog($"抓图成功 {depth.CameraSn} {depth.Width}x{depth.Height} | {calibStatus} | {(hasCalib ? $"R|T已应用" : "使用原始坐标")}", false);

                Recalculate();
            }
            catch (Exception ex) { AppendLog($"抓图失败: {ex.Message}", true); }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
            if (cfg == null) { AppendLog("配置对象不可用", true); return; }

            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrWhiteSpace(sn)) { AppendLog("请先填写相机SN", true); return; }

            int zMinVal = (int)_numDepthMin.Value;
            int zMaxVal = (int)_numDepthMax.Value;

            // 查找或创建该相机的 ROI 参数条目
            var camParam = cfg.FindCameraParam(sn);
            bool isNew = camParam == null;
            if (isNew) camParam = new CameraRoiParam { CameraSn = sn };

            camParam.ZMin = zMinVal;
            camParam.ZMax = zMaxVal;

            // X 范围：优先使用手动设定的值，其次使用检测到的范围
            if (Math.Abs((double)_numXMinRoi.Value) > 0.5 || Math.Abs((double)_numXMaxRoi.Value) > 0.5)
            {
                camParam.XMin = (double)_numXMinRoi.Value;
                camParam.XMax = (double)_numXMaxRoi.Value;
            }
            else if (_currentDebug?.Success == true)
            {
                camParam.XMin = _currentDebug.XMin;
                camParam.XMax = _currentDebug.XMax;
                AppendLog($"X ROI 使用检测范围: [{camParam.XMin:F0}, {camParam.XMax:F0}]", false);
            }

            // Y 范围：优先使用手动设定的值
            if (Math.Abs((double)_numYMinRoi.Value) > 0.5 || Math.Abs((double)_numYMaxRoi.Value) > 0.5)
            {
                camParam.YMin = (double)_numYMinRoi.Value;
                camParam.YMax = (double)_numYMaxRoi.Value;
            }

            // 保存标准位置参考值（如果有成功检测）
            if (_currentDebug?.Success == true)
            {
                camParam.ReferenceX = Math.Round(_currentDebug.GapCenterX, 2);
            }

            // 加入列表（新条目）/ 列表中原位置不变
            if (isNew) cfg.CameraRoiParams.Add(camParam);

            ConfigManager.SaveConfig();
            UpdateRefLabel();
            AppendLog($"✅ 已保存相机 [{sn}] 的 ROI 参数: Z=[{zMinVal},{zMaxVal}], X=[{camParam.XMin:F0},{camParam.XMax:F0}], Y=[{camParam.YMin:F0},{camParam.YMax:F0}], RefX={camParam.ReferenceX:F1} mm", false);
        }

        /// <summary>
        /// 自动适配ROI：根据当前帧的点云包围盒，自动设置所有ROI范围控件。
        /// 解决标定后坐标轴变化导致 ROI 框与点云方向不一致的问题。
        /// </summary>
        private void BtnAutoFitRoi_Click(object? sender, EventArgs e)
        {
            if (_currentFrame == null)
            {
                AppendLog("请先抓取一帧", true);
                return;
            }

            // GetBasePointsFromFrame 返回标定后的基准坐标系点云，直接计算包围盒即可
            var basePts = StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame);
            if (basePts == null || basePts.Count == 0)
            {
                AppendLog("无法获取点云数据（可能没有深度帧或点数不足）", true);
                return;
            }

            // ---- 计算包围盒（重定向后的坐标系，Z=深度, X=横向, Y=垂直） ----
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;
            double zMin = double.MaxValue, zMax = double.MinValue;
            foreach (var pt in basePts)
            {
                if (pt.X < xMin) xMin = pt.X;
                if (pt.X > xMax) xMax = pt.X;
                if (pt.Y < yMin) yMin = pt.Y;
                if (pt.Y > yMax) yMax = pt.Y;
                if (pt.Z < zMin) zMin = pt.Z;
                if (pt.Z > zMax) zMax = pt.Z;
            }

            double xRange = xMax - xMin;
            double yRange = yMax - yMin;
            double zRange = zMax - zMin;

            // 加 5% margin（最少 50mm）
            double xM = Math.Max(xRange * 0.05, 50);
            double yM = Math.Max(yRange * 0.05, 50);
            double zM = Math.Max(zRange * 0.05, 50);

            // ---- 设置控件值 ----
            _numDepthMin.Value = Math.Clamp((decimal)(zMin - zM), _numDepthMin.Minimum, _numDepthMin.Maximum);
            _numDepthMax.Value = Math.Clamp((decimal)(zMax + zM), _numDepthMax.Minimum, _numDepthMax.Maximum);

            _numXMinRoi.Value = Math.Clamp((decimal)(xMin - xM), _numXMinRoi.Minimum, _numXMinRoi.Maximum);
            _numXMaxRoi.Value = Math.Clamp((decimal)(xMax + xM), _numXMaxRoi.Minimum, _numXMaxRoi.Maximum);

            _numYMinRoi.Value = Math.Clamp((decimal)(yMin - yM), _numYMinRoi.Minimum, _numYMinRoi.Maximum);
            _numYMaxRoi.Value = Math.Clamp((decimal)(yMax + yM), _numYMaxRoi.Minimum, _numYMaxRoi.Maximum);

            string sn = _txtCameraSn.Text.Trim();
            string calibInfo = StackerOffsetAlgo.GetDepthAxisDescription(sn);
            AppendLog($"🎯 自动适配完成 | 包围盒: X=[{xMin:F0},{xMax:F0}] Y=[{yMin:F0},{yMax:F0}] Z(深度)=[{zMin:F0},{zMax:F0}] | {calibInfo}", false);

            Recalculate();
        }

        private void BtnCaptureRef_Click(object? sender, EventArgs e)
        {
            if (_currentDebug == null || !_currentDebug.Success)
            {
                AppendLog("请先抓取一帧并确保检测成功", true);
                return;
            }

            var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
            if (cfg == null) { AppendLog("配置对象不可用", true); return; }

            string sn = _txtCameraSn.Text.Trim();
            double newRef = Math.Round(_currentDebug.GapCenterX, 2);

            // 保存到相机独立参数
            var camParam = cfg.FindCameraParam(sn);
            bool isNew = camParam == null;
            if (isNew) camParam = new CameraRoiParam { CameraSn = sn };
            
            camParam.ReferenceX = newRef;
            camParam.XMin = (double)_numXMinRoi.Value;
            camParam.XMax = (double)_numXMaxRoi.Value;
            camParam.YMin = (double)_numYMinRoi.Value;
            camParam.YMax = (double)_numYMaxRoi.Value;
            camParam.ZMin = (int)_numDepthMin.Value;
            camParam.ZMax = (int)_numDepthMax.Value;
            
            if (isNew) cfg.CameraRoiParams.Add(camParam);

            ConfigManager.SaveConfig();
            UpdateRefLabel();
            AppendLog($"✅ 标准位置已采集: {newRef:F1} mm（相机 [{sn}]，已保存到配置）", false);
            Recalculate();
        }

        private void AppendLog(string msg, bool isErr)
        {
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {(isErr ? "[ERR]" : "[INF]")} {msg}{Environment.NewLine}");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }
    }
}
