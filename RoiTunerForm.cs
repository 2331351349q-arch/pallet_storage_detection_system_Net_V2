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
        private TextBox _txtCameraSn = null!;
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

        private DepthFrameData? _currentFrame;

        public RoiTunerForm()
        {
            InitializeUi();
            LoadRoiFromCurrentSide();
            UpdateCameraSnFromConfig();
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
                SplitterWidth = 6
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
                LoadRoiFromCurrentSide();
                RedrawCloudAndStats();
            };
            leftFlow.Controls.Add(_cmbSide);

            leftFlow.Controls.Add(new Label { Text = "目标相机 SN", Width = 340, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) });
            _txtCameraSn = new TextBox { Width = 340 };
            leftFlow.Controls.Add(_txtCameraSn);

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
            _numYaw.ValueChanged += (_, __) => RedrawCloudAndStats();
            _numPitch.ValueChanged += (_, __) => RedrawCloudAndStats();
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
                SplitterWidth = 6
            };
            _mainSplit.Panel2.Controls.Add(_rightSplit);

            _pbCloud = new HighQualityPictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.CenterImage };
            _pbCloud.Resize += (_, __) =>
            {
                if (_currentFrame != null)
                {
                    RedrawCloudAndStats();
                }
            };
            _pbCloud.MouseDown += PbCloud_MouseDown;
            _pbCloud.MouseMove += PbCloud_MouseMove;
            _pbCloud.MouseUp += PbCloud_MouseUp;
            _pbCloud.MouseLeave += (_, __) => _isDraggingView = false;
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

            Shown += (_, __) => ApplyBestLayoutProportion();
            Resize += (_, __) => ApplyBestLayoutProportion();
        }

        private void ApplyBestLayoutProportion()
        {
            if (_mainSplit == null || _mainSplit.IsDisposed) return;

            int totalWidth = _mainSplit.ClientSize.Width;
            if (totalWidth <= 120) return;

            int leftMin = Math.Min(320, Math.Max(80, totalWidth - _mainSplit.SplitterWidth - 120));
            int rightMin = Math.Min(760, Math.Max(80, totalWidth - _mainSplit.SplitterWidth - leftMin));

            _mainSplit.Panel1MinSize = leftMin;
            _mainSplit.Panel2MinSize = rightMin;

            int minLeft = _mainSplit.Panel1MinSize;
            int maxLeft = totalWidth - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth;
            if (maxLeft < minLeft) maxLeft = minLeft;

            int desiredLeft = (int)(totalWidth * 0.26);
            desiredLeft = Math.Min(460, desiredLeft);
            desiredLeft = Math.Clamp(desiredLeft, minLeft, maxLeft);
            _mainSplit.SplitterDistance = desiredLeft;

            if (_rightSplit == null || _rightSplit.IsDisposed) return;

            int totalHeight = _rightSplit.ClientSize.Height;
            if (totalHeight <= 120) return;

            int topMin = Math.Min(360, Math.Max(80, totalHeight - _rightSplit.SplitterWidth - 100));
            int bottomMin = Math.Min(180, Math.Max(80, totalHeight - _rightSplit.SplitterWidth - topMin));

            _rightSplit.Panel1MinSize = topMin;
            _rightSplit.Panel2MinSize = bottomMin;

            int minTop = _rightSplit.Panel1MinSize;
            int maxTop = totalHeight - _rightSplit.Panel2MinSize - _rightSplit.SplitterWidth;
            if (maxTop < minTop) maxTop = minTop;

            int desiredTop = (int)(totalHeight * 0.74);
            desiredTop = Math.Clamp(desiredTop, minTop, maxTop);
            _rightSplit.SplitterDistance = desiredTop;
        }

        private void PbCloud_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _isDraggingView = true;
            _lastMousePoint = e.Location;
            _pbCloud.Cursor = Cursors.SizeAll;
        }

        private void PbCloud_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDraggingView) return;

            int dx = e.X - _lastMousePoint.X;
            int dy = e.Y - _lastMousePoint.Y;
            _lastMousePoint = e.Location;

            decimal yawStep = (decimal)(dx * 0.6);
            decimal pitchStep = (decimal)(-dy * 0.45);

            decimal newYaw = Math.Clamp(_numYaw.Value + yawStep, _numYaw.Minimum, _numYaw.Maximum);
            decimal newPitch = Math.Clamp(_numPitch.Value + pitchStep, _numPitch.Minimum, _numPitch.Maximum);

            _numYaw.Value = newYaw;
            _numPitch.Value = newPitch;
        }

        private void PbCloud_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _isDraggingView = false;
            _pbCloud.Cursor = Cursors.Hand;
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

            string sn = CurrentSide() == "right"
                ? (mapping.RightSideSns?.FirstOrDefault() ?? string.Empty)
                : (mapping.LeftSideSns?.FirstOrDefault() ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(sn))
            {
                _txtCameraSn.Text = sn;
            }
        }

        private void LoadRoiFromCurrentSide()
        {
            var cfg = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
            var roiList = CurrentSide() == "right" ? cfg?.Roi3dRight : cfg?.Roi3dLeft;
            var roi = (roiList != null && roiList.Count >= 6)
                ? new Roi3D(roiList[0], roiList[1], roiList[2], roiList[3], roiList[4], roiList[5])
                : new Roi3D(-500, 500, -500, 500, 1000, 3000);

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
            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrWhiteSpace(sn))
            {
                AppendLog("请先填写相机SN", true);
                return;
            }

            if (DeviceManager.GetCamera(sn) != null)
            {
                AppendLog($"相机已在设备池中: {sn}", false);
                return;
            }

            var cfg = ConfigManager.Instance?.Cameras?
                .FirstOrDefault(c => string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase)
                                     && (c.Type?.Contains("Tycam", StringComparison.OrdinalIgnoreCase) == true
                                         || c.Type?.Contains("Percipio", StringComparison.OrdinalIgnoreCase) == true));
            if (cfg == null)
            {
                AppendLog($"配置中未找到该SN的3D相机: {sn}", true);
                return;
            }

            await Task.Run(() => DeviceManager.Initialize(new List<CameraConfig> { cfg }, m => BeginInvoke(new Action(() => AppendLog(m, false)))));

            bool initOk = DeviceManager.GetCamera(sn) != null;
            if (initOk)
            {
                var calib = ConfigManager.GetCalibration(sn);
                bool hasCalib = calib != null && calib.IsValid;
                AppendLog($"相机初始化成功: {sn} | 外参标定: {(hasCalib ? "✓" : "✗ 未标定")}", false);
            }
            else
            {
                AppendLog($"相机初始化失败: {sn}", true);
            }
        }

        private async void BtnGrab_Click(object? sender, EventArgs e)
        {
            string sn = _txtCameraSn.Text.Trim();
            if (string.IsNullOrWhiteSpace(sn))
            {
                AppendLog("请先填写相机SN", true);
                return;
            }

            var cam = DeviceManager.GetCamera(sn);
            if (cam == null)
            {
                AppendLog("相机不在设备池，先点\"初始化目标相机\"", true);
                return;
            }
            if (!cam.IsConnected)
            {
                AppendLog("相机未连接，请检查硬件或重新初始化", true);
                return;
            }

            try
            {
                var frameObj = await cam.GrabFrameAsync();
                if (frameObj is Bitmap bmp)
                {
                    if (bmp.Width == 640 && bmp.Height == 480)
                        AppendLog("抓图超时(5秒内无深度帧回调)，请: 1)重新初始化 2)检查相机是否被占用", true);
                    else
                        AppendLog("抓图返回2D图像而非深度帧", true);
                    bmp.Dispose();
                    return;
                }
                if (frameObj is not DepthFrameData depth)
                {
                    AppendLog("抓图返回非深度帧", true);
                    return;
                }

                _currentFrame = depth;
                _pbPreview.Image?.Dispose();
                _pbPreview.Image = (Image)depth.PreviewImage.Clone();
                _lblPreviewMeta.Text = $"预览: SN={depth.CameraSn} | {depth.Width}x{depth.Height} | {DateTime.Now:HH:mm:ss.fff}";

                RedrawCloudAndStats();
                AppendLog($"抓图成功 {depth.CameraSn} {depth.Width}x{depth.Height}", false);
            }
            catch (Exception ex)
            {
                AppendLog($"抓图失败: {ex.Message}", true);
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (!TryReadRoi(out var roi))
            {
                AppendLog("ROI输入错误", true);
                return;
            }

            var slot = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
            if (slot == null)
            {
                AppendLog("配置对象不可用", true);
                return;
            }

            var roiList = new List<int>
            {
                (int)Math.Round(roi.MinX),
                (int)Math.Round(roi.MaxX),
                (int)Math.Round(roi.MinY),
                (int)Math.Round(roi.MaxY),
                (int)Math.Round(roi.MinZ),
                (int)Math.Round(roi.MaxZ)
            };

            if (CurrentSide() == "right") slot.Roi3dRight = roiList;
            else slot.Roi3dLeft = roiList;

            ConfigManager.SaveConfig();
            AppendLog($"ROI已保存到 config.json ({CurrentSide()})", false);
            RedrawCloudAndStats();
        }

        private void RedrawCloudAndStats()
        {
            if (!TryReadRoi(out var roi))
            {
                _lblStats.Text = "ROI输入非法";
                return;
            }

            if (_currentFrame == null)
            {
                _lblStats.Text = "当前无深度帧";
                return;
            }

            RenderCloudImage(_currentFrame, roi);

            int count = CountPointsInRoi(_currentFrame, roi);
            int threshold = ConfigManager.Instance?.Algorithms?.SlotOccupancy?.PointThreshold ?? 10000;
            _lblStats.Text = $"ROI内点数: {count} / 阈值: {threshold} / 判定: {(count > threshold ? "有货" : "无货")}";
        }

        private void RenderCloudImage(DepthFrameData frame, Roi3D roi)
        {
            int w = Math.Max(320, _pbCloud.ClientSize.Width);
            int h = Math.Max(240, _pbCloud.ClientSize.Height);
            var bmp = new Bitmap(w, h);

            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(16, 16, 18));

            int step = Math.Max(1, (int)_numSample.Value);
            var yaw = (double)_numYaw.Value * Math.PI / 180.0;
            var pitch = (double)_numPitch.Value * Math.PI / 180.0;

            // ★ 使用 SDK 原生点云（或回退手动公式），统一经过 GetPointCloud() 获取
            var pointCloud = frame.GetPointCloud();
            int totalStep = step * step;
            using var p = new SolidBrush(Color.Lime);
            for (int i = 0; i < pointCloud.Count; i += totalStep)
            {
                var pt = pointCloud[i];
                if (ProjectPoint(pt.X, -pt.Y, pt.Z, yaw, pitch, w, h, out var sx, out var sy))
                {
                    g.FillRectangle(p, sx, sy, 2, 2);
                }
            }

            using var pen = new Pen(Color.OrangeRed, 2f);
            DrawRoiCube2D(g, pen, roi, yaw, pitch, w, h);

            _pbCloud.Image?.Dispose();
            _pbCloud.Image = bmp;
        }

        private static bool ProjectPoint(double x, double y, double z, double yaw, double pitch, int w, int h, out float sx, out float sy)
        {
            double cosY = Math.Cos(yaw);
            double sinY = Math.Sin(yaw);
            double cosP = Math.Cos(pitch);
            double sinP = Math.Sin(pitch);

            double x1 = x * cosY + z * sinY;
            double z1 = -x * sinY + z * cosY;

            double y2 = y * cosP - z1 * sinP;
            double z2 = y * sinP + z1 * cosP;

            double dist = 4000.0;
            double d = z2 + dist;
            if (d <= 10)
            {
                sx = sy = 0;
                return false;
            }

            double f = 900.0;
            sx = (float)(w * 0.5 + x1 * f / d);
            sy = (float)(h * 0.5 - y2 * f / d);
            return sx >= 0 && sx < w && sy >= 0 && sy < h;
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
                bool ok = ProjectPoint(c.Item1, c.Item2, c.Item3, yaw, pitch, w, h, out var sx, out var sy);
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

        private int CountPointsInRoi(DepthFrameData frame, Roi3D roi)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.DepthRaw == null || frame.DepthRaw.Length == 0)
                return 0;

            // ★ 使用统一点云接口获取点云
            var points = frame.GetPointCloud();
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
