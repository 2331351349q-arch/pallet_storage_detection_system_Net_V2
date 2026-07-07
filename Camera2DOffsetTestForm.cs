using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;

namespace pallet_storage_detection_system_Net_V2
{
    /// <summary>
    /// 2D 立柱孔洞识别与偏移量计算测试工具。
    /// 支持选择特定相机 SN，载入现场图片或控制相机单帧采集 2D 彩色图，
    /// 支持手绘多边形 ROI，调整双模检测参数并保存，支持 RANSAC 单线拟合及标准参考值设定。
    /// </summary>
    public class Camera2DOffsetTestForm : Form
    {
        // ─────────────────────────── 配置文件路径 ───────────────────────────
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "config_2d_offset_test.json");

        // ─────────────────────────── 控件 ───────────────────────────
        private ComboBox _cmbCamera = null!;
        private ComboBox _cmbBeamLength = null!;
        private PictureBox _pbImage = null!;
        private TextBox _txtLog = null!;
        
        // 调节参数
        private NumericUpDown _nudCValue = null!;
        private NumericUpDown _nudBlockSize = null!;
        private NumericUpDown _nudMinArea = null!;
        private NumericUpDown _nudMaxArea = null!;
        
        // 直线数量
        private NumericUpDown _nudLinesToFit = null!;
        
        // 圆孔筛选
        private NumericUpDown _nudRoundAspectMin = null!;
        private NumericUpDown _nudRoundSolidMin = null!;
        
        // 长圆孔筛选
        private NumericUpDown _nudSlotAspectMin = null!;
        private NumericUpDown _nudSlotAspectMax = null!;
        private NumericUpDown _nudSlotSolidMin = null!;
        private NumericUpDown _nudPixelToMmRatio = null!;

        // 操作按钮
        private Button _btnLoad = null!;
        private Button _btnDetect = null!;
        private Button _btnSetRef = null!;
        private Button _btnClearRef = null!;
        private Button _btnGrab = null!;
        
        private Label _lblRefStatus = null!;

        // ─────────────────────────── 状态 ───────────────────────────
        private string? _currentImagePath;
        private float? _referenceOffsetX;
        private float? _lastComputedCenterX;
        private Mat? _currentMat;
        
        // 多边形 ROI 状态
        private List<System.Drawing.Point> _roiPoints = new();
        private bool _isDrawingRoi = false;
        private System.Drawing.Point _mouseHoverPos;

        // 全局配置字典缓存
        private OffsetTestConfig _globalConfig = new();

        public Camera2DOffsetTestForm()
        {
            InitializeUi();
            LoadGlobalConfigFromFile();
            PopulateCameraCombo();
        }

        private void InitializeUi()
        {
            Text = "2D 立柱孔洞检测与偏移测试工具";
            Width = 1400;
            Height = 900;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(240, 242, 248);

            // 主分割：左侧(380px) 控制区 | 右侧 绘图与日志
            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 380,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(218, 220, 224)
            };
            Controls.Add(mainSplit);

            // ====== 左侧：控制面板 ======
            var leftPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true, BackColor = Color.White };
            mainSplit.Panel1.Controls.Add(leftPnl);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            leftPnl.Controls.Add(flow);

            int flowW = 340;

            // 0. 选择相机 SN
            flow.Controls.Add(SectionLabel("📷 选择调试目标相机", flowW));
            _cmbCamera = new ComboBox 
            { 
                Width = flowW - 20, 
                Height = 28, 
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _cmbCamera.SelectedIndexChanged += CmbCamera_SelectedIndexChanged;
            flow.Controls.Add(_cmbCamera);

            // 0.1 选择货架规格 (BeamLength)
            flow.Controls.Add(SectionLabel("📐 选择调试货架规格", flowW));
            _cmbBeamLength = new ComboBox
            {
                Width = flowW - 20,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            _cmbBeamLength.Items.Add("1380");
            _cmbBeamLength.Items.Add("2180");
            _cmbBeamLength.SelectedIndex = 0; // 默认为 1380
            _cmbBeamLength.SelectedIndexChanged += CmbBeamLength_SelectedIndexChanged;
            flow.Controls.Add(_cmbBeamLength);

            // 1. 图像载入与 ROI 绘制
            flow.Controls.Add(SectionLabel("📂 图像与 ROI 操作", flowW));
            
            var btnFlow1 = new FlowLayoutPanel { Width = flowW, Height = 45 };
            _btnLoad = new Button { Text = "打开图像...", Width = 160, Height = 36, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _btnLoad.Click += BtnLoad_Click;
            
            _btnDetect = new Button { Text = "开始检测", Width = 150, Height = 36, BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Enabled = false };
            _btnDetect.Click += BtnDetect_Click;
            
            btnFlow1.Controls.Add(_btnLoad);
            btnFlow1.Controls.Add(_btnDetect);
            flow.Controls.Add(btnFlow1);

            var btnFlow2 = new FlowLayoutPanel { Width = flowW, Height = 45 };
            var btnDrawRoi = new Button { Text = "✏️ 绘制多边形 ROI", Width = 160, Height = 36, BackColor = Color.FromArgb(100, 50, 150), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnDrawRoi.Click += (s, e) => {
                if (_pbImage.Image == null) { MessageBox.Show("请先打开图像或采集后再绘制 ROI"); return; }
                _isDrawingRoi = true;
                _roiPoints.Clear();
                _pbImage.Invalidate();
                AppendLog("✏️ 开始绘制 ROI：请在右侧大图上依次点击 4 个点绘制多边形（上宽下窄）...");
            };
            var btnClearRoi = new Button { Text = "❌ 清除 ROI", Width = 150, Height = 36, BackColor = Color.FromArgb(120, 120, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnClearRoi.Click += (s, e) => {
                _roiPoints.Clear();
                _isDrawingRoi = false;
                _pbImage.Invalidate();
                AppendLog("❌ ROI 范围已清除");
            };
            btnFlow2.Controls.Add(btnDrawRoi);
            btnFlow2.Controls.Add(btnClearRoi);
            flow.Controls.Add(btnFlow2);

            var btnFlow3 = new FlowLayoutPanel { Width = flowW, Height = 45 };
            var btnSaveParams = new Button { Text = "💾 保存当前参数", Width = 160, Height = 36, BackColor = Color.FromArgb(23, 162, 184), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSaveParams.Click += (s, e) => SaveCurrentCameraConfig();
            
            _btnGrab = new Button { Text = "📸 相机采集", Width = 150, Height = 36, BackColor = Color.FromArgb(111, 66, 193), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _btnGrab.Click += BtnGrab_Click;
            
            btnFlow3.Controls.Add(btnSaveParams);
            btnFlow3.Controls.Add(_btnGrab);
            flow.Controls.Add(btnFlow3);

            // 2. 二值化与面积参数
            flow.Controls.Add(SectionLabel("⚙️ 基础检测参数", flowW));
            
            flow.Controls.Add(PropRow("自适应阈值 C 值", _nudCValue = new NumericUpDown { Minimum = -50, Maximum = 100, Value = 15, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("自适应窗口 BlockSize", _nudBlockSize = new NumericUpDown { Minimum = 3, Maximum = 201, Value = 51, Increment = 2, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("最小面积 (px)", _nudMinArea = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = 30, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("最大面积 (px)", _nudMaxArea = new NumericUpDown { Minimum = 100, Maximum = 100000, Value = 8000, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("拟合直线数量", _nudLinesToFit = new NumericUpDown { Minimum = 1, Maximum = 4, Value = 1, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("像素/毫米比例 (Ratio)", _nudPixelToMmRatio = new NumericUpDown { Minimum = 0.0001M, Maximum = 10.0M, DecimalPlaces = 4, Increment = 0.001M, Value = 0.125M, Width = 80 }, flowW));

            // 3. 圆孔识别参数
            flow.Controls.Add(SectionLabel("⚪ 圆孔筛选阈值 (Round)", flowW));
            flow.Controls.Add(PropRow("最小宽高比 (Aspect)", _nudRoundAspectMin = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M, Value = 0.55M, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("最小饱满度 (Solidity)", _nudRoundSolidMin = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M, Value = 0.95M, Width = 80 }, flowW));

            // 4. 长圆孔/腰型孔识别参数
            flow.Controls.Add(SectionLabel("椭圆/长圆孔筛选阈值 (Slot)", flowW));
            flow.Controls.Add(PropRow("最小宽高比 (Aspect)", _nudSlotAspectMin = new NumericUpDown { Minimum = 0.01M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M, Value = 0.10M, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("最大宽高比 (Aspect)", _nudSlotAspectMax = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M, Value = 0.55M, Width = 80 }, flowW));
            flow.Controls.Add(PropRow("最小饱满度 (Solidity)", _nudSlotSolidMin = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, DecimalPlaces = 2, Increment = 0.05M, Value = 0.90M, Width = 80 }, flowW));

            // 5. 参考位置设置
            flow.Controls.Add(SectionLabel("📌 标准参考位置", flowW));
            _lblRefStatus = new Label { Text = "当前标准中心: 未设定 (将以图像几何中心为基准)", Width = flowW, Height = 36, ForeColor = Color.FromArgb(100, 100, 100), Font = new Font("Microsoft YaHei UI", 8.5F) };
            flow.Controls.Add(_lblRefStatus);

            var refBtnFlow = new FlowLayoutPanel { Width = flowW, Height = 45 };
            _btnSetRef = new Button { Text = "设为标准中心", Width = 160, Height = 34, BackColor = Color.FromArgb(180, 120, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Enabled = false };
            _btnSetRef.Click += BtnSetRef_Click;
            
            _btnClearRef = new Button { Text = "清除标准值", Width = 150, Height = 34, BackColor = Color.FromArgb(100, 100, 100), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _btnClearRef.Click += BtnClearRef_Click;
            
            refBtnFlow.Controls.Add(_btnSetRef);
            refBtnFlow.Controls.Add(_btnClearRef);
            flow.Controls.Add(refBtnFlow);

            // ====== 右侧：绘图展示与日志 ======
            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 620,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(218, 220, 224)
            };
            mainSplit.Panel2.Controls.Add(rightSplit);

            // 右侧上：图像展示 PictureBox
            var imgPnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 35), Padding = new Padding(4) };
            _pbImage = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 24)
            };
            
            // 绑定事件
            _pbImage.MouseDown += PbImage_MouseDown;
            _pbImage.MouseMove += PbImage_MouseMove;
            _pbImage.Paint += PbImage_Paint;

            imgPnl.Controls.Add(_pbImage);
            rightSplit.Panel1.Controls.Add(imgPnl);

            // 右侧下：日志文本框
            var logPnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.FromArgb(240, 242, 248) };
            var logTitle = new Label { Text = "🗋 运行与识别日志", Dock = DockStyle.Top, Height = 22, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(70, 80, 95) };
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 38),
                ForeColor = Color.FromArgb(200, 220, 200),
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.None
            };
            logPnl.Controls.Add(_txtLog);
            logPnl.Controls.Add(logTitle);
            rightSplit.Panel2.Controls.Add(logPnl);
        }

        private static Label SectionLabel(string text, int width) => new Label
        {
            Text = text,
            Width = width,
            Height = 28,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 102, 204),
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 16, 0, 6)
        };

        private static Panel PropRow(string labelText, System.Windows.Forms.Control inputControl, int width)
        {
            var pnl = new Panel { Width = width, Height = 28, Margin = new Padding(0, 2, 0, 2) };
            var lbl = new Label
            {
                Text = labelText,
                Width = width - 100,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(60, 60, 60),
                Dock = DockStyle.Left
            };
            inputControl.Dock = DockStyle.Right;
            pnl.Controls.Add(lbl);
            pnl.Controls.Add(inputControl);
            return pnl;
        }

        // ─────────────────────────── 相机 SN 初始化 ───────────────────────────

        private void PopulateCameraCombo()
        {
            _cmbCamera.Items.Clear();
            var cameras = ConfigManager.Instance?.Cameras;
            if (cameras == null) return;

            foreach (var c in cameras.Where(x =>
                          x.Type?.Contains("3D", StringComparison.OrdinalIgnoreCase) == true ||
                          x.Type?.Contains("2D", StringComparison.OrdinalIgnoreCase) == true ||
                          x.Type?.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) == true))
            {
                _cmbCamera.Items.Add($"{c.Name}  ({c.Sn})");
            }
            if (_cmbCamera.Items.Count > 0) _cmbCamera.SelectedIndex = 0;
        }

        private string? GetSelectedSn()
        {
            var txt = _cmbCamera.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(txt)) return null;
            int p0 = txt.LastIndexOf('('), p1 = txt.LastIndexOf(')');
            return (p0 >= 0 && p1 > p0) ? txt[(p0 + 1)..p1].Trim() : null;
        }

        private void CmbCamera_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn)) return;

            LoadCameraConfigFromCache(sn);
        }

        private void CmbBeamLength_SelectedIndexChanged(object? sender, EventArgs e)
        {
            string? sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn)) return;

            LoadCameraConfigFromCache(sn);
        }

        private int GetSelectedBeamLength()
        {
            if (_cmbBeamLength.SelectedItem?.ToString() == "2180")
                return 2180;
            return 1380;
        }

        // ─────────────────────────── 配置文件读写 ───────────────────────────

        private void LoadGlobalConfigFromFile()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<OffsetTestConfig>(json);
                    if (config != null)
                    {
                        _globalConfig = config;
                        _globalConfig.MigrateAll();
                        AppendLog($"✅ 成功载入本地历史配置文件，共包含 {_globalConfig.Cameras.Count} 台相机配置");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"⚠️ 载入全局配置文件失败，将使用优质默认值。错误: {ex.Message}");
            }
            _globalConfig = new OffsetTestConfig();
        }

        private void LoadCameraConfigFromCache(string sn)
        {
            if (_globalConfig.Cameras.TryGetValue(sn, out var param))
            {
                param.MigrateOldConfig();

                _nudCValue.Value = Math.Clamp(param.CValue, _nudCValue.Minimum, _nudCValue.Maximum);
                _nudBlockSize.Value = Math.Clamp(param.BlockSize, _nudBlockSize.Minimum, _nudBlockSize.Maximum);
                _nudMinArea.Value = Math.Clamp(param.MinArea, _nudMinArea.Minimum, _nudMinArea.Maximum);
                _nudMaxArea.Value = Math.Clamp(param.MaxArea, _nudMaxArea.Minimum, _nudMaxArea.Maximum);
                _nudLinesToFit.Value = Math.Clamp(param.LinesToFit, _nudLinesToFit.Minimum, _nudLinesToFit.Maximum);

                _nudRoundAspectMin.Value = Math.Clamp((decimal)param.RoundAspectMin, _nudRoundAspectMin.Minimum, _nudRoundAspectMin.Maximum);
                _nudRoundSolidMin.Value = Math.Clamp((decimal)param.RoundSolidMin, _nudRoundSolidMin.Minimum, _nudRoundSolidMin.Maximum);

                _nudSlotAspectMin.Value = Math.Clamp((decimal)param.SlotAspectMin, _nudSlotAspectMin.Minimum, _nudSlotAspectMin.Maximum);
                _nudSlotAspectMax.Value = Math.Clamp((decimal)param.SlotAspectMax, _nudSlotAspectMax.Minimum, _nudSlotAspectMax.Maximum);
                _nudSlotSolidMin.Value = Math.Clamp((decimal)param.SlotSolidMin, _nudSlotSolidMin.Minimum, _nudSlotSolidMin.Maximum);
                _nudPixelToMmRatio.Value = Math.Clamp((decimal)(param.PixelToMmRatio > 0 ? param.PixelToMmRatio : 0.125), _nudPixelToMmRatio.Minimum, _nudPixelToMmRatio.Maximum);

                int beam = GetSelectedBeamLength();
                if (beam == 2180)
                {
                    _roiPoints = param.RoiPoints2180 != null ? new List<System.Drawing.Point>(param.RoiPoints2180) : new List<System.Drawing.Point>();
                    _referenceOffsetX = param.ReferenceOffsetX2180;
                }
                else
                {
                    _roiPoints = param.RoiPoints1380 != null ? new List<System.Drawing.Point>(param.RoiPoints1380) : new List<System.Drawing.Point>();
                    _referenceOffsetX = param.ReferenceOffsetX1380;
                }

                if (_referenceOffsetX.HasValue)
                {
                    _lblRefStatus.Text = $"当前标准中心 ({beam}): {_referenceOffsetX.Value:F1} px (已锁定)";
                    _lblRefStatus.ForeColor = Color.FromArgb(180, 80, 0);
                }
                else
                {
                    _lblRefStatus.Text = $"当前标准中心 ({beam}): 未设定 (将以图像几何中心为基准)";
                    _lblRefStatus.ForeColor = Color.FromArgb(100, 100, 100);
                }

                _pbImage.Invalidate();
                AppendLog($"✅ 已载入相机 SN [{sn}] (规格 {beam}) 的专属参数和 ROI 设定！");
            }
            else
            {
                // 使用默认参数
                _nudCValue.Value = 15;
                _nudBlockSize.Value = 51;
                _nudMinArea.Value = 30;
                _nudMaxArea.Value = 8000;
                _nudLinesToFit.Value = 1;

                _nudRoundAspectMin.Value = 0.55M;
                _nudRoundSolidMin.Value = 0.95M;

                _nudSlotAspectMin.Value = 0.10M;
                _nudSlotAspectMax.Value = 0.55M;
                _nudSlotSolidMin.Value = 0.90M;
                _nudPixelToMmRatio.Value = 0.125M;

                _roiPoints.Clear();
                _referenceOffsetX = null;
                _lblRefStatus.Text = "当前标准中心: 未设定 (将以图像几何中心为基准)";
                _lblRefStatus.ForeColor = Color.FromArgb(100, 100, 100);

                _pbImage.Invalidate();
                AppendLog($"ℹ️ 未找到相机 SN [{sn}] 的专属参数，已应用推荐默认值");
            }
        }

        private void SaveCurrentCameraConfig()
        {
            string? sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn))
            {
                MessageBox.Show("未选择任何相机", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!_globalConfig.Cameras.TryGetValue(sn, out var param))
                {
                    param = new Camera2DOffsetParam();
                }

                param.CValue = (int)_nudCValue.Value;
                param.BlockSize = (int)_nudBlockSize.Value;
                param.MinArea = (int)_nudMinArea.Value;
                param.MaxArea = (int)_nudMaxArea.Value;
                param.LinesToFit = (int)_nudLinesToFit.Value;

                param.RoundAspectMin = (double)_nudRoundAspectMin.Value;
                param.RoundSolidMin = (double)_nudRoundSolidMin.Value;

                param.SlotAspectMin = (double)_nudSlotAspectMin.Value;
                param.SlotAspectMax = (double)_nudSlotAspectMax.Value;
                param.SlotSolidMin = (double)_nudSlotSolidMin.Value;
                param.PixelToMmRatio = (double)_nudPixelToMmRatio.Value;

                int beam = GetSelectedBeamLength();
                if (beam == 2180)
                {
                    param.RoiPoints2180 = new List<System.Drawing.Point>(_roiPoints);
                    param.ReferenceOffsetX2180 = _referenceOffsetX;
                }
                else
                {
                    param.RoiPoints1380 = new List<System.Drawing.Point>(_roiPoints);
                    param.ReferenceOffsetX1380 = _referenceOffsetX;
                }

                _globalConfig.Cameras[sn] = param;

                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_globalConfig, options);
                File.WriteAllText(ConfigPath, json);

                AppendLog($"💾 相机 SN [{sn}] (规格 {beam}) 参数与 ROI 设定已保存成功！");
                MessageBox.Show($"相机 [{sn}] (规格 {beam}) 参数保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"参数保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ─────────────────────────── 绘图与 ROI 坐标射影映射 ───────────────────────────

        private System.Drawing.Point MapToScreen(System.Drawing.Point realPt)
        {
            if (_pbImage.Image == null) return realPt;

            float scaleX = (float)_pbImage.Image.Width / _pbImage.Width;
            float scaleY = (float)_pbImage.Image.Height / _pbImage.Height;
            float scale = Math.Max(scaleX, scaleY);

            int imgW = (int)(_pbImage.Image.Width / scale);
            int imgH = (int)(_pbImage.Image.Height / scale);
            int offsetX = (_pbImage.Width - imgW) / 2;
            int offsetY = (_pbImage.Height - imgH) / 2;

            int screenX = (int)(realPt.X / scale + offsetX);
            int screenY = (int)(realPt.Y / scale + offsetY);
            return new System.Drawing.Point(screenX, screenY);
        }

        private System.Drawing.Point MapToReal(System.Drawing.Point screenPt)
        {
            if (_pbImage.Image == null) return screenPt;

            float scaleX = (float)_pbImage.Image.Width / _pbImage.Width;
            float scaleY = (float)_pbImage.Image.Height / _pbImage.Height;
            float scale = Math.Max(scaleX, scaleY);

            int imgW = (int)(_pbImage.Image.Width / scale);
            int imgH = (int)(_pbImage.Image.Height / scale);
            int offsetX = (_pbImage.Width - imgW) / 2;
            int offsetY = (_pbImage.Height - imgH) / 2;

            int realX = (int)((screenPt.X - offsetX) * scale);
            int realY = (int)((screenPt.Y - offsetY) * scale);

            realX = Math.Clamp(realX, 0, _pbImage.Image.Width);
            realY = Math.Clamp(realY, 0, _pbImage.Image.Height);

            return new System.Drawing.Point(realX, realY);
        }

        private void PbImage_MouseDown(object? sender, MouseEventArgs e)
        {
            if (!_isDrawingRoi || _pbImage.Image == null) return;

            if (e.Button == MouseButtons.Left)
            {
                var realPt = MapToReal(e.Location);
                _roiPoints.Add(realPt);
                AppendLog($"  • ROI 节点 {_roiPoints.Count}: ({realPt.X}, {realPt.Y}) px");
                
                if (_roiPoints.Count == 4)
                {
                    _isDrawingRoi = false;
                    AppendLog("✅ ROI 多边形绘制完成！将在下一次检测中生效。");
                }
                _pbImage.Invalidate();
            }
        }

        private void PbImage_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_pbImage.Image == null) return;
            
            _mouseHoverPos = MapToReal(e.Location);
            if (_isDrawingRoi && _roiPoints.Count > 0)
            {
                _pbImage.Invalidate();
            }
        }

        private void PbImage_Paint(object? sender, PaintEventArgs e)
        {
            if (_pbImage.Image == null) return;

            using var pen = new Pen(Color.FromArgb(0, 255, 255), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };
            using var brush = new SolidBrush(Color.FromArgb(30, 0, 255, 255));

            var screenPts = _roiPoints.Select(MapToScreen).ToArray();

            if (screenPts.Length > 0)
            {
                foreach (var pt in screenPts)
                {
                    e.Graphics.FillEllipse(Brushes.Red, pt.X - 5, pt.Y - 5, 10, 10);
                }

                for (int i = 0; i < screenPts.Length - 1; i++)
                {
                    e.Graphics.DrawLine(pen, screenPts[i], screenPts[i + 1]);
                }

                if (screenPts.Length == 4)
                {
                    e.Graphics.DrawLine(pen, screenPts[3], screenPts[0]);
                    
                    var path = new System.Drawing.Drawing2D.GraphicsPath();
                    path.AddPolygon(screenPts);
                    e.Graphics.FillPath(brush, path);
                }
                else if (_isDrawingRoi)
                {
                    var hoverScreen = MapToScreen(_mouseHoverPos);
                    e.Graphics.DrawLine(pen, screenPts.Last(), hoverScreen);
                }
            }
        }

        // ─────────────────────────── 按钮与相机事件 ───────────────────────────

        private void BtnLoad_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "图像文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*",
                Title = "请选择现场2D立柱图像"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                _currentImagePath = ofd.FileName;
                try
                {
                    _currentMat?.Dispose();
                    _currentMat = Cv2.ImRead(_currentImagePath, ImreadModes.Color);
                    if (_currentMat.Empty())
                    {
                        MessageBox.Show("图像载入失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _pbImage.Image?.Dispose();
                    _pbImage.Image = _currentMat.ToBitmap();
                    _btnDetect.Enabled = true;
                    _btnSetRef.Enabled = false;

                    AppendLog($"📂 载入图像成功: {Path.GetFileName(_currentImagePath)} ({_currentMat.Width}x{_currentMat.Height})");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"图像载入异常: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void BtnGrab_Click(object? sender, EventArgs e)
        {
            string? sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn))
            {
                MessageBox.Show("请先选择一个有效相机", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var camera = DeviceManager.GetCamera(sn);
            if (camera == null)
            {
                MessageBox.Show($"系统中未找到 SN 为 {sn} 的相机，请检查该设备是否在线连接。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _btnGrab.Enabled = false;
            _btnGrab.Text = "正在采集...";
            AppendLog($"📸 正在触发相机 [{sn}] 单帧采集彩色 2D 图像...");

            try
            {
                // 确保建立相机物理连接
                if (!camera.IsConnected)
                {
                    AppendLog("🔌 相机未就绪，正在尝试建立流物理连接...");
                    bool ok = await Task.Run(() => camera.Connect());
                    if (!ok) throw new Exception("相机物理连接失败。请检查网线及电源。");
                    AppendLog("🔌 相机已就绪！");
                }

                // 确保开启采集引擎
                if (!camera.IsCapturing)
                {
                    camera.StartGrabbing();
                }

                // 抓取帧
                object frameObj = await camera.GrabFrameAsync();
                if (frameObj is DepthFrameData frame)
                {
                    _currentMat?.Dispose();

                    // 优先提取 2D 彩色原始图像，若相机无 RGB 功能则使用 Pseudo-color depth 图像降级代替
                    Bitmap srcBmp = frame.ColorImage ?? frame.PreviewImage;
                    if (srcBmp != null)
                    {
                        _currentMat = srcBmp.ToMat();
                        _pbImage.Image?.Dispose();
                        _pbImage.Image = _currentMat.ToBitmap();
                        _btnDetect.Enabled = true;
                        _btnSetRef.Enabled = false;
                        
                        string frameType = frame.ColorImage != null ? "2D 真实彩色图" : "深度图(伪彩色映射)";
                        AppendLog($"✅ 图像单帧采集成功！分辨率: {srcBmp.Width}x{srcBmp.Height} ({frameType})");
                    }
                    else
                    {
                        throw new Exception("采集到的帧图像内容为空。");
                    }
                }
                else
                {
                    throw new Exception("接收到的帧对象类型不匹配。");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 图像采集失败: {ex.Message}");
                MessageBox.Show($"相机图像采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnGrab.Enabled = true;
                _btnGrab.Text = "📸 相机采集";
            }
        }

        private void BtnDetect_Click(object? sender, EventArgs e)
        {
            if (_currentMat == null || _currentMat.Empty()) return;

            try
            {
                RunHoleDetectionAndOffset();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 检测发生异常: {ex.Message}");
                MessageBox.Show($"检测失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSetRef_Click(object? sender, EventArgs e)
        {
            if (_lastComputedCenterX.HasValue)
            {
                _referenceOffsetX = _lastComputedCenterX.Value;
                _lblRefStatus.Text = $"当前标准中心: {_referenceOffsetX.Value:F1} px (已锁定)";
                _lblRefStatus.ForeColor = Color.FromArgb(180, 80, 0);
                AppendLog($"📌 已将当前中心 {_referenceOffsetX.Value:F2} px 设定为标准参考值");
                
                if (_currentMat != null) RunHoleDetectionAndOffset();
            }
        }

        private void BtnClearRef_Click(object? sender, EventArgs e)
        {
            _referenceOffsetX = null;
            _lblRefStatus.Text = "当前标准中心: 未设定 (将以图像几何中心为基准)";
            _lblRefStatus.ForeColor = Color.FromArgb(100, 100, 100);
            AppendLog("📌 标准参考值已清除，偏移计算将以图像几何中心为基准");
            
            if (_currentMat != null) RunHoleDetectionAndOffset();
        }

        // ─────────────────────────── 核心双模识别与 RANSAC 算法 ───────────────────────────

        private void RunHoleDetectionAndOffset()
        {
            if (_currentMat == null) return;

            using var srcCopy = _currentMat.Clone();
            using var gray = new Mat();
            Cv2.CvtColor(srcCopy, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

            using var binary = new Mat();
            int blockSize = (int)_nudBlockSize.Value;
            if (blockSize % 2 == 0) blockSize++; 
            int cVal = (int)_nudCValue.Value;
            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, blockSize, cVal);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);

            Cv2.FindContours(binary, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            AppendLog($"🔍 图像提取特征：发现图像中包含的原始二值化轮廓数 = {contours.Length}个");

            int minArea = (int)_nudMinArea.Value;
            int maxArea = (int)_nudMaxArea.Value;

            double roundAspectMin = (double)_nudRoundAspectMin.Value;
            double roundSolidMin = (double)_nudRoundSolidMin.Value;

            double slotAspectMin = (double)_nudSlotAspectMin.Value;
            double slotAspectMax = (double)_nudSlotAspectMax.Value;
            double slotSolidMin = (double)_nudSlotSolidMin.Value;

            var detectedRounds = new List<RotatedRect>();
            var detectedSlots = new List<RotatedRect>();

            foreach (var contour in contours)
            {
                if (contour.Length < 5) continue;

                double area = Cv2.ContourArea(contour);
                if (area < minArea || area > maxArea) continue;

                var ellipse = Cv2.FitEllipse(contour);

                if (_roiPoints.Count == 4)
                {
                    var pts = _roiPoints.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                    var pt = new OpenCvSharp.Point2f(ellipse.Center.X, ellipse.Center.Y);
                    if (Cv2.PointPolygonTest(pts, pt, false) < 0) continue;
                }

                double width = Math.Min(ellipse.Size.Width, ellipse.Size.Height);
                double height = Math.Max(ellipse.Size.Width, ellipse.Size.Height);
                double aspectRatio = width / height;

                var hull = Cv2.ConvexHull(contour);
                double hullArea = Cv2.ContourArea(hull);
                double solidity = hullArea > 0 ? area / hullArea : 0;

                bool isRound = (aspectRatio >= roundAspectMin && aspectRatio <= 1.0 && solidity >= roundSolidMin);
                bool isSlot = (aspectRatio >= slotAspectMin && aspectRatio <= slotAspectMax && solidity >= slotSolidMin);

                if (isRound) detectedRounds.Add(ellipse);
                else if (isSlot) detectedSlots.Add(ellipse);
            }

            var allCandidates = new List<RotatedRect>();
            allCandidates.AddRange(detectedRounds);
            allCandidates.AddRange(detectedSlots);

            AppendLog($"🔍 满足几何尺寸并进入 ROI 的候选孔洞: 圆孔={detectedRounds.Count}个, 腰型孔={detectedSlots.Count}个");

            if (allCandidates.Count == 0)
            {
                _pbImage.Image?.Dispose();
                _pbImage.Image = srcCopy.ToBitmap();
                throw new Exception("二值化或 ROI 范围内未检测到任何满足特征的孔洞候选！");
            }

            var remainingCandidates = new List<RotatedRect>(allCandidates);
            var detectedLines = new List<Line2D>();
            var lineInliersList = new List<List<RotatedRect>>();

            int maxLinesToFind = (int)_nudLinesToFit.Value;
            bool selectByRef = false;
            // 如果设定了标准参考值并且期望只拟合 1 条直线，我们拟合至多 2 条直线以便从中优选与标准值同一列的那条。
            if (maxLinesToFind == 1 && _referenceOffsetX.HasValue)
            {
                maxLinesToFind = 2;
                selectByRef = true;
            }

            int minInliers = 3;    
            double distanceThresh = 15.0; 

            var rng = new Random(42); 

            for (int lineIdx = 0; lineIdx < maxLinesToFind; lineIdx++)
            {
                if (remainingCandidates.Count < minInliers) break;

                var bestInliers = new List<RotatedRect>();
                double bestSlopeK = 0, bestInterceptC = 0;

                for (int iter = 0; iter < 300; iter++)
                {
                    int i1 = rng.Next(remainingCandidates.Count);
                    int i2 = rng.Next(remainingCandidates.Count);
                    if (i1 == i2) continue;

                    var p1 = remainingCandidates[i1].Center;
                    var p2 = remainingCandidates[i2].Center;

                    double dy = p2.Y - p1.Y;
                    if (Math.Abs(dy) < 10.0) continue; 

                    double k = (p2.X - p1.X) / dy;
                    if (Math.Abs(k) > 0.35) continue; 

                    double c = p1.X - k * p1.Y;

                    var inliers = new List<RotatedRect>();
                    foreach (var e in remainingCandidates)
                    {
                        double expectedX = k * e.Center.Y + c;
                        if (Math.Abs(e.Center.X - expectedX) < distanceThresh)
                        {
                            inliers.Add(e);
                        }
                    }

                    if (inliers.Count > bestInliers.Count)
                    {
                        bestInliers = inliers;
                        bestSlopeK = k;
                        bestInterceptC = c;
                    }
                }

                if (bestInliers.Count >= minInliers)
                {
                    var linePts = bestInliers.Select(e => new Point2f(e.Center.X, e.Center.Y)).ToArray();
                    var fittedLine = Cv2.FitLine(linePts, DistanceTypes.L2, 0, 0.01, 0.01);
                    
                    detectedLines.Add(fittedLine);
                    lineInliersList.Add(bestInliers);

                    foreach (var inlier in bestInliers)
                    {
                        remainingCandidates.Remove(inlier);
                    }
                }
                else
                {
                    break;
                }
            }

            AppendLog($"📊 RANSAC 列拟合完成，共找到垂直列: {detectedLines.Count}条");

            if (detectedLines.Count == 0)
            {
                _pbImage.Image?.Dispose();
                _pbImage.Image = srcCopy.ToBitmap();
                throw new Exception("未检测到任何满足垂直对齐的孔洞列，请调整筛选参数！");
            }

            float midY = srcCopy.Height / 2f;
            var lineInfos = new List<(Line2D line, double xAtMid, List<RotatedRect> inliers)>();
            for (int i = 0; i < detectedLines.Count; i++)
            {
                var line = detectedLines[i];
                double k = line.Vx / line.Vy;
                double c = line.X1 - k * line.Y1;
                double xAtMid = k * midY + c;
                lineInfos.Add((line, xAtMid, lineInliersList[i]));
            }

            lineInfos = lineInfos.OrderBy(item => item.xAtMid).ToList();

            float actualCenterX = 0;
            if (selectByRef && _referenceOffsetX.HasValue && lineInfos.Count > 1)
            {
                // 按与参考值的偏差绝对值升序排序，选择最接近参考值的那一条
                lineInfos = lineInfos.OrderBy(item => Math.Abs(item.xAtMid - _referenceOffsetX.Value)).ToList();
                actualCenterX = (float)lineInfos[0].xAtMid;
                AppendLog($"📊 检测到多条列，已选择最靠近标准值 ({_referenceOffsetX.Value:F1} px) 的列，中心为: {actualCenterX:F1} px");

                // 将舍弃的直线的内点重新放回 remainingCandidates，使其能够作为背景噪点（灰色圈）绘制
                for (int i = 1; i < lineInfos.Count; i++)
                {
                    remainingCandidates.AddRange(lineInfos[i].inliers);
                }
                // 只保留最接近的那一条直线
                lineInfos = new List<(Line2D line, double xAtMid, List<RotatedRect> inliers)> { lineInfos[0] };
            }
            else if (lineInfos.Count == 1)
            {
                actualCenterX = (float)lineInfos[0].xAtMid;
                AppendLog($"📊 检测到 1 条立柱列中心: {actualCenterX:F1} px");
            }
            else
            {
                double leftX = lineInfos[0].xAtMid;
                double rightX = lineInfos[lineInfos.Count - 1].xAtMid;
                actualCenterX = (float)((leftX + rightX) / 2.0);
                AppendLog($"⚖️ 检测到 {lineInfos.Count} 条孔洞列，以两侧列中点 (左={leftX:F1}, 右={rightX:F1}) 均值 {actualCenterX:F1} px 为偏移基准");
            }

            _lastComputedCenterX = actualCenterX;
            _btnSetRef.Enabled = true;

            float offset = 0;
            if (_referenceOffsetX.HasValue)
            {
                offset = actualCenterX - _referenceOffsetX.Value;
            }
            else
            {
                float imgCenter = srcCopy.Width / 2f;
                offset = actualCenterX - imgCenter;
            }

            double ratio = (double)_nudPixelToMmRatio.Value;
            double offsetMm = offset * ratio;

            foreach (var e in remainingCandidates)
            {
                Cv2.Ellipse(srcCopy, e, Scalar.Gray, 1);
            }

            Scalar[] colors = new[] { Scalar.Red, Scalar.Orange, Scalar.Magenta, Scalar.Lime };
            for (int i = 0; i < lineInfos.Count; i++)
            {
                var info = lineInfos[i];
                var color = colors[i % colors.Length];

                foreach (var e in info.inliers)
                {
                    Cv2.Ellipse(srcCopy, e, color, 3);
                }

                var line = info.line;
                double k = line.Vx / line.Vy;
                double c = line.X1 - k * line.Y1;

                double minY = info.inliers.Min(e => (double)e.Center.Y);
                double maxY = info.inliers.Max(e => (double)e.Center.Y);

                minY = Math.Max(0, minY - 15);
                maxY = Math.Min(srcCopy.Height, maxY + 15);

                Point2f pt1 = new Point2f((float)(k * minY + c), (float)minY);
                Point2f pt2 = new Point2f((float)(k * maxY + c), (float)maxY);
                Cv2.Line(srcCopy, new OpenCvSharp.Point((int)pt1.X, (int)pt1.Y), new OpenCvSharp.Point((int)pt2.X, (int)pt2.Y), color, 3, LineTypes.AntiAlias);
            }

            if (lineInfos.Count > 1)
            {
                Cv2.Line(srcCopy, new OpenCvSharp.Point((int)actualCenterX, 0), new OpenCvSharp.Point((int)actualCenterX, srcCopy.Height), Scalar.Yellow, 2, LineTypes.AntiAlias);

                if (_referenceOffsetX.HasValue)
                {
                    Cv2.Line(srcCopy, new OpenCvSharp.Point((int)_referenceOffsetX.Value, 0), new OpenCvSharp.Point((int)_referenceOffsetX.Value, srcCopy.Height), Scalar.Purple, 2, LineTypes.Link4);
                }
                else
                {
                    Cv2.Line(srcCopy, new OpenCvSharp.Point((int)(srcCopy.Width / 2f), 0), new OpenCvSharp.Point((int)(srcCopy.Width / 2f), srcCopy.Height), Scalar.Blue, 2, LineTypes.Link4);
                }
            }

            _pbImage.Image?.Dispose();
            var bmp = srcCopy.ToBitmap();

            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                
                using (var titleFont = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold))
                using (var descFont = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold))
                using (var bgBrush = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                {
                    string refMode = _referenceOffsetX.HasValue ? "基于标准值" : "基于画面中心";
                    string infoText = $"偏差: {offsetMm:F2} mm ({refMode})";
                    string descText = $"RANSAC 提取孔洞 (比例: {ratio:F4} mm/px，彩色圈为对应列特征)";

                    g.FillRectangle(bgBrush, 30, 20, 1100, 100);
                    g.DrawString(infoText, titleFont, Brushes.Cyan, 40, 25);
                    g.DrawString(descText, descFont, Brushes.White, 40, 75);
                }
            }

            _pbImage.Image = bmp;
            AppendLog($"📊 计算中心位置: {actualCenterX:F1} px. 偏移结果: {offset:F2} px ({offsetMm:F2} mm)");
        }

        private void AppendLog(string msg)
        {
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }
    }
}
