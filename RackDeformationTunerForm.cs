using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using pallet_storage_detection_system_Net_V2.Algorithms;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;

namespace pallet_storage_detection_system_Net_V2
{
    /// <summary>
    /// 货架变形检测调参工具 (Flag 3)。
    /// 显示 X 轴深度剖面图、立柱与托臂分离效果，并输出四项变形指标。
    /// </summary>
    public class RackDeformationTunerForm : Form
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
        private SegmentationResult? _currentSeg;
        private bool _isDragging;
        private Point _lastMouse;
        private double _zoomLevel = 1.0;
        private double _panX, _panY;               // 3D 视图平移偏移 (pixels)
        private Label _lblCalibStatus = null!;      // 标定状态指示

        // ---- 标准基准值（从配置加载 / 由"设为标准值"按钮写入）----
        private double _refRackDefLeft;
        private double _refRackDefRight;
        private double _refArmAngleLeft;
        private double _refArmAngleRight;
        private Label _lblRefStatus = null!;        // 标准值状态指示

        // ---- 托臂计算用点云（可视化标记用）----
        private System.Collections.Generic.List<System.Numerics.Vector3> _usedArmPtsL = new();
        private System.Collections.Generic.List<System.Numerics.Vector3> _usedArmPtsR = new();

        public RackDeformationTunerForm()
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
            Text = "货架变形检测(Flag 3) — 调参测试工具";
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
            var btnLine = new FlowLayoutPanel { Width = FlowW, Height = 44, WrapContents = false };
            var btnGrab = new Button { Text = "📸 采集图像", Width = 190, Height = 40, Margin = new Padding(3, 2, 3, 2), BackColor = Color.FromArgb(40, 140, 90), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGrab.Click += BtnGrab_Click;
            var btnSave = new Button { Text = "💾 保存到配置", Width = 190, Height = 40, Margin = new Padding(3, 2, 3, 2), BackColor = Color.FromArgb(60, 80, 130), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSave.Click += BtnSave_Click;
            btnLine.Controls.Add(btnGrab);
            btnLine.Controls.Add(btnSave);
            flow.Controls.Add(btnLine);

            // 标定状态
            _lblCalibStatus = new Label { Width = FlowW, Height = 22, Font = new Font("Consolas", 8F), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft };
            flow.Controls.Add(_lblCalibStatus);

            // 自动适配ROI按鈕
            var btnAutoFitRoi = new Button { Text = "🎯 自动适配 ROI", Width = FlowW, Height = 30, BackColor = Color.FromArgb(40, 160, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnAutoFitRoi.Click += BtnAutoFitRoi_Click;
            flow.Controls.Add(btnAutoFitRoi);

            // 设为标准值按钮
            var btnSetRef = new Button { Text = "📐 设为标准值", Width = FlowW, Height = 30, BackColor = Color.FromArgb(180, 120, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold) };
            btnSetRef.Click += BtnSetRef_Click;
            flow.Controls.Add(btnSetRef);

            // 标准值状态
            _lblRefStatus = new Label { Width = FlowW, Height = 38, Font = new Font("Consolas", 8F), ForeColor = Color.FromArgb(200, 200, 100), TextAlign = ContentAlignment.MiddleLeft, Text = "标准基准: 未设置" };
            flow.Controls.Add(_lblRefStatus);

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
            flow.Controls.Add(SectionLabel("✨ 实时变形检测结果"));
            _lblResult = new Label { Width = FlowW, Height = 300, AutoSize = false, Font = new Font("Consolas", 11F, FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(20, 28, 20), ForeColor = Color.Lime };
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
            _pbCloud = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 16, 20), SizeMode = PictureBoxSizeMode.Normal, Cursor = Cursors.Hand };
            _pbCloud.MouseDown += PbCloud_MouseDown;
            _pbCloud.MouseMove += PbCloud_MouseMove;
            _pbCloud.MouseUp += PbCloud_MouseUp;
            _pbCloud.MouseLeave += (s, e) => { _isDragging = false; (_pbCloud as PictureBox)!.Cursor = Cursors.Hand; };
            _pbCloud.MouseWheel += PbCloud_MouseWheel;
            _pbCloud.SizeChanged += (_, __) => RedrawCloud();
            var cloudTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "  🗂 3D点云视图 (橙=左立柱, 黄=右立柱, 青=左托臂, 绿=右托臂, 品红=计算用点)", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(30, 30, 38), TextAlign = ContentAlignment.MiddleLeft };
            cloudPnl.Controls.Add(_pbCloud);
            cloudPnl.Controls.Add(cloudTitle);
            rightSplit.Panel1.Controls.Add(cloudPnl);

            // 下方：深度剖面图 + 预览图
            var botSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5, BackColor = Color.FromArgb(40, 42, 54) };
            rightSplit.Panel2.Controls.Add(botSplit);

            var profPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 24) };
            var profTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "  📈 X轴深度剖面图  (辅助查看点云提取的准确性)", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(28, 28, 35), TextAlign = ContentAlignment.MiddleLeft };
            _pbProfile = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 24), SizeMode = PictureBoxSizeMode.Normal };
            _pbProfile.SizeChanged += (_, __) => RedrawProfile();
            profPnl.Controls.Add(_pbProfile);
            profPnl.Controls.Add(profTitle);
            botSplit.Panel1.Controls.Add(profPnl);

            var prevPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            var lblPrev = new Label { Dock = DockStyle.Top, Height = 22, Text = "  📷 深度图预览", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = Color.FromArgb(140, 150, 160), BackColor = Color.FromArgb(28, 28, 35), TextAlign = ContentAlignment.MiddleLeft };
            _pbPreview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
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

        private const int FlowW = 420;

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
            if (main.Width > 100) main.SplitterDistance = Math.Max(480, Math.Min(560, main.Width * 32 / 100));
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
                _panX += dx;
                _panY += dy;
            }
            else
            {
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
            var cfg = ConfigManager.Instance?.Algorithms?.RackDeformation;
            if (cfg == null) return;

            string sn = _txtCameraSn.Text.Trim();
            var camParam = cfg.FindCameraParam(sn);

            if (camParam != null)
            {
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
                AppendLog($"已从配置加载相机 [{sn}] 的独立 ROI 参数", false);

                // 加载标准基准值
                _refRackDefLeft  = camParam.RefRackDefLeft;
                _refRackDefRight = camParam.RefRackDefRight;
                _refArmAngleLeft = camParam.RefArmAngleLeft;
                _refArmAngleRight = camParam.RefArmAngleRight;
                UpdateRefStatusLabel();
            }
            else
            {
                _numDepthMin.Value = Math.Clamp(cfg.DepthMin, _numDepthMin.Minimum, _numDepthMin.Maximum);
                _numDepthMax.Value = Math.Clamp(cfg.DepthMax, _numDepthMax.Minimum, _numDepthMax.Maximum);
                AppendLog($"已加载全局默认深度区间参数", false);
            }

            UpdateCameraSnFromConfig();
        }

        private void LoadCameraParams()
        {
            string sn = _txtCameraSn.Text.Trim();
            var camParam = ConfigManager.Instance?.Algorithms?.RackDeformation?.FindCameraParam(sn);
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

                // 加载标准基准值
                _refRackDefLeft  = camParam.RefRackDefLeft;
                _refRackDefRight = camParam.RefRackDefRight;
                _refArmAngleLeft = camParam.RefArmAngleLeft;
                _refArmAngleRight = camParam.RefArmAngleRight;
                UpdateRefStatusLabel();

                AppendLog($"切换到相机 [{sn}]", false);
            }
        }

        private void UpdateCameraSnFromConfig()
        {
            var mapping = ConfigManager.Instance?.Algorithms?.RackDeformation?.CameraMapping;
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
            if (_numDepthMin.Value >= _numDepthMax.Value)
            {
                if (nud == _numDepthMin) _numDepthMax.Value = _numDepthMin.Value + 100;
                else _numDepthMin.Value = _numDepthMax.Value - 100;
            }
            Recalculate();
        }

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

            double? xMinRoI = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMinRoiNum : null;
            double? xMaxRoI = (Math.Abs(xMinRoiNum) > 0.5 || Math.Abs(xMaxRoiNum) > 0.5) ? xMaxRoiNum : null;

            double yMinRoI = (Math.Abs(yMinRoiNum) > 0.5 || Math.Abs(yMaxRoiNum) > 0.5) ? yMinRoiNum : -800;
            double yMaxRoI = (Math.Abs(yMinRoiNum) > 0.5 || Math.Abs(yMaxRoiNum) > 0.5) ? yMaxRoiNum : 800;

            var basePts = StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame);
            if (basePts == null || basePts.Count == 0) return;

            _currentSeg = CloudSegmentationHelper.Segment(
                basePts, zMin, zMax, yMinRoI, yMaxRoI, xMinRoI, xMaxRoI,
                5.0, 3, 500, extractComponentClouds: true);

            if (_currentSeg != null && _currentSeg.Success)
                _lblDetectedXRange.Text = $"检测范围: X=[{_currentSeg.XMin:F0}, {_currentSeg.XMax:F0}] mm";
            else
                _lblDetectedXRange.Text = "检测范围: --";

            _lblDetectedYRange.Text = $"ROI Y=[{yMinRoI:F0}, {yMaxRoI:F0}] mm";

            UpdateResultLabel();
            RedrawCloud();
            RedrawProfile();
        }

        private void UpdateResultLabel()
        {
            var d = _currentSeg;
            if (d == null) { _lblResult.Text = "无数据"; return; }

            if (!d.Success)
            {
                _lblResult.Text = $"❌ 检测失败\n{d.ErrorMessage}\n\nROI点数: {d.RoiPoints.Count}";
                _lblResult.ForeColor = Color.OrangeRed;
                return;
            }

            double rackL = RackDeformationAlgo.ComputeColumnDeformation(d.LeftColumnPoints);
            double rackR = RackDeformationAlgo.ComputeColumnDeformation(d.RightColumnPoints);
            double armL = RackDeformationAlgo.ComputeArmAngle(d.LeftArmPoints, _usedArmPtsL);
            double armR = RackDeformationAlgo.ComputeArmAngle(d.RightArmPoints, _usedArmPtsR);

            // 计算差值 (当前值 - 标准值)
            double diffRackL = rackL - _refRackDefLeft;
            double diffRackR = rackR - _refRackDefRight;
            double diffArmL  = armL  - _refArmAngleLeft;
            double diffArmR  = armR  - _refArmAngleRight;

            bool hasRef = (_refRackDefLeft != 0 || _refRackDefRight != 0 || _refArmAngleLeft != 0 || _refArmAngleRight != 0);
            string refTag = hasRef ? "" : " (无标准值)";

            _lblResult.Text =
                $"┌─ 变形检测结果{refTag} ──────────\n" +
                $"│ 【左立柱】当前:{rackL,7:F2} mm  标准:{_refRackDefLeft,7:F2}  差值:{diffRackL,+7:F2}\n" +
                $"│ 【右立柱】当前:{rackR,7:F2} mm  标准:{_refRackDefRight,7:F2}  差值:{diffRackR,+7:F2}\n" +
                $"│\n" +
                $"│ 【左托臂】当前:{armL,7:F2} °   标准:{_refArmAngleLeft,7:F2}  差值:{diffArmL,+7:F2}\n" +
                $"│ 【右托臂】当前:{armR,7:F2} °   标准:{_refArmAngleRight,7:F2}  差值:{diffArmR,+7:F2}\n" +
                $"│\n" +
                $"│ 立柱点云 (L/R): {d.LeftColumnPoints.Count} / {d.RightColumnPoints.Count}\n" +
                $"│ 托臂点云 (L/R): {d.LeftArmPoints.Count} / {d.RightArmPoints.Count}\n" +
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
                
                // Draw outline box
                DrawDepthRangeCuboid(g, zMin, zMax, xMinViz, xMaxViz, yMinViz, yMaxViz, yaw, pitch, w, h);
                
                using var infoFont = new Font("Consolas", 9F, FontStyle.Bold);
                g.DrawString($"缩放 ×{_zoomLevel:F1}", infoFont, Brushes.Gray, 10, h - 34);
            }

            var basePts = StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame);
            if (basePts == null || basePts.Count == 0)
            {
                _pbCloud.Image?.Dispose();
                _pbCloud.Image = bmp;
                return;
            }

            double yaw2 = (double)_numYaw.Value * Math.PI / 180.0;
            double pitch2 = (double)_numPitch.Value * Math.PI / 180.0;
            int step = Math.Max(1, (int)Math.Sqrt(basePts.Count / 120000));
            int totalStep = step * step;

            var drawList = new List<(int sx, int sy, byte r, byte g, byte b)>(basePts.Count / totalStep + 100);

            // Create sets or use bounds from currentSeg
            double bThresh = _currentSeg?.BeamZThreshold ?? double.MaxValue;
            double lxInner = _currentSeg?.RefinedLeftBeamInnerX ?? double.NaN;
            double rxInner = _currentSeg?.RefinedRightBeamInnerX ?? double.NaN;

            if (_currentSeg != null && _currentSeg.Success && _currentSeg.BeamRegions.Count >= 2)
            {
                // To determine if a point is in left beam region or right beam region
                // Find the best left and right index roughly
                // But we don't have the best index in SegmentationResult directly.
                // We'll just rely on x < lxInner + width and x > rxInner - width
            }

            // Simplistic approach: just use the separated lists to draw!
            HashSet<System.Numerics.Vector3>? lcol = _currentSeg?.LeftColumnPoints != null ? new HashSet<System.Numerics.Vector3>(_currentSeg.LeftColumnPoints) : null;
            HashSet<System.Numerics.Vector3>? rcol = _currentSeg?.RightColumnPoints != null ? new HashSet<System.Numerics.Vector3>(_currentSeg.RightColumnPoints) : null;
            HashSet<System.Numerics.Vector3>? larm = _currentSeg?.LeftArmPoints != null ? new HashSet<System.Numerics.Vector3>(_currentSeg.LeftArmPoints) : null;
            HashSet<System.Numerics.Vector3>? rarm = _currentSeg?.RightArmPoints != null ? new HashSet<System.Numerics.Vector3>(_currentSeg.RightArmPoints) : null;

            // 实际用于计算的托臂点云（品红色高亮标记）
            HashSet<System.Numerics.Vector3>? usedL = _usedArmPtsL.Count > 0 ? new HashSet<System.Numerics.Vector3>(_usedArmPtsL) : null;
            HashSet<System.Numerics.Vector3>? usedR = _usedArmPtsR.Count > 0 ? new HashSet<System.Numerics.Vector3>(_usedArmPtsR) : null;

            for (int i = 0; i < basePts.Count; i += totalStep)
            {
                var pt = basePts[i];
                if (!ProjectPt(pt.X, -pt.Y, pt.Z, yaw2, pitch2, w, h, out int sx, out int sy))
                    continue;

                byte r = 105, g = 105, b = 105; // Default Gray
                // 计算用点优先级最高（品红色）
                if (usedL != null && usedL.Contains(pt)) { r = 255; g = 0; b = 255; } // Magenta
                else if (usedR != null && usedR.Contains(pt)) { r = 255; g = 0; b = 255; } // Magenta
                else if (lcol != null && lcol.Contains(pt)) { r = 255; g = 165; b = 0; } // Orange
                else if (rcol != null && rcol.Contains(pt)) { r = 255; g = 255; b = 0; } // Yellow
                else if (larm != null && larm.Contains(pt)) { r = 0; g = 255; b = 255; } // Cyan
                else if (rarm != null && rarm.Contains(pt)) { r = 50; g = 205; b = 50; } // LimeGreen

                drawList.Add((sx, sy, r, g, b));
            }

            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = bmpData.Stride;
                int bufSize = stride * h;
                byte[] buffer = new byte[bufSize];

                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, bufSize);

                void SetPixel(byte[] buf, int px, int py, byte c_r, byte c_g, byte c_b)
                {
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) return;
                    int off = py * stride + px * 4;
                    buf[off] = c_b;
                    buf[off + 1] = c_g;
                    buf[off + 2] = c_r;
                    buf[off + 3] = 255;
                }

                int dynamicSz = (int)(Math.Max(1.0, _zoomLevel * 0.8));
                int dotSz = step <= 1 ? dynamicSz : 1;
                int hsz = dotSz / 2;

                foreach (var (sx, sy, c_r, c_g, c_b) in drawList)
                {
                    for (int dy = -hsz; dy <= hsz; dy++)
                        for (int dx = -hsz; dx <= hsz; dx++)
                            SetPixel(buffer, sx + dx, sy + dy, c_r, c_g, c_b);
                }

                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, bufSize);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            _pbCloud.Image?.Dispose();
            _pbCloud.Image = bmp;
        }

        private bool ProjectPt(double x, double y, double z, double yaw, double pitch, int w, int h, out int px, out int py)
        {
            double cx = Math.Cos(yaw), sx = Math.Sin(yaw);
            double cy = Math.Cos(pitch), sy = Math.Sin(pitch);
            double rx = x * cx - z * sx;
            double rz = x * sx + z * cx;
            double ry = y * cy - rz * sy;
            rz = y * sy + rz * cy;

            double scale = 0.5 * _zoomLevel;
            double f = 500;
            double zOffset = 2000;
            double zz = rz + zOffset;
            if (zz < 10) { px = 0; py = 0; return false; }

            px = (int)(w / 2.0 + (rx * f / zz) * scale + _panX);
            py = (int)(h / 2.0 + (ry * f / zz) * scale + _panY);
            return true;
        }

        private void DrawDepthRangeCuboid(Graphics g, double zMin, double zMax, double xMin, double xMax, double yMin, double yMax, double yaw, double pitch, int w, int h)
        {
            // X ROI 为 0 时使用自动检测范围
            double xLo, xHi;
            bool xAuto = Math.Abs(xMin) < 0.5 && Math.Abs(xMax) < 0.5;
            if (xAuto && _currentSeg != null && _currentSeg.XMax > _currentSeg.XMin)
            {
                xLo = _currentSeg.XMin;
                xHi = _currentSeg.XMax;
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

        // ============ 2D 剖面绘制 ============

        private void RedrawProfile()
        {
            var d = _currentSeg;
            if (d == null || !d.Success || d.BinCount == 0)
            {
                _pbProfile.Image?.Dispose();
                _pbProfile.Image = null;
                return;
            }

            int w = Math.Max(200, _pbProfile.ClientSize.Width);
            int h = Math.Max(100, _pbProfile.ClientSize.Height);
            var bmp = new Bitmap(w, h);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(20, 20, 24));
                g.SmoothingMode = SmoothingMode.AntiAlias;

                double xMin = d.XMin, xMax = d.XMax;
                double zMin = d.DepthProfile.Min();
                double zMax = d.DepthProfile.Where(z => z < double.MaxValue * 0.5).Max();
                if (zMax <= zMin) zMax = zMin + 100;

                float Pad = 30f;
                float plotW = w - 2 * Pad, plotH = h - 2 * Pad;
                float MapX(double x) => Pad + (float)((x - xMin) / (xMax - xMin) * plotW);
                float MapZ(double z) => h - Pad - (float)((z - zMin) / (zMax - zMin) * plotH);

                var pts = new List<PointF>();
                for (int i = 0; i < d.BinCount; i++)
                {
                    if (d.DepthProfile[i] < double.MaxValue * 0.5)
                        pts.Add(new PointF(MapX(d.BinXCenters[i]), MapZ(d.DepthProfile[i])));
                }

                if (pts.Count > 1)
                    g.DrawLines(new Pen(Color.FromArgb(100, 150, 255), 1.5f), pts.ToArray());

                // 绘制阈值线
                float thZ = MapZ(d.BeamZThreshold);
                using (var pen = new Pen(Color.Red, 1f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, Pad, thZ, w - Pad, thZ);
                    g.DrawString($"Thresh={d.BeamZThreshold:F0}", new Font("Consolas", 8), Brushes.Red, w - Pad + 5, thZ - 6);
                }

                // 绘制区域
                foreach (var b in d.BeamRegions)
                {
                    float l = MapX(b.leftX), r = MapX(b.rightX);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(60, 255, 165, 0)), l, Pad, r - l, plotH);
                }
            }

            _pbProfile.Image?.Dispose();
            _pbProfile.Image = bmp;
        }

        // ============ 操作与事件 ============

        private void AppendLog(string msg, bool error = false)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendLog(msg, error)));
                return;
            }
            if (_txtLog.TextLength > 10000) _txtLog.Clear();
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {(error ? "❌" : "ℹ")} {msg}\r\n");
            _txtLog.ScrollToCaret();
        }


        private async void BtnGrab_Click(object? sender, EventArgs e)
        {
            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrEmpty(sn)) return;

            var cam = DeviceManager.GetCamera(sn);
            if (cam == null) { AppendLog("未找到指定的相机", true); return; }

            var btn = (Button)sender!;
            btn.Enabled = false;
            try
            {
                AppendLog("正在抓取...");
                var frame = await cam.GrabFrameAsync() as DepthFrameData;
                if (frame != null)
                {
                    _currentFrame = frame;
                    _pbPreview.Image?.Dispose();
                    _pbPreview.Image = (Image)frame.PreviewImage.Clone();
                    AppendLog($"抓取成功. 点云数: {frame.GetPointCloud()?.Count ?? 0}");
                    Recalculate();
                }
            }
            finally { btn.Enabled = true; }
        }

        private void BtnAutoFitRoi_Click(object? sender, EventArgs e)
        {
            if (_currentSeg != null && _currentSeg.Success)
            {
                _numXMinRoi.Value = (decimal)Math.Floor(_currentSeg.XMin) - 50;
                _numXMaxRoi.Value = (decimal)Math.Ceiling(_currentSeg.XMax) + 50;
                AppendLog("已自动适配 X 轴检测边界");
            }
        }

        private void BtnSetRef_Click(object? sender, EventArgs e)
        {
            var d = _currentSeg;
            if (d == null || !d.Success)
            {
                AppendLog("请先采集图像并确保检测成功", true);
                return;
            }

            // 将当前检测的原始值设为标准基准
            _refRackDefLeft  = RackDeformationAlgo.ComputeColumnDeformation(d.LeftColumnPoints);
            _refRackDefRight = RackDeformationAlgo.ComputeColumnDeformation(d.RightColumnPoints);
            _refArmAngleLeft = RackDeformationAlgo.ComputeArmAngle(d.LeftArmPoints);
            _refArmAngleRight = RackDeformationAlgo.ComputeArmAngle(d.RightArmPoints);

            UpdateRefStatusLabel();
            UpdateResultLabel();  // 刷新结果显示（差值将变为 0）
            AppendLog($"✅ 已设为标准值: 立柱L={_refRackDefLeft:F2} R={_refRackDefRight:F2}, 托臂L={_refArmAngleLeft:F2}° R={_refArmAngleRight:F2}°", false);
        }

        private void UpdateRefStatusLabel()
        {
            bool hasRef = (_refRackDefLeft != 0 || _refRackDefRight != 0 || _refArmAngleLeft != 0 || _refArmAngleRight != 0);
            if (hasRef)
            {
                _lblRefStatus.Text = $"标准基准: 立柱 L={_refRackDefLeft:F2} R={_refRackDefRight:F2}\n" +
                                     $"         托臂 L={_refArmAngleLeft:F2}° R={_refArmAngleRight:F2}°";
                _lblRefStatus.ForeColor = Color.FromArgb(100, 255, 100);
            }
            else
            {
                _lblRefStatus.Text = "标准基准: 未设置 (点击上方按钮采集标准值)";
                _lblRefStatus.ForeColor = Color.FromArgb(200, 200, 100);
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var cfg = ConfigManager.Instance?.Algorithms?.RackDeformation;
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
            else if (_currentSeg?.Success == true)
            {
                camParam.XMin = _currentSeg.XMin;
                camParam.XMax = _currentSeg.XMax;
                AppendLog($"X ROI 使用检测范围: [{camParam.XMin:F0}, {camParam.XMax:F0}]", false);
            }

            // Y 范围：优先使用手动设定的值
            if (Math.Abs((double)_numYMinRoi.Value) > 0.5 || Math.Abs((double)_numYMaxRoi.Value) > 0.5)
            {
                camParam.YMin = (double)_numYMinRoi.Value;
                camParam.YMax = (double)_numYMaxRoi.Value;
            }

            // 保存标准基准值
            camParam.RefRackDefLeft  = _refRackDefLeft;
            camParam.RefRackDefRight = _refRackDefRight;
            camParam.RefArmAngleLeft = _refArmAngleLeft;
            camParam.RefArmAngleRight = _refArmAngleRight;

            // 加入列表（新条目）/ 列表中原位置不变
            if (isNew) cfg.CameraRoiParams.Add(camParam);

            ConfigManager.SaveConfig();
            AppendLog($"✅ 已保存相机 [{sn}] 的 ROI 参数: Z=[{zMinVal},{zMaxVal}], X=[{camParam.XMin:F0},{camParam.XMax:F0}], Y=[{camParam.YMin:F0},{camParam.YMax:F0}]", false);
            AppendLog($"   标准基准: 立柱L={_refRackDefLeft:F2} R={_refRackDefRight:F2}, 托臂L={_refArmAngleLeft:F2}° R={_refArmAngleRight:F2}°", false);
        }
    }
}
