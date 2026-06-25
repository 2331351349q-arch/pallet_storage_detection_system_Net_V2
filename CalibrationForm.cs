using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using pallet_storage_detection_system_Net_V2.Algorithms;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2
{
    /// <summary>
    /// 3D 相机外参标定窗体。
    /// 流程：选相机 → 看预览 → 框选 ROI（垂直参考面）→ 输入参数 → 执行标定 → 保存。
    /// </summary>
    public class CalibrationForm : Form
    {
        // ---- 控件 ----
        private ComboBox _cmbCamera = null!;
        private Button _btnCapture = null!;
        private Button _btnCalibrate = null!;
        private Button _btnSave = null!;

        private PictureBox _pbPreview = null!;
        private Label _lblRoiInfo = null!;

        // 内参
        private TextBox _txtFx = null!;
        private TextBox _txtFy = null!;
        private TextBox _txtCx = null!;
        private TextBox _txtCy = null!;

        // 目标法向量
        private TextBox _txtTargetNx = null!;
        private TextBox _txtTargetNy = null!;
        private TextBox _txtTargetNz = null!;

        // 手动测量距离
        private TextBox _txtDist = null!;

        // 结果显示
        private TextBox _txtResult = null!;

        // ---- 状态 ----
        private ICameraDevice? _currentCamera;
        private DepthFrameData? _capturedFrame;
        private CameraCalibration? _lastResult;

        // ROI 绘制
        private bool _isDrawingRoi;
        private Point _roiStart;
        private Rectangle _roiRect;
        private bool _hasRoi;

        public CalibrationForm()
        {
            InitializeUi();
            PopulateCameraList();
        }

        // ============ UI 初始化 ============

        private void InitializeUi()
        {
            Text = "3D 相机外参标定工具";
            Width = 1280;
            Height = 900;
            MinimumSize = new Size(1024, 700);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;

            var normalFont = new Font("Microsoft YaHei UI", 9F);

            // ---- 整体左右分割（使用 TableLayoutPanel 避免 SplitContainer 边界问题）----
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            Controls.Add(mainLayout);

            // ====== 左侧：预览区 ======
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            mainLayout.Controls.Add(leftPanel, 0, 0);

            // --- 顶栏（精简：仅相机 + 抓图 + ROI 提示）---
            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 8, 8, 8),
                WrapContents = true
            };
            leftPanel.Controls.Add(topBar);

            _cmbCamera = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 10F) };
            topBar.Controls.Add(_cmbCamera);

            _btnCapture = new Button { AutoSize = true, Padding = new Padding(10, 0, 10, 0), Height = 32, Text = "📷 抓取一帧", Font = normalFont, FlatStyle = FlatStyle.Flat, BackColor = Color.LightGreen };
            _btnCapture.Click += BtnCapture_Click;
            topBar.Controls.Add(_btnCapture);

            _lblRoiInfo = new Label { Text = "ROI: 拖动鼠标框选参考平面", Font = normalFont, ForeColor = Color.Gray, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, MinimumSize = new Size(200, 32) };
            topBar.Controls.Add(_lblRoiInfo);

            // --- 预览图 ---
            _pbPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            _pbPreview.MouseDown += PbPreview_MouseDown;
            _pbPreview.MouseMove += PbPreview_MouseMove;
            _pbPreview.MouseUp += PbPreview_MouseUp;
            _pbPreview.Paint += PbPreview_Paint;
            leftPanel.Controls.Add(_pbPreview);

            // ====== 右侧：参数面板（动态距离计算垂直布局，避免高度和缩放重叠）======
            var rightOuter = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0) };
            mainLayout.Controls.Add(rightOuter, 1, 0);

            int y = 8;
            const int Gap = 12;
            const int InputW = 100;
            var boldFont = new Font(normalFont.FontFamily, normalFont.Size + 1, FontStyle.Bold);

            // ---- 辅助函数 ----
            Label MakeTitle(string text)
            {
                var lbl = new Label
                {
                    Text = $"◆ {text}",
                    Font = boldFont,
                    ForeColor = Color.FromArgb(60, 60, 120),
                    AutoSize = true,
                    Location = new Point(10, y)
                };
                rightOuter.Controls.Add(lbl);
                y += lbl.PreferredHeight + 10;
                return lbl;
            }

            void AddField(string label, string defaultVal, out TextBox txt)
            {
                var lbl = new Label
                {
                    Text = label + ":",
                    Font = normalFont,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(14, y + 4)
                };
                rightOuter.Controls.Add(lbl);

                txt = new TextBox
                {
                    Width = InputW,
                    Text = defaultVal,
                    Font = normalFont,
                    Location = new Point(14 + Math.Max(60, lbl.PreferredWidth + 5), y)
                };
                rightOuter.Controls.Add(txt);
                y += Math.Max(lbl.PreferredHeight, txt.PreferredHeight) + 8;
            }

            // === 1. 相机内参 ===
            MakeTitle("相机内参");
            AddField("Fx", "1000", out _txtFx!);
            AddField("Fy", "1000", out _txtFy!);
            AddField("Cx", "320",  out _txtCx!);
            AddField("Cy", "240",  out _txtCy!);
            y += Gap - 5;

            // === 2. 参考平面法向量 ===
            MakeTitle("参考平面法向量");

            int currX = 14;
            var nLbl = new Label { Text = "X:", Font = normalFont, AutoSize = true, Location = new Point(currX, y + 4) };
            rightOuter.Controls.Add(nLbl);
            currX += nLbl.PreferredWidth + 2;

            _txtTargetNx = new TextBox { Width = 55, Text = "0", Font = normalFont, Location = new Point(currX, y) };
            rightOuter.Controls.Add(_txtTargetNx);
            currX += _txtTargetNx.Width + 12;

            var nyLbl = new Label { Text = "Y:", Font = normalFont, AutoSize = true, Location = new Point(currX, y + 4) };
            rightOuter.Controls.Add(nyLbl);
            currX += nyLbl.PreferredWidth + 2;

            _txtTargetNy = new TextBox { Width = 55, Text = "1", Font = normalFont, Location = new Point(currX, y) };
            rightOuter.Controls.Add(_txtTargetNy);
            currX += _txtTargetNy.Width + 12;

            var nzLbl = new Label { Text = "Z:", Font = normalFont, AutoSize = true, Location = new Point(currX, y + 4) };
            rightOuter.Controls.Add(nzLbl);
            currX += nzLbl.PreferredWidth + 2;

            _txtTargetNz = new TextBox { Width = 55, Text = "0", Font = normalFont, Location = new Point(currX, y) };
            rightOuter.Controls.Add(_txtTargetNz);

            y += Math.Max(nLbl.PreferredHeight, _txtTargetNx.PreferredHeight) + 12;

            // 预设按钮行
            string[] dirs = { "X方向(1,0,0)", "Y方向(0,1,0)", "Z方向(0,0,1)" };
            double[][] vals = { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } };
            int btnX = 14;
            for (int i = 0; i < 3; i++)
            {
                var idx = i;
                var btn = new Button
                {
                    Text = dirs[i],
                    AutoSize = true,
                    Padding = new Padding(2),
                    Height = 28,
                    Font = new Font(normalFont.FontFamily, 8F),
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(btnX, y)
                };
                btn.Click += (_, _) =>
                {
                    _txtTargetNx.Text = vals[idx][0].ToString();
                    _txtTargetNy.Text = vals[idx][1].ToString();
                    _txtTargetNz.Text = vals[idx][2].ToString();
                };
                rightOuter.Controls.Add(btn);
                btnX += btn.PreferredSize.Width + 10;
            }
            y += 38;
            y += Gap;

            // === 3. 物理测量值 ===
            MakeTitle("物理测量值");
            var distLbl = new Label
            {
                Text = "平面距离(mm):",
                Font = normalFont,
                AutoSize = true,
                Location = new Point(14, y + 4)
            };
            rightOuter.Controls.Add(distLbl);

            _txtDist = new TextBox
            {
                Width = 80,
                Text = "0",
                Font = normalFont,
                Location = new Point(14 + distLbl.PreferredWidth + 10, y)
            };
            rightOuter.Controls.Add(_txtDist);
            y += Math.Max(distLbl.PreferredHeight, _txtDist.PreferredHeight) + 12;
            y += Gap;

            // === 4. 操作按钮 ===
            _btnCalibrate = new Button
            {
                AutoSize = true,
                Padding = new Padding(10, 0, 10, 0),
                Height = 36,
                Text = "🎯 执行标定",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                Enabled = false,
                Location = new Point(14, y)
            };
            _btnCalibrate.Click += BtnCalibrate_Click;

            _btnSave = new Button
            {
                AutoSize = true,
                Padding = new Padding(10, 0, 10, 0),
                Height = 36,
                Text = "💾 保存",
                Font = normalFont,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.LightGray,
                Enabled = false,
                Location = new Point(14 + Math.Max(120, _btnCalibrate.PreferredSize.Width) + 10, y)
            };
            _btnSave.Click += BtnSave_Click;

            rightOuter.Controls.Add(_btnCalibrate);
            rightOuter.Controls.Add(_btnSave);
            y += _btnCalibrate.Height + 12;
            y += Gap;

            // === 5. 标定结果 ===
            MakeTitle("标定结果");
            _txtResult = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGreen,
                WordWrap = false,
                Location = new Point(10, y),
            };
            rightOuter.Controls.Add(_txtResult);

            // 动态调整结果区宽度和高度
            rightOuter.Resize += (_, _) =>
            {
                int w = rightOuter.ClientSize.Width - 20 - SystemInformation.VerticalScrollBarWidth;
                _txtResult.Width = Math.Max(200, w);

                // 确保结果框能有效填充到面板底部，且给予一个比较大的最小高度
                int calculatedHeight = rightOuter.ClientSize.Height - _txtResult.Top - 12;
                _txtResult.Height = Math.Max(200, calculatedHeight);
            };

            // 初始时触发一次调整以设定正确的结果框大小
            // 因为 Resize 可能在控件真正显示前不会给予足够的高度
            _txtResult.Size = new Size(200, 200);
        }

        // ============ 相机列表 ============

        private void PopulateCameraList()
        {
            _cmbCamera.Items.Clear();
            var cams = ConfigManager.Instance?.Cameras;
            if (cams != null)
            {
                foreach (var c in cams.Where(x => x.Type == "Tycam3D"))
                    _cmbCamera.Items.Add($"{c.Name} ({c.Sn})");
            }
            if (_cmbCamera.Items.Count > 0)
                _cmbCamera.SelectedIndex = 0;
        }

        private string? GetSelectedCameraSn()
        {
            if (_cmbCamera.SelectedItem == null) return null;
            var text = _cmbCamera.SelectedItem.ToString()!;
            int p0 = text.LastIndexOf('('), p1 = text.LastIndexOf(')');
            if (p0 >= 0 && p1 > p0)
                return text[(p0 + 1)..p1];
            return null;
        }

        // ============ 抓取一帧 ============

        private async void BtnCapture_Click(object? sender, EventArgs e)
        {
            _btnCapture.Enabled = false;
            _btnCapture.Text = "⏳ 采集中...";
            _btnCalibrate.Enabled = false;

            string? sn = GetSelectedCameraSn();
            if (sn == null) { MessageBox.Show("请先选择相机。"); _btnCapture.Enabled = true; _btnCapture.Text = "📷 抓取一帧"; return; }

            _currentCamera = DeviceManager.GetCamera(sn);
            if (_currentCamera == null) { MessageBox.Show($"未找到相机 {sn}"); _btnCapture.Enabled = true; _btnCapture.Text = "📷 抓取一帧"; return; }

            try
            {
                _txtResult.Text = "";
                if (!_currentCamera.IsConnected)
                    _currentCamera.Connect();

                // 先抓取一帧
                var obj = await _currentCamera.GrabFrameAsync();
                if (obj is DepthFrameData df)
                {
                    _capturedFrame = df;
                    _pbPreview.Image?.Dispose();
                    _pbPreview.Image = new Bitmap(df.PreviewImage);
                    _pbPreview.Invalidate();
                    _btnCalibrate.Enabled = _hasRoi;
                    _lblRoiInfo.Text = _hasRoi
                        ? $"ROI: ({_roiRect.X},{_roiRect.Y}) {_roiRect.Width}x{_roiRect.Height}"
                        : "ROI: 请拖动鼠标框选参考平面";
                    _lblRoiInfo.ForeColor = _hasRoi ? Color.Green : Color.Gray;
                }
                else
                {
                    MessageBox.Show("采集失败：未获取到深度数据。");
                    _btnCapture.Enabled = true;
                    _btnCapture.Text = "📷 抓取一帧";
                    return;
                }

                // 从 SDK 读取内参（优先用帧元数据中已缓存的内参，其次用 GetIntrinsicsWithLog）
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"════ 内参读取 {_currentCamera.SerialNumber} ════");

                // ★ 优先使用帧回调中已从 .Info 提取的内参
                if (df.Intrinsics.HasValue)
                {
                    var intr = df.Intrinsics.Value;
                    _txtFx.Text = intr.fx.ToString("F2");
                    _txtFy.Text = intr.fy.ToString("F2");
                    _txtCx.Text = intr.cx.ToString("F2");
                    _txtCy.Text = intr.cy.ToString("F2");
                    sb.AppendLine($"✓ 从帧元数据提取成功: fx={intr.fx:F2} fy={intr.fy:F2} cx={intr.cx:F2} cy={intr.cy:F2}");
                }
                else
                {
                    sb.AppendLine("帧元数据未提取到内参，尝试 GetIntrinsicsWithLog...");
                    void Log(string msg) { sb.AppendLine(msg); }

                    var intrinsics = _currentCamera is PercipioCamera percipio
                        ? percipio.GetIntrinsicsWithLog(Log)
                        : null;

                    if (intrinsics.HasValue)
                    {
                        _txtFx.Text = intrinsics.Value.fx.ToString("F2");
                        _txtFy.Text = intrinsics.Value.fy.ToString("F2");
                        _txtCx.Text = intrinsics.Value.cx.ToString("F2");
                        _txtCy.Text = intrinsics.Value.cy.ToString("F2");
                    }
                    else
                    {
                        sb.AppendLine();
                        sb.AppendLine("✗ 所有路径均未能读取内参，将使用默认值 fx=fy=1000, cx=w/2, cy=h/2");
                    }
                }
                _txtResult.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"采集失败: {ex.Message}");
            }

            _btnCapture.Enabled = true;
            _btnCapture.Text = "📷 抓取一帧";
        }

        // ============ ROI 绘制 ============

        private void PbPreview_MouseDown(object? sender, MouseEventArgs e)
        {
            if (_capturedFrame == null) return;
            _isDrawingRoi = true;
            _roiStart = e.Location;
            _roiRect = Rectangle.Empty;
        }

        private void PbPreview_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDrawingRoi) return;

            // 转换为图像坐标
            var (imgPt, _) = ControlToImage(e.Location);
            var (startPt, _) = ControlToImage(_roiStart);

            _roiRect = new Rectangle(
                Math.Min(startPt.X, imgPt.X),
                Math.Min(startPt.Y, imgPt.Y),
                Math.Abs(imgPt.X - startPt.X),
                Math.Abs(imgPt.Y - startPt.Y)
            );
            _hasRoi = _roiRect.Width > 5 && _roiRect.Height > 5;
            _lblRoiInfo.Text = _hasRoi
                ? $"ROI: ({_roiRect.X},{_roiRect.Y}) {_roiRect.Width}x{_roiRect.Height}"
                : "ROI: 拖动选择";
            _lblRoiInfo.ForeColor = _hasRoi ? Color.Green : Color.Gray;
            _pbPreview.Invalidate();
        }

        private void PbPreview_MouseUp(object? sender, MouseEventArgs e)
        {
            _isDrawingRoi = false;
            _btnCalibrate.Enabled = _hasRoi && _capturedFrame != null;
        }

        private void PbPreview_Paint(object? sender, PaintEventArgs e)
        {
            if (!_hasRoi || _capturedFrame == null) return;

            // 转换 ROI 坐标到控件坐标
            var topLeft = ImageToControl(new Point(_roiRect.Left, _roiRect.Top));
            var botRight = ImageToControl(new Point(_roiRect.Right, _roiRect.Bottom));

            using var pen = new Pen(Color.Lime, 2) { DashStyle = DashStyle.Dash };
            int x = Math.Min(topLeft.X, botRight.X);
            int y = Math.Min(topLeft.Y, botRight.Y);
            int w = Math.Abs(botRight.X - topLeft.X);
            int h = Math.Abs(botRight.Y - topLeft.Y);
            e.Graphics.DrawRectangle(pen, x, y, w, h);

            // 半透明填充
            using var brush = new SolidBrush(Color.FromArgb(30, 0, 255, 0));
            e.Graphics.FillRectangle(brush, x, y, w, h);
        }

        private (Point imagePt, double scale) ControlToImage(Point controlPt)
        {
            if (_pbPreview.Image == null) return (controlPt, 1);

            double scaleX = (double)_pbPreview.Image.Width / _pbPreview.ClientSize.Width;
            double scaleY = (double)_pbPreview.Image.Height / _pbPreview.ClientSize.Height;
            double scale = Math.Max(scaleX, scaleY);

            int imgW = (int)(_pbPreview.Image.Width / scale);
            int imgH = (int)(_pbPreview.Image.Height / scale);
            int offsetX = (_pbPreview.ClientSize.Width - imgW) / 2;
            int offsetY = (_pbPreview.ClientSize.Height - imgH) / 2;

            int ix = (int)((controlPt.X - offsetX) * scale);
            int iy = (int)((controlPt.Y - offsetY) * scale);

            ix = Math.Clamp(ix, 0, _pbPreview.Image.Width - 1);
            iy = Math.Clamp(iy, 0, _pbPreview.Image.Height - 1);
            return (new Point(ix, iy), scale);
        }

        private Point ImageToControl(Point imagePt)
        {
            if (_pbPreview.Image == null) return imagePt;

            double scaleX = (double)_pbPreview.Image.Width / _pbPreview.ClientSize.Width;
            double scaleY = (double)_pbPreview.Image.Height / _pbPreview.ClientSize.Height;
            double scale = Math.Max(scaleX, scaleY);

            int imgW = (int)(_pbPreview.Image.Width / scale);
            int imgH = (int)(_pbPreview.Image.Height / scale);
            int offsetX = (_pbPreview.ClientSize.Width - imgW) / 2;
            int offsetY = (_pbPreview.ClientSize.Height - imgH) / 2;

            return new Point(
                (int)(imagePt.X / scale) + offsetX,
                (int)(imagePt.Y / scale) + offsetY
            );
        }

        // ============ 标定执行 ============

        private void BtnCalibrate_Click(object? sender, EventArgs e)
        {
            if (_capturedFrame == null)
            {
                MessageBox.Show("请先捕获一帧图像。");
                return;
            }

            try
            {
                // 读取目标法向量参数
                double tnx = double.Parse(_txtTargetNx.Text);
                double tny = double.Parse(_txtTargetNy.Text);
                double tnz = double.Parse(_txtTargetNz.Text);
                double tlen = Math.Sqrt(tnx * tnx + tny * tny + tnz * tnz);
                if (tlen < 1e-9) { MessageBox.Show("目标法向量不能为零向量。"); return; }
                tnx /= tlen; tny /= tlen; tnz /= tlen;

                double measuredDist = double.Parse(_txtDist.Text);

                string sn = GetSelectedCameraSn() ?? "unknown";

                // ROI
                int[] roi = { _roiRect.Left, _roiRect.Top, _roiRect.Right, _roiRect.Bottom };

                // ★ 方案A：使用新方法，直接传入 DepthFrameData，自动从 SDK 内参生成点云
                _lastResult = CalibrationAlgo.Calibrate(
                    sn,
                    _capturedFrame,       // DepthFrameData（已携带内参）
                    roi,
                    new[] { tnx, tny, tnz },
                    measuredDist
                );

                // 显示结果
                _btnSave.Enabled = true;
                _btnSave.BackColor = Color.LimeGreen;

                var r = _lastResult.RotationMatrix;
                var t = _lastResult.TranslationVector;
                var nc = _lastResult.RefPlaneNormalCam;
                string intrinsicSource = _capturedFrame.Intrinsics.HasValue ? "SDK帧元数据" : "默认值";
                _txtResult.Text =
                    $"✅ 标定成功！  ({_lastResult.CalibratedAt:yyyy-MM-dd HH:mm:ss})\r\n\r\n" +
                    $"相机: {_lastResult.CameraSn}\r\n" +
                    $"内参来源: {intrinsicSource}\r\n" +
                    $"内参: fx={_lastResult.Fx:F1} fy={_lastResult.Fy:F1} cx={_lastResult.Cx:F1} cy={_lastResult.Cy:F1}\r\n" +
                    $"\r\n━━ 旋转矩阵 R ━━\r\n" +
                    $"  [{r[0][0],10:F6}  {r[0][1],10:F6}  {r[0][2],10:F6}]\r\n" +
                    $"  [{r[1][0],10:F6}  {r[1][1],10:F6}  {r[1][2],10:F6}]\r\n" +
                    $"  [{r[2][0],10:F6}  {r[2][1],10:F6}  {r[2][2],10:F6}]\r\n" +
                    $"\r\n━━ 平移向量 T (mm) ━━\r\n" +
                    $"  [{t[0],10:F2}  {t[1],10:F2}  {t[2],10:F2}]\r\n" +
                    $"\r\n━━ 参考平面检测 ━━\r\n" +
                    $"  相机系法向量: [{nc[0]:F6}, {nc[1]:F6}, {nc[2]:F6}]\r\n" +
                    $"  目标法向量:   [{tnx:F6}, {tny:F6}, {tnz:F6}]\r\n" +
                    $"  平面 D 值:    {_lastResult.PlaneD:F2}\r\n" +
                    $"  测量距离:     {_lastResult.MeasuredDistanceMm:F1} mm\r\n" +
                    $"\r\n━━ 变换公式 ━━\r\n" +
                    $"  P_base = R · P_cam + T\r\n";
            }
            catch (Exception ex)
            {
                _txtResult.Text = $"❌ 标定失败:\r\n{ex.Message}";
                _btnSave.Enabled = false;
            }
        }

        // ============ 保存 ============

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (_lastResult == null) return;

            try
            {
                var config = ConfigManager.Instance;
                if (config == null)
                {
                    MessageBox.Show("ConfigManager.Instance 为 null！", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 确保 Calibrations 列表已初始化（JSON 中没有时可能为 null）
                config.Calibrations ??= new List<CameraCalibration>();

                // 移除同 SN 旧记录
                int removed = config.Calibrations.RemoveAll(c => c.CameraSn == _lastResult.CameraSn);
                config.Calibrations.Add(_lastResult);

                ConfigManager.SaveConfig();

                MessageBox.Show($"标定结果已保存！\n相机: {_lastResult.CameraSn}\n" +
                    $"移除旧记录: {removed} 条\n保存路径: Config/config.json",
                    "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace}",
                    "保存异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
