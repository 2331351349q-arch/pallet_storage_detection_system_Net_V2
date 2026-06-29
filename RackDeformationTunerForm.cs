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
        /// <summary>3D ROI 立方体（坐标统一使用标定后的基准坐标系）</summary>
        private readonly record struct Roi3D(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

        // ---- 控件 ----
        private ComboBox _cmbSide = null!;
        private ComboBox _cmbTuneCamera = null!;
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
        private DepthFrameData? _currentFrame1;
        private DepthFrameData? _currentFrame2;
        private List<string> _currentSideSNs = new List<string>();
        private SegmentationResult? _currentSeg; // 当前调参相机的分割结果（供剖面图显示）
        private SegmentationResult? _seg1;           // frame1(LeftSideSns[0]) 的分割结果
        private SegmentationResult? _seg2;           // frame2(LeftSideSns[1]) 的分割结果
        private bool _isDragging;
        private Point _lastMouse;
        private double _zoomLevel = 1.0;
        private double _panX, _panY;               // 3D 视图平移偏移 (pixels)
        private Label _lblCalibStatus = null!;      // 标定状态指示

        // ---- 调节目标 (立柱 ROI 或 横梁 ROI) ----
        private ComboBox _cmbRoiTarget = null!;
        private Roi3D _colRoi;
        private Roi3D _beamRoi;

        // ---- 标准基准值（从配置加载 / 由"设为标准值"按钮写入）----
        private double _refRackDefLeft;
        private double _refRackDefRight;
        private double _refBeamDefLeft;
        private double _refBeamDefRight;

        private List<System.Numerics.Vector3> _usedBeamPts = new();
        private System.Collections.Generic.List<System.Numerics.Vector3> _usedArmPtsL = new();
        private System.Collections.Generic.List<System.Numerics.Vector3> _usedArmPtsR = new();
        private Label _lblRefStatus = null!;        // 标准值状态指示

        // ---- SlotOccupancy ROI（已转换到标定基准坐标系，供3D可视化）----
        private Roi3D _slotOccupancyRoi;

        // ---- RackDeformation ROI（标定基准坐标系，当前调节值）----
        private Roi3D _rackDeformationRoi;

        public RackDeformationTunerForm()
        {
            InitializeUi();
            LoadConfigValues();
            FormClosed += OnFormClosed;
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            // 停止并断开通过本窗口初始化的所有相机
            foreach (var sn in _currentSideSNs)
            {
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
                        catch { /* 忽略异常 */ }
                    }
                }
            }
            
            _currentFrame1?.PreviewImage?.Dispose();
            _currentFrame2?.PreviewImage?.Dispose();

            // 释放图像资源
            _pbCloud?.Image?.Dispose();
            _pbProfile?.Image?.Dispose();
            _pbPreview?.Image?.Dispose();
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
            _cmbSide.SelectedIndexChanged += (_, __) => { UpdateCameraSnFromConfig(); LoadRoiForCurrentCamera(); };
            flow.Controls.Add(_cmbSide);

            // 调参相机选择
            flow.Controls.Add(BoldLabel("当前调参相机 SN"));
            _cmbTuneCamera = new ComboBox { Width = FlowW, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTuneCamera.SelectedIndexChanged += (_, __) =>
            {
                LoadRoiForCurrentCamera();
                UpdatePreviewImage();
                Recalculate();
            };
            flow.Controls.Add(_cmbTuneCamera);

            // 调节目标选择
            flow.Controls.Add(BoldLabel("当前调节 ROI"));
            _cmbRoiTarget = new ComboBox { Width = FlowW, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbRoiTarget.Items.AddRange(new object[] { "立柱 ROI (Column)", "横梁 ROI (Beam)" });
            _cmbRoiTarget.SelectedIndex = 0;
            _cmbRoiTarget.SelectedIndexChanged += (_, __) =>
            {
                LoadRoiForCurrentCamera();
                Recalculate();
            };
            flow.Controls.Add(_cmbRoiTarget);

            // 按鈕行
            var btnLine = new FlowLayoutPanel { Width = FlowW, Height = 44, WrapContents = false };
            var btnInit = new Button { Text = "🔌 初始化", Width = 120, Height = 40, Margin = new Padding(3, 2, 3, 2), BackColor = Color.FromArgb(80, 80, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnInit.Click += BtnInit_Click;
            var btnGrab = new Button { Text = "📸 采集", Width = 120, Height = 40, Margin = new Padding(3, 2, 3, 2), BackColor = Color.FromArgb(40, 140, 90), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGrab.Click += BtnGrab_Click;
            var btnSave = new Button { Text = "💾 保存", Width = 120, Height = 40, Margin = new Padding(3, 2, 3, 2), BackColor = Color.FromArgb(60, 80, 130), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSave.Click += BtnSave_Click;
            btnLine.Controls.Add(btnInit);
            btnLine.Controls.Add(btnGrab);
            btnLine.Controls.Add(btnSave);
            flow.Controls.Add(btnLine);

            // 标定状态
            _lblCalibStatus = new Label { Width = FlowW, Height = 22, Font = new Font("Consolas", 8F), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Text = "📷 标定: — 未指定相机" };
            flow.Controls.Add(_lblCalibStatus);

            // 自动适配ROI按鈕
            var btnAutoFitRoi = new Button { Text = "🎯 自动适配 ROI", Width = FlowW, Height = 30, BackColor = Color.FromArgb(40, 160, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnAutoFitRoi.Click += BtnAutoFitRoi_Click;
            flow.Controls.Add(btnAutoFitRoi);

            // 设为标准值按钮
            var btnSetRef = new Button { Text = "📐 设为标准值", Width = FlowW, Height = 30, BackColor = Color.FromArgb(180, 120, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold) };
            btnSetRef.Click += BtnCaptureRef_Click;
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

        private void LoadConfigValues()
        {
            UpdateCameraSnFromConfig();
        }

        /// <summary>仅加载当前相机 SN 对应的独立 ROI 参数（不触发完整 LoadConfigValues）。</summary>
        private void LoadRoiForCurrentCamera()
        {
            string sn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            var cfg = ConfigManager.Instance?.Algorithms?.RackDeformation;

            // --- 1. 加载 RackDeformation 滑块参数 ---
            var camParam = cfg?.FindCameraParam(sn);
            
            if (camParam != null)
            {
                bool isBeam = _cmbRoiTarget.SelectedIndex == 1;
                double xMin = isBeam ? camParam.BeamXMin : camParam.ColXMin;
                double xMax = isBeam ? camParam.BeamXMax : camParam.ColXMax;
                double yMin = isBeam ? camParam.BeamYMin : camParam.ColYMin;
                double yMax = isBeam ? camParam.BeamYMax : camParam.ColYMax;
                int zMin = isBeam ? camParam.BeamZMin : camParam.ColZMin;
                int zMax = isBeam ? camParam.BeamZMax : camParam.ColZMax;

                // 若未配置过对应的双 ROI 范围，Fallback 回退到原单 ROI 配置值
                if (xMax <= xMin)
                {
                    xMin = camParam.XMin;
                    xMax = camParam.XMax;
                    yMin = camParam.YMin;
                    yMax = camParam.YMax;
                    zMin = camParam.ZMin;
                    zMax = camParam.ZMax;
                }

                _numDepthMin.Value = Math.Clamp(zMin, (int)_numDepthMin.Minimum, (int)_numDepthMin.Maximum);
                _numDepthMax.Value = Math.Clamp(zMax, (int)_numDepthMax.Minimum, (int)_numDepthMax.Maximum);
                if (xMax > xMin)
                {
                    _numXMinRoi.Value = Math.Clamp((decimal)xMin, _numXMinRoi.Minimum, _numXMinRoi.Maximum);
                    _numXMaxRoi.Value = Math.Clamp((decimal)xMax, _numXMaxRoi.Minimum, _numXMaxRoi.Maximum);
                }
                else { _numXMinRoi.Value = 0; _numXMaxRoi.Value = 0; }
                if (yMax > yMin)
                {
                    _numYMinRoi.Value = Math.Clamp((decimal)yMin, _numYMinRoi.Minimum, _numYMinRoi.Maximum);
                    _numYMaxRoi.Value = Math.Clamp((decimal)yMax, _numYMaxRoi.Minimum, _numYMaxRoi.Maximum);
                }
                else { _numYMinRoi.Value = -800; _numYMaxRoi.Value = 800; }

                // 加载当前侧的立柱与横梁标准值：左相机对应左立柱，右相机对应右立柱
                var leftCamSn = _currentSideSNs.Count > 0 ? _currentSideSNs[0] : null;
                var rightCamSn = _currentSideSNs.Count > 1 ? _currentSideSNs[1] : null;
                var leftParam = cfg?.FindCameraParam(leftCamSn);
                var rightParam = cfg?.FindCameraParam(rightCamSn);

                _refRackDefLeft  = leftParam?.RefRackDefLeft ?? 0.0;
                _refRackDefRight = rightParam?.RefRackDefRight ?? 0.0;
                _refBeamDefLeft  = leftParam?.RefBeamDef ?? 0.0;
                _refBeamDefRight = rightParam?.RefBeamDef ?? 0.0;

                UpdateRefStatusLabel();

                AppendLog($"切换到相机 [{sn}]", false);
            }

            // --- 2. 加载 SlotOccupancy ROI 并转换到标定基准坐标系 ---
            _slotOccupancyRoi = LoadSlotOccupancyRoi(sn);

            UpdateCalibStatus();
        }

        /// <summary>
        /// 从 SlotOccupancy 配置加载当前相机的 ROI，并转换到标定基准坐标系。
        /// 若无标定，ROI 保持原始相机坐标系下的值。
        /// </summary>
        private Roi3D LoadSlotOccupancyRoi(string sn)
        {
            var slotCfg = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
            string side = CurrentSide();

            // 尝试获取该相机独立的 ROI
            var camRoi = slotCfg?.FindCameraParam(sn);
            double xMin, xMax, yMin, yMax, zMin, zMax;

            if (camRoi != null)
            {
                xMin = camRoi.XMin; xMax = camRoi.XMax;
                yMin = camRoi.YMin; yMax = camRoi.YMax;
                zMin = camRoi.ZMin; zMax = camRoi.ZMax;
            }
            else
            {
                // 回退到 side 全局 ROI 或默认值
                var roiList = side == "right" ? slotCfg?.Roi3dRight : slotCfg?.Roi3dLeft;
                if (roiList != null && roiList.Count >= 6)
                {
                    xMin = roiList[0]; xMax = roiList[1];
                    yMin = roiList[2]; yMax = roiList[3];
                    zMin = roiList[4]; zMax = roiList[5];
                }
                else
                {
                    xMin = -500; xMax = 500; yMin = -500; yMax = 500; zMin = 1000; zMax = 3000;
                }
            }

            // 若标定有效，将 ROI 的 8 个角点从相机坐标系转换到基准坐标系，再取 AABB
            var calib = ConfigManager.GetCalibration(sn);
            if (calib != null && calib.IsValid)
            {
                var corners = new[]
                {
                    new System.Numerics.Vector3((float)xMin, (float)yMin, (float)zMin),
                    new System.Numerics.Vector3((float)xMax, (float)yMin, (float)zMin),
                    new System.Numerics.Vector3((float)xMin, (float)yMax, (float)zMin),
                    new System.Numerics.Vector3((float)xMax, (float)yMax, (float)zMin),
                    new System.Numerics.Vector3((float)xMin, (float)yMin, (float)zMax),
                    new System.Numerics.Vector3((float)xMax, (float)yMin, (float)zMax),
                    new System.Numerics.Vector3((float)xMin, (float)yMax, (float)zMax),
                    new System.Numerics.Vector3((float)xMax, (float)yMax, (float)zMax),
                };

                var transformed = corners.Select(c => CalibrationAlgo.TransformPoint(c, calib)).ToArray();
                xMin = transformed.Min(p => (double)p.X);
                xMax = transformed.Max(p => (double)p.X);
                yMin = transformed.Min(p => (double)p.Y);
                yMax = transformed.Max(p => (double)p.Y);
                zMin = transformed.Min(p => (double)p.Z);
                zMax = transformed.Max(p => (double)p.Z);
            }

            return new Roi3D(xMin, xMax, yMin, yMax, zMin, zMax);
        }

        private string CurrentSide() => _cmbSide.SelectedItem?.ToString() ?? "left";

        private void UpdateCalibStatus()
        {
            string? sn = _cmbTuneCamera.SelectedItem?.ToString();
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

        private void UpdateCameraSnFromConfig()
        {
            var mapping = ConfigManager.Instance?.Algorithms?.RackDeformation?.CameraMapping;
            if (mapping == null) return;
            
            _currentSideSNs = _cmbSide.SelectedItem?.ToString() == "right"
                ? (mapping.RightSideSns ?? new List<string>())
                : (mapping.LeftSideSns ?? new List<string>());

            _cmbTuneCamera.Items.Clear();
            foreach (var sn in _currentSideSNs)
            {
                if (!string.IsNullOrWhiteSpace(sn))
                    _cmbTuneCamera.Items.Add(sn);
            }
            if (_cmbTuneCamera.Items.Count > 0)
                _cmbTuneCamera.SelectedIndex = 0;
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
            double sliderZMin = (double)_numDepthMin.Value;
            double sliderZMax = (double)_numDepthMax.Value;
            double xMinNum    = (double)_numXMinRoi.Value;
            double xMaxNum    = (double)_numXMaxRoi.Value;
            double yMinNum    = (double)_numYMinRoi.Value;
            double yMaxNum    = (double)_numYMaxRoi.Value;

            string tuneSn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            var cfg = ConfigManager.Instance?.Algorithms?.RackDeformation;

            bool isBeam = _cmbRoiTarget.SelectedIndex == 1;

            // 1. 临时在内存中更新配置参数，供算法直接提取
            var camParam = cfg?.FindCameraParam(tuneSn);
            if (camParam == null && cfg != null && !string.IsNullOrEmpty(tuneSn))
            {
                camParam = new CameraRoiParam { CameraSn = tuneSn };
                cfg.CameraRoiParams.Add(camParam);
            }

            if (camParam != null)
            {
                if (isBeam)
                {
                    camParam.BeamXMin = xMinNum;
                    camParam.BeamXMax = xMaxNum;
                    camParam.BeamYMin = yMinNum;
                    camParam.BeamYMax = yMaxNum;
                    camParam.BeamZMin = (int)sliderZMin;
                    camParam.BeamZMax = (int)sliderZMax;
                }
                else
                {
                    camParam.ColXMin = xMinNum;
                    camParam.ColXMax = xMaxNum;
                    camParam.ColYMin = yMinNum;
                    camParam.ColYMax = yMaxNum;
                    camParam.ColZMin = (int)sliderZMin;
                    camParam.ColZMax = (int)sliderZMax;
                }
            }

            // 2. 调用算法层面的单相机分割，实现无缝的调参计算
            _seg1 = RackDeformationAlgo.SegmentSingleCamera(_currentFrame1, cfg);
            _seg2 = RackDeformationAlgo.SegmentSingleCamera(_currentFrame2, cfg);

            _currentSeg = (_currentFrame1?.CameraSn == tuneSn) ? _seg1 :
                          (_currentFrame2?.CameraSn == tuneSn) ? _seg2 :
                          (_seg1 ?? _seg2);

            var activeSeg = _currentSeg ?? _seg1 ?? _seg2;
            _lblDetectedXRange.Text = (activeSeg?.Success == true)
                ? $"检测范围: X=[{activeSeg.XMin:F0}, {activeSeg.XMax:F0}] mm"
                : "检测范围: --";
            _lblDetectedYRange.Text = $"ROI Y=[{yMinNum:F0}, {yMaxNum:F0}] mm";

            // 3. 计算 3D 视图中展示的立柱 ROI 和横梁 ROI 立方体
            if (isBeam)
            {
                _beamRoi = new Roi3D(xMinNum, xMaxNum, yMinNum, yMaxNum, sliderZMin, sliderZMax);
                
                double cxMin = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColXMin : -200;
                double cxMax = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColXMax : 200;
                double cyMin = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColYMin : -800;
                double cyMax = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColYMax : 800;
                double czMin = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColZMin : 1000;
                double czMax = (camParam != null && camParam.ColXMax > camParam.ColXMin) ? camParam.ColZMax : 3000;
                _colRoi = new Roi3D(cxMin, cxMax, cyMin, cyMax, czMin, czMax);
            }
            else
            {
                _colRoi = new Roi3D(xMinNum, xMaxNum, yMinNum, yMaxNum, sliderZMin, sliderZMax);

                double bxMin = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamXMin : -1000;
                double bxMax = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamXMax : 1000;
                double byMin = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamYMin : -800;
                double byMax = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamYMax : 800;
                double bzMin = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamZMin : 1000;
                double bzMax = (camParam != null && camParam.BeamXMax > camParam.BeamXMin) ? camParam.BeamZMax : 3000;
                _beamRoi = new Roi3D(bxMin, bxMax, byMin, byMax, bzMin, bzMax);
            }

            _rackDeformationRoi = isBeam ? _beamRoi : _colRoi;

            UpdateResultLabel();
            RedrawCloud();
            RedrawProfile();
        }

        private SegmentationResult? SegmentFrameForTuner(
            DepthFrameData? frame, string tuneSn,
            double sliderZMin, double sliderZMax,
            double sliderYMin, double sliderYMax,
            double? sliderXMin, double? sliderXMax,
            RackDeformationConfig? cfg)
        {
            if (frame == null) return null;

            var pts = StackerOffsetAlgo.GetBasePointsFromFrame(frame);
            if (pts == null || pts.Count == 0) return null;

            double zMin, zMax, yLo, yHi;
            double? xMinRoI, xMaxRoI;

            if (string.Equals(frame.CameraSn, tuneSn, StringComparison.OrdinalIgnoreCase))
            {
                zMin = sliderZMin; zMax = sliderZMax;
                yLo  = sliderYMin; yHi  = sliderYMax;
                xMinRoI = sliderXMin; xMaxRoI = sliderXMax;
            }
            else
            {
                var camParam = cfg?.FindCameraParam(frame.CameraSn);
                zMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
                zMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
                xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : (double?)null;
                xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : (double?)null;
                yLo = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : -800.0;
                yHi = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax :  800.0;
            }

            var seg = CloudSegmentationHelper.Segment(
                pts, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                5.0, 3, 500, extractComponentClouds: true);

            return seg.Success ? seg : null;
        }

        private void UpdateResultLabel()
        {
            bool seg1Ok = _seg1 != null;
            bool seg2Ok = _seg2 != null;

            if (!seg1Ok && !seg2Ok)
            {
                _lblResult.Text = "无数据";
                _lblResult.ForeColor = Color.OrangeRed;
                return;
            }

            // 立柱：seg1(frame1/左侧相机)→左立柱，seg2(frame2/右侧相机)→右立柱
            double rackL = RackDeformationAlgo.ComputeColumnDeformation(_seg1?.LeftColumnPoints);
            double rackR = RackDeformationAlgo.ComputeColumnDeformation(_seg2?.RightColumnPoints);

            // 横梁：左相机计算左半，右相机计算右半，再进行整合
            double beamL = RackDeformationAlgo.ComputeBeamDeformation(_seg1?.BeamPoints);
            double beamR = RackDeformationAlgo.ComputeBeamDeformation(_seg2?.BeamPoints);

            double diffRackL = rackL - _refRackDefLeft;
            double diffRackR = rackR - _refRackDefRight;

            double diffBeamL = _seg1 != null ? (beamL - _refBeamDefLeft) : 0.0;
            double diffBeamR = _seg2 != null ? (beamR - _refBeamDefRight) : 0.0;
            double diffBeam = (seg1Ok && seg2Ok) ? Math.Max(diffBeamL, diffBeamR) :
                              (seg1Ok) ? diffBeamL :
                              (seg2Ok) ? diffBeamR : 0.0;

            double beam = (seg1Ok && seg2Ok) ? (diffBeam == diffBeamL ? beamL : beamR) :
                          (seg1Ok) ? beamL :
                          (seg2Ok) ? beamR : 0.0;

            double refBeam = (seg1Ok && seg2Ok) ? (diffBeam == diffBeamL ? _refBeamDefLeft : _refBeamDefRight) :
                             (seg1Ok) ? _refBeamDefLeft :
                             (seg2Ok) ? _refBeamDefRight : 0.0;

            bool hasRef = (_refRackDefLeft != 0 || _refRackDefRight != 0 || _refBeamDefLeft != 0 || _refBeamDefRight != 0);
            string refTag = hasRef ? "" : " (无标准值)";

            var activeSeg = _currentSeg ?? _seg1 ?? _seg2;
            string yInfo = activeSeg != null && !double.IsNaN(activeSeg.YBeamCenterY)
                ? $"\n│ 横梁Y中心:{activeSeg.YBeamCenterY,7:F1} mm  (±{activeSeg.YBeamHalfHeight:F0})"
                : "";

            int beamXSpan1 = _seg1?.BeamPoints?.Count > 0
                ? (int)(_seg1.BeamPoints.Max(p => (double)p.X) - _seg1.BeamPoints.Min(p => (double)p.X))
                : 0;
            int beamXSpan2 = _seg2?.BeamPoints?.Count > 0
                ? (int)(_seg2.BeamPoints.Max(p => (double)p.X) - _seg2.BeamPoints.Min(p => (double)p.X))
                : 0;

            _lblResult.Text =
                $"┌─ 弯曲度检测结果{refTag} ──────────\n" +
                $"│ 分割状态: Cam1:{(seg1Ok ? "✓" : "✗")}  Cam2:{(seg2Ok ? "✓" : "✗")}\n" +
                $"│ 【左立柱】当前:{rackL,7:F2} mm  标准:{_refRackDefLeft,7:F2}  差值:{diffRackL,+7:F2}\n" +
                $"│ 【右立柱】当前:{rackR,7:F2} mm  标准:{_refRackDefRight,7:F2}  差值:{diffRackR,+7:F2}\n" +
                $"│\n" +
                $"│ 【横  梁】当前:{beam,7:F2} mm  标准:{refBeam,7:F2}  差值:{diffBeam,+7:F2}{yInfo}\n" +
                $"│\n" +
                $"│ 立柱点云 (L/R): {_seg1?.LeftColumnPoints?.Count ?? 0} / {_seg2?.RightColumnPoints?.Count ?? 0}\n" +
                $"│ 横梁点云 (L/R): {(_seg1?.BeamPoints?.Count ?? 0)} / {(_seg2?.BeamPoints?.Count ?? 0)}\n" +
                $"│ 横梁 X 跨度(L/R): {beamXSpan1} / {beamXSpan2} mm\n" +
                $"└────────────────────────────";
            _lblResult.ForeColor = Color.White;
        }

        private DepthFrameData? GetSelectedFrame()
        {
            var sn = _cmbTuneCamera.SelectedItem?.ToString();
            if (_currentFrame1 != null && _currentFrame1.CameraSn == sn) return _currentFrame1;
            if (_currentFrame2 != null && _currentFrame2.CameraSn == sn) return _currentFrame2;
            return _currentFrame1 ?? _currentFrame2;
        }

        // ============ 3D 点云绘制 ============

        private void RedrawCloud()
        {
            string tuneSn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            bool showCam1 = string.IsNullOrEmpty(tuneSn) || (_currentFrame1?.CameraSn == tuneSn);
            bool showCam2 = string.IsNullOrEmpty(tuneSn) || (_currentFrame2?.CameraSn == tuneSn);

            // 分相机渲染：只展示当前调参相机的点云
            var pts1 = showCam1 ? StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame1) : null;
            var pts2 = showCam2 ? StackerOffsetAlgo.GetBasePointsFromFrame(_currentFrame2) : null;

            int totalCount = (pts1?.Count ?? 0) + (pts2?.Count ?? 0);
            if (totalCount == 0) return;

            int w = Math.Max(320, _pbCloud.ClientSize.Width);
            int h = Math.Max(240, _pbCloud.ClientSize.Height);
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(16, 16, 18));
                double yaw   = (double)_numYaw.Value   * Math.PI / 180.0;
                double pitch = (double)_numPitch.Value * Math.PI / 180.0;

                bool isBeam = _cmbRoiTarget.SelectedIndex == 1;

                if (isBeam)
                {
                    // --- 绘制横梁 ROI 立方体 (青色) ---
                    using var beamPen = new Pen(Color.Cyan, 2.2f) { DashStyle = DashStyle.Solid };
                    DrawRoiCube2D(g, beamPen, _beamRoi, yaw, pitch, w, h);
                }
                else
                {
                    // --- 绘制立柱 ROI 立方体 (橙红色) ---
                    using var colPen = new Pen(Color.OrangeRed, 2.2f) { DashStyle = DashStyle.Solid };
                    DrawRoiCube2D(g, colPen, _colRoi, yaw, pitch, w, h);
                }

                // --- 绘制坐标系指示 ---
                DrawCoordinateAxes(g, isBeam ? _beamRoi : _colRoi, yaw, pitch, w, h);

                using var infoFont = new Font("Consolas", 9F, FontStyle.Bold);
                using var hintFont = new Font("Microsoft YaHei UI", 7F);

                // 标定状态（与 RoiTunerForm 一致的风格）
                string? tuneSn2 = _cmbTuneCamera.SelectedItem?.ToString();
                var calib = string.IsNullOrWhiteSpace(tuneSn2) ? null : ConfigManager.GetCalibration(tuneSn2);
                bool hasCalib = calib != null && calib.IsValid;
                string calibText = hasCalib
                    ? $"✓ 标定已应用 (R|T) | SN={tuneSn2}  点云={totalCount}"
                    : $"⚠ 无标定 — SN={tuneSn2 ?? "?"}  显示相机原始坐标  点云={totalCount}";
                Color calibColor = hasCalib ? Color.Lime : Color.OrangeRed;
                g.DrawString(calibText, infoFont, new SolidBrush(calibColor), 10, h - 52);
                g.DrawString($"缩放 ×{_zoomLevel:F1}", infoFont, Brushes.Gray, 10, h - 34);
                g.DrawString("左键旋转 | Shift+拖/中键平移 | 滚轮缩放 | 右键复位", hintFont, Brushes.DimGray, 10, h - 18);
            }

            double yaw2   = (double)_numYaw.Value   * Math.PI / 180.0;
            double pitch2 = (double)_numPitch.Value * Math.PI / 180.0;
            int step = Math.Max(1, (int)Math.Sqrt(totalCount / 120000));
            int totalStep = step * step;

            var drawList = new List<(int sx, int sy, byte r, byte g, byte b)>(totalCount / totalStep + 100);

            // --- frame1 点云（根据是否在 ROI 内部进行双色高亮，其余点变灰）---
            if (pts1 != null)
            {
                for (int i = 0; i < pts1.Count; i += totalStep)
                {
                    var pt = pts1[i];
                    if (!ProjectPt(pt.X, -pt.Y, pt.Z, yaw2, pitch2, w, h, out int sx, out int sy)) continue;

                    byte r = 70, g = 70, b = 75; // 默认暗灰色
                    if (pt.X >= _colRoi.MinX && pt.X <= _colRoi.MaxX &&
                        pt.Y >= _colRoi.MinY && pt.Y <= _colRoi.MaxY &&
                        pt.Z >= _colRoi.MinZ && pt.Z <= _colRoi.MaxZ)
                    {
                        r = 255; g = 140; b = 0; // 亮橙色 (立柱)
                    }
                    else if (pt.X >= _beamRoi.MinX && pt.X <= _beamRoi.MaxX &&
                             pt.Y >= _beamRoi.MinY && pt.Y <= _beamRoi.MaxY &&
                             pt.Z >= _beamRoi.MinZ && pt.Z <= _beamRoi.MaxZ)
                    {
                        r = 0; g = 255; b = 255; // 亮青色 (横梁)
                    }
                    drawList.Add((sx, sy, r, g, b));
                }
            }

            // --- frame2 点云（根据是否在 ROI 内部进行双色高亮，其余点变灰）---
            if (pts2 != null)
            {
                for (int i = 0; i < pts2.Count; i += totalStep)
                {
                    var pt = pts2[i];
                    if (!ProjectPt(pt.X, -pt.Y, pt.Z, yaw2, pitch2, w, h, out int sx, out int sy)) continue;

                    byte r = 70, g = 70, b = 75; // 默认暗灰色
                    if (pt.X >= _colRoi.MinX && pt.X <= _colRoi.MaxX &&
                        pt.Y >= _colRoi.MinY && pt.Y <= _colRoi.MaxY &&
                        pt.Z >= _colRoi.MinZ && pt.Z <= _colRoi.MaxZ)
                    {
                        r = 255; g = 140; b = 0; // 亮橙色 (立柱)
                    }
                    else if (pt.X >= _beamRoi.MinX && pt.X <= _beamRoi.MaxX &&
                             pt.Y >= _beamRoi.MinY && pt.Y <= _beamRoi.MaxY &&
                             pt.Z >= _beamRoi.MinZ && pt.Z <= _beamRoi.MaxZ)
                    {
                        r = 0; g = 255; b = 255; // 亮青色 (横梁)
                    }
                    drawList.Add((sx, sy, r, g, b));
                }
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
                    buf[off] = c_b; buf[off + 1] = c_g; buf[off + 2] = c_r; buf[off + 3] = 255;
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
            finally { bmp.UnlockBits(bmpData); }

            _pbCloud.Image?.Dispose();
            _pbCloud.Image = bmp;
        }

        /// <summary>
        /// 3D 点投影到 2D 屏幕坐标（与 RoiTunerForm 一致）。
        /// 旋转顺序：先绕 Y 轴(Yaw)再绕 X 轴(Pitch)，透视投影。
        /// </summary>
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

        /// <summary>
        /// 绘制 3D ROI 立方体框（与 RoiTunerForm 一致的风格），
        /// 使用 SlotOccupancy 配置中的 ROI 参数（已转换到标定基准坐标系）。
        /// </summary>
        private void DrawRoiCube2D(Graphics g, Pen pen, Roi3D roi, double yaw, double pitch, int w, int h)
        {
            var corners = new[]
            {
                (roi.MinX, -roi.MinY, roi.MinZ),
                (roi.MaxX, -roi.MinY, roi.MinZ),
                (roi.MinX, -roi.MaxY, roi.MinZ),
                (roi.MaxX, -roi.MaxY, roi.MinZ),
                (roi.MinX, -roi.MinY, roi.MaxZ),
                (roi.MaxX, -roi.MinY, roi.MaxZ),
                (roi.MinX, -roi.MaxY, roi.MaxZ),
                (roi.MaxX, -roi.MaxY, roi.MaxZ),
            };

            var pts = corners.Select(c =>
            {
                bool ok = ProjectPt(c.Item1, c.Item2, c.Item3, yaw, pitch, w, h, out var sx, out var sy);
                return (ok, p: new PointF(sx, sy));
            }).ToArray();

            var edges = new (int, int)[]
            {
                (0,1),(1,3),(3,2),(2,0),
                (4,5),(5,7),(7,6),(6,4),
                (0,4),(1,5),(2,6),(3,7)
            };

            foreach (var (a, b) in edges)
            {
                if (pts[a].ok && pts[b].ok)
                {
                    g.DrawLine(pen, pts[a].p, pts[b].p);
                }
            }
        }

        /// <summary>
        /// 绘制基准坐标轴 (X红, Y绿, Z蓝)，原点取 ROI 区域的一个角点附近。
        /// </summary>
        private void DrawCoordinateAxes(Graphics g, Roi3D roi, double yaw, double pitch, int w, int h)
        {
            float ox = (float)roi.MinX, oy = (float)roi.MaxY, oz = (float)roi.MinZ;
            ProjectPt(ox, -oy, oz, yaw, pitch, w, h, out int oxS, out int oyS);

            // X 轴（红）
            ProjectPt(ox + 300, -oy, oz, yaw, pitch, w, h, out int xS, out int xSy);
            using (var pX = new Pen(Color.Red, 2f)) g.DrawLine(pX, oxS, oyS, xS, xSy);
            using (var f = new Font("Consolas", 9F))
                g.DrawString("X", f, Brushes.Red, xS, xSy);

            // Y 轴（绿）
            ProjectPt(ox, -(oy - 300), oz, yaw, pitch, w, h, out int yS, out int ySy);
            using (var pY = new Pen(Color.Lime, 2f)) g.DrawLine(pY, oxS, oyS, yS, ySy);
            using (var f = new Font("Consolas", 9F))
                g.DrawString("Y", f, Brushes.Lime, yS, ySy);

            // Z 轴（蓝）
            ProjectPt(ox, -oy, oz + 500, yaw, pitch, w, h, out int zS, out int zSy);
            using (var pZ = new Pen(Color.DodgerBlue, 2f)) g.DrawLine(pZ, oxS, oyS, zS, zSy);
            using (var f = new Font("Consolas", 9F))
                g.DrawString("Z(深度)", f, Brushes.DodgerBlue, zS, zSy);
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

                float thZ = MapZ(d.BeamZThreshold);
                using (var pen = new Pen(Color.Red, 1f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, Pad, thZ, w - Pad, thZ);
                    g.DrawString($"Thresh={d.BeamZThreshold:F0}", new Font("Consolas", 8), Brushes.Red, w - Pad + 5, thZ - 6);
                }

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

        private async void BtnInit_Click(object? sender, EventArgs e)
        {
            if (_currentSideSNs.Count == 0) { AppendLog("当前侧无配置相机SN", true); return; }

            var configsToInit = new List<CameraConfig>();
            foreach (var sn in _currentSideSNs)
            {
                if (DeviceManager.GetCamera(sn) != null) continue;
                var cfg = ConfigManager.Instance?.Cameras?.FirstOrDefault(c =>
                    string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase));
                if (cfg != null) configsToInit.Add(cfg);
            }

            if (configsToInit.Count == 0) { AppendLog("所有相关相机均已初始化", false); return; }

            await Task.Run(() => DeviceManager.Initialize(configsToInit, m => BeginInvoke(new Action(() => AppendLog(m, false)))));

            foreach (var cfg in configsToInit)
            {
                if (DeviceManager.GetCamera(cfg.Sn) != null)
                {
                    var calib = ConfigManager.GetCalibration(cfg.Sn);
                    bool hasCalib = calib != null && calib.IsValid;
                    AppendLog($"相机初始化成功: {cfg.Sn} | 外参标定: {(hasCalib ? "✓" : "✗")}", false);
                }
                else
                {
                    AppendLog($"相机初始化失败: {cfg.Sn}", true);
                }
            }
        }

        private async void BtnGrab_Click(object? sender, EventArgs e)
        {
            if (_currentSideSNs.Count == 0) { AppendLog("当前侧无相机配置", true); return; }

            var btn = (Button)sender!;
            btn.Enabled = false;
            try
            {
                AppendLog("正在抓取...");
                var tasks = new List<Task<object>>();
                foreach (var sn in _currentSideSNs)
                {
                    var cam = DeviceManager.GetCamera(sn);
                    if (cam == null) { AppendLog($"相机 {sn} 未初始化", true); continue; }
                    if (!cam.IsConnected) { AppendLog($"相机 {sn} 未连接", true); continue; }
                    tasks.Add(cam.GrabFrameAsync()!);
                }

                if (tasks.Count == 0) return;

                var results = await Task.WhenAll(tasks);
                
                _currentFrame1 = results.Length > 0 ? results[0] as DepthFrameData : null;
                _currentFrame2 = results.Length > 1 ? results[1] as DepthFrameData : null;

                if (_currentFrame1 != null) {
                    AppendLog($"抓图成功 (1) {_currentFrame1.CameraSn}", false);
                }
                if (_currentFrame2 != null) {
                    AppendLog($"抓图成功 (2) {_currentFrame2.CameraSn}", false);
                }

                UpdatePreviewImage();

                Recalculate();
            }
            catch (Exception ex) { AppendLog($"抓图失败: {ex.Message}", true); }
            finally { btn.Enabled = true; }
        }

        private void UpdatePreviewImage()
        {
            var targetFrame = GetSelectedFrame();
            if (targetFrame != null)
            {
                _pbPreview.Image?.Dispose();
                _pbPreview.Image = (Image)targetFrame.PreviewImage.Clone();
            }
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

        private void BtnCaptureRef_Click(object? sender, EventArgs e)
        {
            if (_seg1 == null && _seg2 == null)
            {
                AppendLog("请先抓取一帧并确保至少一台相机分割成功", true);
                return;
            }

            // 立柱：seg1→左，seg2→右
            _refRackDefLeft  = RackDeformationAlgo.ComputeColumnDeformation(_seg1?.LeftColumnPoints);
            _refRackDefRight = RackDeformationAlgo.ComputeColumnDeformation(_seg2?.RightColumnPoints);

            // 横梁：左相机计算左半，右相机计算右半
            _refBeamDefLeft  = RackDeformationAlgo.ComputeBeamDeformation(_seg1?.BeamPoints);
            _refBeamDefRight = RackDeformationAlgo.ComputeBeamDeformation(_seg2?.BeamPoints);

            UpdateRefStatusLabel();
            AppendLog($"✅ 已设为标准值: 立柱L={_refRackDefLeft:F2}mm R={_refRackDefRight:F2}mm, 横梁L={_refBeamDefLeft:F2}mm R={_refBeamDefRight:F2}mm", false);
        }

        private void UpdateRefStatusLabel()
        {
            bool hasRef = (_refRackDefLeft != 0 || _refRackDefRight != 0 || _refBeamDefLeft != 0 || _refBeamDefRight != 0);
            if (hasRef)
            {
                _lblRefStatus.Text = $"标准基准: 立柱 L={_refRackDefLeft:F2} R={_refRackDefRight:F2}\n" +
                                     $"         横梁 L={_refBeamDefLeft:F2} R={_refBeamDefRight:F2} mm";
                _lblRefStatus.ForeColor = Color.FromArgb(100, 255, 100);
            }
            else
            {
                _lblRefStatus.Text = "标准基准: 未设置 (点击上方按钮采集标准值)";
                _lblRefStatus.ForeColor = Color.Gray;
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var cfg = ConfigManager.Instance?.Algorithms?.RackDeformation;
            if (cfg == null) { AppendLog("配置对象不可用", true); return; }

            string sn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sn)) { AppendLog("请先选择调参相机SN", true); return; }

            int zMinVal = (int)_numDepthMin.Value;
            int zMaxVal = (int)_numDepthMax.Value;
            double xMinVal = (double)_numXMinRoi.Value;
            double xMaxVal = (double)_numXMaxRoi.Value;
            double yMinVal = (double)_numYMinRoi.Value;
            double yMaxVal = (double)_numYMaxRoi.Value;

            // 查找或创建该相机的 ROI 参数条目
            var camParam = cfg.FindCameraParam(sn);
            bool isNew = camParam == null;
            if (isNew) camParam = new CameraRoiParam { CameraSn = sn };

            bool isBeam = _cmbRoiTarget.SelectedIndex == 1;
            if (isBeam)
            {
                camParam.BeamXMin = xMinVal;
                camParam.BeamXMax = xMaxVal;
                camParam.BeamYMin = yMinVal;
                camParam.BeamYMax = yMaxVal;
                camParam.BeamZMin = zMinVal;
                camParam.BeamZMax = zMaxVal;
            }
            else
            {
                camParam.ColXMin = xMinVal;
                camParam.ColXMax = xMaxVal;
                camParam.ColYMin = yMinVal;
                camParam.ColYMax = yMaxVal;
                camParam.ColZMin = zMinVal;
                camParam.ColZMax = zMaxVal;
            }

            // 计算双 ROI 的包络作为 Legacy 字段写入，维持向后兼容性
            double legacyXMin = camParam.ColXMax > camParam.ColXMin ? camParam.ColXMin : camParam.BeamXMin;
            double legacyXMax = camParam.ColXMax > camParam.ColXMin ? camParam.ColXMax : camParam.BeamXMax;
            if (camParam.BeamXMax > camParam.BeamXMin && camParam.ColXMax > camParam.ColXMin)
            {
                legacyXMin = Math.Min(camParam.ColXMin, camParam.BeamXMin);
                legacyXMax = Math.Max(camParam.ColXMax, camParam.BeamXMax);
            }
            camParam.XMin = legacyXMin;
            camParam.XMax = legacyXMax;

            double legacyYMin = camParam.ColYMax > camParam.ColYMin ? camParam.ColYMin : camParam.BeamYMin;
            double legacyYMax = camParam.ColYMax > camParam.ColYMin ? camParam.ColYMax : camParam.BeamYMax;
            if (camParam.BeamYMax > camParam.BeamYMin && camParam.ColYMax > camParam.ColYMin)
            {
                legacyYMin = Math.Min(camParam.ColYMin, camParam.BeamYMin);
                legacyYMax = Math.Max(camParam.ColYMax, camParam.BeamYMax);
            }
            camParam.YMin = legacyYMin;
            camParam.YMax = legacyYMax;

            int legacyZMin = camParam.ColZMax > camParam.ColZMin ? camParam.ColZMin : camParam.BeamZMin;
            int legacyZMax = camParam.ColZMax > camParam.ColZMin ? camParam.ColZMax : camParam.BeamZMax;
            if (camParam.BeamZMax > camParam.BeamZMin && camParam.ColZMax > camParam.ColZMin)
            {
                legacyZMin = Math.Min(camParam.ColZMin, camParam.BeamZMin);
                legacyZMax = Math.Max(camParam.ColZMax, camParam.BeamZMax);
            }
            camParam.ZMin = legacyZMin;
            camParam.ZMax = legacyZMax;

            // 保存各自的标准值：左相机保存左立柱与左半横梁标准值，右相机保存右立柱与右半横梁标准值
            var leftCamSn = _currentSideSNs.Count > 0 ? _currentSideSNs[0] : null;
            var rightCamSn = _currentSideSNs.Count > 1 ? _currentSideSNs[1] : null;

            if (!string.IsNullOrEmpty(leftCamSn))
            {
                var leftParam = (leftCamSn == sn) ? camParam : cfg.FindCameraParam(leftCamSn);
                if (leftParam == null)
                {
                    leftParam = new CameraRoiParam { CameraSn = leftCamSn };
                    cfg.CameraRoiParams.Add(leftParam);
                }
                leftParam.RefRackDefLeft = _refRackDefLeft;
                leftParam.RefBeamDef = _refBeamDefLeft;
            }

            if (!string.IsNullOrEmpty(rightCamSn))
            {
                var rightParam = (rightCamSn == sn) ? camParam : cfg.FindCameraParam(rightCamSn);
                if (rightParam == null)
                {
                    rightParam = new CameraRoiParam { CameraSn = rightCamSn };
                    cfg.CameraRoiParams.Add(rightParam);
                }
                rightParam.RefRackDefRight = _refRackDefRight;
                rightParam.RefBeamDef = _refBeamDefRight;
            }

            // 加入列表（新条目）/ 列表中原位置不变
            if (isNew && sn != leftCamSn && sn != rightCamSn) cfg.CameraRoiParams.Add(camParam);

            ConfigManager.SaveConfig();
            string targetName = isBeam ? "横梁" : "立柱";
            AppendLog($"✅ 已保存相机 [{sn}] 的 [{targetName} ROI] 及其向后兼容参数", false);
            AppendLog($"   标准基准: 立柱L={_refRackDefLeft:F2} R={_refRackDefRight:F2}, 横梁L={_refBeamDefLeft:F2} R={_refBeamDefRight:F2}mm", false);
        }
    }
}
