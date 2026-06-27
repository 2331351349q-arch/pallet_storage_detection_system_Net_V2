using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;

namespace pallet_storage_detection_system_Net_V2
{
    public class RoiTunerForm : Form
    {
        private ComboBox _cmbSide = null!;
        private ComboBox _cmbTuneCamera = null!;
        private NumericUpDown _numSample = null!;
        private NumericUpDown _numYaw = null!;
        private NumericUpDown _numPitch = null!;
        private TextBox _txtMinX = null!;
        private TextBox _txtMaxX = null!;
        private TextBox _txtMinY = null!;
        private TextBox _txtMaxY = null!;
        private TextBox _txtMinZ = null!;
        private TextBox _txtMaxZ = null!;
        private Label _lblStats = null!;
        private Label _lblPreviewMeta = null!;
        private PictureBox _pbCloud = null!;
        private PictureBox _pbPreview = null!;
        private TextBox _txtLog = null!;
        private SplitContainer _mainSplit = null!;
        private SplitContainer _rightSplit = null!;

        private bool _isDraggingView;
        private Point _lastMousePoint;
        private double _zoomLevel = 1.0;
        private double _panX, _panY;

        private DepthFrameData? _currentFrame1;
        private DepthFrameData? _currentFrame2;
        private List<string> _currentSideSNs = new List<string>();

        public RoiTunerForm()
        {
            InitializeUi();
            UpdateCameraSnFromConfig();
            LoadRoiForCurrentCamera();
            RedrawCloudAndStats();
        }

        private void InitializeUi()
        {
            Text = "SlotOccupancy ROI 调整工具（内嵌）";
            Width = 1500;
            Height = 900;
            StartPosition = FormStartPosition.CenterParent;

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 380,
                Panel1MinSize = 380
            };
            Controls.Add(_mainSplit);

            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            _mainSplit.Panel1.Controls.Add(leftPanel);

            var leftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            leftPanel.Controls.Add(leftFlow);

            leftFlow.Controls.Add(new Label { Text = "检测侧", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _cmbSide = new ComboBox { Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSide.Items.AddRange(new object[] { "left", "right" });
            _cmbSide.SelectedIndex = 0;
            _cmbSide.SelectedIndexChanged += (_, __) =>
            {
                UpdateCameraSnFromConfig();
                LoadRoiForCurrentCamera();
                RedrawCloudAndStats();
            };
            leftFlow.Controls.Add(_cmbSide);

            leftFlow.Controls.Add(new Label { Text = "当前调参相机 SN", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _cmbTuneCamera = new ComboBox { Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTuneCamera.SelectedIndexChanged += (_, __) =>
            {
                LoadRoiForCurrentCamera();
                UpdatePreviewImage();
                RedrawCloudAndStats();
            };
            leftFlow.Controls.Add(_cmbTuneCamera);

            var btnLine = new FlowLayoutPanel { Width = 340, Height = 36 };
            var btnInit = new Button { Text = "初始化目标相机", Width = 160, Height = 30 };
            btnInit.Click += BtnInit_Click;
            var btnGrab = new Button { Text = "抓取一帧", Width = 160, Height = 30 };
            btnGrab.Click += BtnGrab_Click;
            btnLine.Controls.Add(btnInit);
            btnLine.Controls.Add(btnGrab);
            leftFlow.Controls.Add(btnLine);

            leftFlow.Controls.Add(new Label { Text = "采样步长(点云)", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _numSample = new NumericUpDown { Width = 340, Minimum = 1, Maximum = 12, Value = 4 };
            _numSample.ValueChanged += (_, __) => RedrawCloudAndStats();
            leftFlow.Controls.Add(_numSample);

            leftFlow.Controls.Add(new Label { Text = "视角 Yaw / Pitch (度)", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _numYaw = new NumericUpDown { Width = 340, Minimum = -180, Maximum = 180, Value = 20 };
            _numPitch = new NumericUpDown { Width = 340, Minimum = -80, Maximum = 80, Value = -12 };
            _numYaw.ValueChanged += (_, __) => RedrawCloudImageOnly();
            _numPitch.ValueChanged += (_, __) => RedrawCloudImageOnly();
            leftFlow.Controls.Add(_numYaw);
            leftFlow.Controls.Add(_numPitch);

            leftFlow.Controls.Add(new Label { Text = "ROI (mm): minX, maxX, minY, maxY, minZ, maxZ", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _txtMinX = CreateRoiText(leftFlow);
            _txtMaxX = CreateRoiText(leftFlow);
            _txtMinY = CreateRoiText(leftFlow);
            _txtMaxY = CreateRoiText(leftFlow);
            _txtMinZ = CreateRoiText(leftFlow);
            _txtMaxZ = CreateRoiText(leftFlow);

            var btnSave = new Button { Text = "保存当前ROI到配置", Width = 340, Height = 30 };
            btnSave.Click += BtnSave_Click;
            leftFlow.Controls.Add(btnSave);

            _lblStats = new Label { Width = 340, Height = 60, AutoSize = false };
            leftFlow.Controls.Add(_lblStats);

            leftFlow.Controls.Add(new Label { Text = "日志", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _txtLog = new TextBox { Width = 340, Height = 240, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            leftFlow.Controls.Add(_txtLog);

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                SplitterDistance = (int)(Height * 0.74)
            };
            _mainSplit.Panel2.Controls.Add(_rightSplit);

            _pbCloud = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.CenterImage };
            _pbCloud.Resize += (_, __) =>
            {
                if (_currentFrame1 != null || _currentFrame2 != null)
                {
                    RedrawCloudAndStats();
                }
            };
            _pbCloud.MouseDown += PbCloud_MouseDown;
            _pbCloud.MouseMove += PbCloud_MouseMove;
            _pbCloud.MouseUp += PbCloud_MouseUp;
            _pbCloud.MouseLeave += (_, __) => _isDraggingView = false;
            _pbCloud.MouseWheel += PbCloud_MouseWheel;
            _pbCloud.Cursor = Cursors.Hand;
            _rightSplit.Panel1.Controls.Add(_pbCloud);

            var previewPanel = new Panel { Dock = DockStyle.Fill };
            _rightSplit.Panel2.Controls.Add(previewPanel);

            _lblPreviewMeta = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                ForeColor = Color.FromArgb(30, 50, 80),
                Text = "预览: 无图像",
                TextAlign = ContentAlignment.MiddleLeft
            };
            previewPanel.Controls.Add(_lblPreviewMeta);

            _pbPreview = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            _pbPreview.DoubleClick += (_, __) =>
            {
                _pbPreview.SizeMode = _pbPreview.SizeMode == PictureBoxSizeMode.Zoom
                    ? PictureBoxSizeMode.CenterImage
                    : PictureBoxSizeMode.Zoom;
            };
            var previewMenu = new ContextMenuStrip();
            previewMenu.Items.Add("适应窗口显示", null, (_, __) => _pbPreview.SizeMode = PictureBoxSizeMode.Zoom);
            previewMenu.Items.Add("原始比例(居中)", null, (_, __) => _pbPreview.SizeMode = PictureBoxSizeMode.CenterImage);
            _pbPreview.ContextMenuStrip = previewMenu;
            previewPanel.Controls.Add(_pbPreview);

        }

        private void PbCloud_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDraggingView = true;
                _lastMousePoint = e.Location;
                _pbCloud.Cursor = Cursors.SizeAll;
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

        private void PbCloud_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDraggingView) return;

            int dx = e.X - _lastMousePoint.X;
            int dy = e.Y - _lastMousePoint.Y;
            _lastMousePoint = e.Location;

            if ((ModifierKeys & Keys.Shift) != 0 || e.Button == MouseButtons.Middle)
            {
                _panX += dx;
                _panY += dy;
            }
            else
            {
                decimal yawStep = (decimal)(dx * 0.6);
                decimal pitchStep = (decimal)(-dy * 0.45);

                decimal newYaw = Math.Clamp(_numYaw.Value + yawStep, _numYaw.Minimum, _numYaw.Maximum);
                decimal newPitch = Math.Clamp(_numPitch.Value + pitchStep, _numPitch.Minimum, _numPitch.Maximum);

                _numYaw.Value = newYaw;
                _numPitch.Value = newPitch;
            }
            RedrawCloudImageOnly();
        }

        private void PbCloud_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _isDraggingView = false;
            _pbCloud.Cursor = Cursors.Hand;
            RedrawCloudAndStats();
        }

        private void PbCloud_MouseWheel(object? sender, MouseEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            _zoomLevel = Math.Clamp(_zoomLevel * factor, 0.2, 50.0);
            RedrawCloudAndStats();
            ((HandledMouseEventArgs)e).Handled = true;
        }

        private TextBox CreateRoiText(System.Windows.Forms.Control parent)
        {
            var txt = new TextBox { Width = 340 };
            txt.LostFocus += (_, __) => RedrawCloudAndStats();
            parent.Controls.Add(txt);
            return txt;
        }

        private string CurrentSide() => _cmbSide.SelectedItem?.ToString() ?? "left";

        private void UpdateCameraSnFromConfig()
        {
            var mapping = ConfigManager.Instance?.Algorithms?.SlotOccupancy?.CameraMapping;
            if (mapping == null) return;

            string side = CurrentSide();
            _currentSideSNs = side == "right"
                ? (mapping.RightSideSns ?? new List<string>())
                : (mapping.LeftSideSns ?? new List<string>());

            _cmbTuneCamera.Items.Clear();
            foreach (var sn in _currentSideSNs)
            {
                if (!string.IsNullOrWhiteSpace(sn)) _cmbTuneCamera.Items.Add(sn);
            }
            if (_cmbTuneCamera.Items.Count > 0)
                _cmbTuneCamera.SelectedIndex = 0;
        }

        private void LoadRoiForCurrentCamera()
        {
            var sn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            var cfg = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
            Roi3D roi;

            var p = cfg?.FindCameraParam(sn);
            if (p != null)
            {
                roi = new Roi3D(p.XMin, p.XMax, p.YMin, p.YMax, p.ZMin, p.ZMax);
            }
            else
            {
                var roiList = CurrentSide() == "right" ? cfg?.Roi3dRight : cfg?.Roi3dLeft;
                roi = (roiList != null && roiList.Count >= 6)
                    ? new Roi3D(roiList[0], roiList[1], roiList[2], roiList[3], roiList[4], roiList[5])
                    : new Roi3D(-500, 500, -500, 500, 1000, 3000);
            }

            _txtMinX.Text = roi.MinX.ToString("F0");
            _txtMaxX.Text = roi.MaxX.ToString("F0");
            _txtMinY.Text = roi.MinY.ToString("F0");
            _txtMaxY.Text = roi.MaxY.ToString("F0");
            _txtMinZ.Text = roi.MinZ.ToString("F0");
            _txtMaxZ.Text = roi.MaxZ.ToString("F0");
        }

        private bool TryReadRoi(out Roi3D roi)
        {
            roi = default;
            if (!double.TryParse(_txtMinX.Text, out var minX)) return false;
            if (!double.TryParse(_txtMaxX.Text, out var maxX)) return false;
            if (!double.TryParse(_txtMinY.Text, out var minY)) return false;
            if (!double.TryParse(_txtMaxY.Text, out var maxY)) return false;
            if (!double.TryParse(_txtMinZ.Text, out var minZ)) return false;
            if (!double.TryParse(_txtMaxZ.Text, out var maxZ)) return false;
            roi = new Roi3D(minX, maxX, minY, maxY, minZ, maxZ);
            return true;
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
                    AppendLog($"相机初始化成功: {cfg.Sn} | 外参标定: {(hasCalib ? "✓" : "✗ 未标定")}", false);
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
                AppendLog("正在抓取...", false);
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

                RedrawCloudAndStats();
            }
            catch (Exception ex) { AppendLog($"抓图异常: {ex.Message}", true); }
            finally { btn.Enabled = true; }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (!TryReadRoi(out var roi))
            {
                AppendLog("ROI输入错误", true);
                return;
            }

            var sn = _cmbTuneCamera.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sn))
            {
                AppendLog("相机SN为空，无法保存", true);
                return;
            }

            var slot = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
            if (slot == null)
            {
                AppendLog("配置对象不可用", true);
                return;
            }

            var p = slot.FindCameraParam(sn);
            if (p == null)
            {
                p = new CameraRoiParam { CameraSn = sn };
                slot.CameraRoiParams.Add(p);
            }

            p.XMin = roi.MinX;
            p.XMax = roi.MaxX;
            p.YMin = roi.MinY;
            p.YMax = roi.MaxY;
            p.ZMin = (int)roi.MinZ;
            p.ZMax = (int)roi.MaxZ;

            ConfigManager.SaveConfig();
            AppendLog($"ROI已保存到 config.json (SN={sn})", false);
            RedrawCloudAndStats();
        }

        private List<System.Numerics.Vector3> _mergedPtsCache = new List<System.Numerics.Vector3>();

        private DepthFrameData? GetSelectedFrame()
        {
            var sn = _cmbTuneCamera.SelectedItem?.ToString();
            if (_currentFrame1 != null && _currentFrame1.CameraSn == sn) return _currentFrame1;
            if (_currentFrame2 != null && _currentFrame2.CameraSn == sn) return _currentFrame2;
            return _currentFrame1 ?? _currentFrame2;
        }

        private void RedrawCloudAndStats()
        {
            if (!TryReadRoi(out var roi))
            {
                _lblStats.Text = "ROI输入非法";
                return;
            }

            var selectedFrame = GetSelectedFrame();
            if (selectedFrame == null)
            {
                _lblStats.Text = "当前无深度帧";
                return;
            }

            _mergedPtsCache.Clear();
            var pts = selectedFrame.GetPointCloud();
            if (pts != null) _mergedPtsCache.AddRange(pts);

            RenderCloudImage(_mergedPtsCache, roi);

            int count = CountPointsInRoi(_mergedPtsCache, roi);
            int threshold = ConfigManager.Instance?.Algorithms?.SlotOccupancy?.PointThreshold ?? 10000;
            string cameraLabel = selectedFrame.CameraSn?.Length > 6 ? "..." + selectedFrame.CameraSn[^6..] : selectedFrame.CameraSn ?? "?";
            _lblStats.Text = $"相机: {cameraLabel} | ROI内点数: {count} / 阈值: {threshold} / 判定: {(count > threshold ? "有货" : "无货")}";
        }

        private void RedrawCloudImageOnly()
        {
            if (!TryReadRoi(out var roi)) return;
            if (GetSelectedFrame() == null) return;
            
            RenderCloudImage(_mergedPtsCache, roi);
        }

        private void UpdatePreviewImage()
        {
            var targetFrame = GetSelectedFrame();
            if (targetFrame != null)
            {
                _pbPreview.Image?.Dispose();
                _pbPreview.Image = (Image)targetFrame.PreviewImage.Clone();
                _lblPreviewMeta.Text = $"预览: SN={targetFrame.CameraSn} | {targetFrame.Width}x{targetFrame.Height} | {DateTime.Now:HH:mm:ss.fff}";
            }
        }

        private void RenderCloudImage(List<System.Numerics.Vector3> pointCloud, Roi3D roi)
        {
            int w = Math.Max(320, _pbCloud.ClientSize.Width);
            int h = Math.Max(240, _pbCloud.ClientSize.Height);
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // GDI 绘制背景 + ROI 框 + 文字
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(16, 16, 18));
                double yaw = (double)_numYaw.Value * Math.PI / 180.0;
                double pitch = (double)_numPitch.Value * Math.PI / 180.0;
                using var pen = new Pen(Color.OrangeRed, 1.5f) { DashStyle = DashStyle.Dash };
                DrawRoiCube2D(g, pen, roi, yaw, pitch, w, h);

                using var infoFont = new Font("Consolas", 9F, FontStyle.Bold);
                using var hintFont = new Font("Microsoft YaHei UI", 7F);
                string? sn = _cmbTuneCamera.SelectedItem?.ToString();
                var calib = string.IsNullOrWhiteSpace(sn) ? null : ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                string calibText = hasCalib
                    ? $"✓ 标定已应用 (R|T) | SN={sn}"
                    : $"⚠ 无标定 — SN={sn ?? "?"} 显示相机原始坐标";
                Color calibColor = hasCalib ? Color.Lime : Color.OrangeRed;
                g.DrawString(calibText, infoFont, new SolidBrush(calibColor), 10, h - 52);
                g.DrawString($"缩放 ×{_zoomLevel:F1}", infoFont, Brushes.Gray, 10, h - 34);
                g.DrawString("左键旋转 | Shift+拖/中键平移 | 滚轮缩放 | 右键复位", hintFont, Brushes.DimGray, 10, h - 18);
            }

            // LockBits 高性能渲染点云
            double yaw2 = (double)_numYaw.Value * Math.PI / 180.0;
            double pitch2 = (double)_numPitch.Value * Math.PI / 180.0;

            int step = _isDraggingView ? Math.Max(8, (int)_numSample.Value) : Math.Max(1, (int)_numSample.Value);
            int totalStep = step * step;

            var drawList = new List<(int sx, int sy)>(pointCloud.Count / totalStep + 100);
            for (int i = 0; i < pointCloud.Count; i += totalStep)
            {
                var pt = pointCloud[i];
                if (ProjectPt(pt.X, -pt.Y, pt.Z, yaw2, pitch2, w, h, out int sx, out int sy))
                {
                    drawList.Add((sx, sy));
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

                void SetPixel(byte[] buf, int px, int py, byte r, byte g, byte b)
                {
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) return;
                    int off = py * stride + px * 4;
                    buf[off] = b; buf[off + 1] = g; buf[off + 2] = r; buf[off + 3] = 255;
                }

                int dynamicSz = (int)(Math.Max(1.0, _zoomLevel * 0.8));
                int dotSz = step <= 1 ? dynamicSz : 1;
                int hsz = dotSz / 2;
                byte r = 0, g = 255, b = 0; // 亮绿色点云

                foreach (var (sx, sy) in drawList)
                {
                    for (int dy = -hsz; dy <= hsz; dy++)
                        for (int dx = -hsz; dx <= hsz; dx++)
                            SetPixel(buffer, sx + dx, sy + dy, r, g, b);
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

        private int CountPointsInRoi(List<System.Numerics.Vector3> points, Roi3D roi)
        {
            if (points == null || points.Count == 0) return 0;

            int count = 0;

            foreach (var pt in points)
            {
                if (pt.X >= roi.MinX && pt.X <= roi.MaxX &&
                    pt.Y >= roi.MinY && pt.Y <= roi.MaxY &&
                    pt.Z >= roi.MinZ && pt.Z <= roi.MaxZ)
                {
                    count++;
                }
            }

            return count;
        }

        private void AppendLog(string message, bool isError)
        {
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {(isError ? "[ERR]" : "[INF]")} {message}{Environment.NewLine}");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        private readonly record struct Roi3D(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);
    }

    internal sealed class HighQualityPictureBox : PictureBox
    {
        public HighQualityPictureBox()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pe.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            base.OnPaint(pe);
        }
    }
}
