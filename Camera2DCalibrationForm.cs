using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using material_box_storage_detection_system_Net.Config;
using OpenCvSharp;
using WinForms = System.Windows.Forms;

// 明确别名消除歧义
using DrawPoint = System.Drawing.Point;
using DrawSize  = System.Drawing.Size;
using DrawRect  = System.Drawing.Rectangle;

namespace material_box_storage_detection_system_Net
{
    /// <summary>
    /// 2D 相机（海康）棋盘格内参标定工具。
    /// 流程：选相机 SN → 加载图片文件夹 → 输入标定板参数 → 执行标定 → 保存结果。
    /// 布局：左侧(62%) 为图像预览+文件列表+翻页；右侧(38%) 为参数卡+日志+结果。
    /// </summary>
    public class Camera2DCalibrationForm : Form
    {
        // ─────────────────────────── 控件 ───────────────────────────
        private WinForms.ComboBox     _cmbCamera   = null!;
        private Button                _btnLoadDir  = null!;
        private Label                 _lblDirPath  = null!;
        private Label                 _lblImgIndex = null!;
        private Button                _btnPrev     = null!;
        private Button                _btnNext     = null!;
        private PictureBox            _pbPreview   = null!;
        private WinForms.ListBox      _lstFiles    = null!;

        // 标定板参数
        private WinForms.NumericUpDown _nudCols   = null!;
        private WinForms.NumericUpDown _nudRows   = null!;
        private WinForms.TextBox       _txtSquare = null!;

        // 操作按钮
        private Button _btnCalibrate = null!;
        private Button _btnSave      = null!;
        private Button _btnClear     = null!;
        private Button _btnTestOffset = null!;

        private Button _btnCaptureImage = null!;
        private Button _btnDrawRoiLeft = null!;
        private Button _btnDrawRoiRight = null!;
        private Button _btnSaveRoiRef = null!;

        private bool _isDrawingLeftRoi = false;
        private bool _isDrawingRightRoi = false;
        private List<DrawPoint> _roiLeft = new();
        private List<DrawPoint> _roiRight = new();
        private float? _referenceOffsetX = null;
        private List<DrawPoint> _currentDrawingPoints = new();
        private DrawPoint _mouseHoverPos;

        // 结果区 & 日志区
        private WinForms.TextBox _txtResult = null!;
        private WinForms.ListBox _lstLog    = null!;

        // 状态标签
        private Label _lblStatus = null!;

        // ─────────────────────────── 状态 ───────────────────────────
        private List<string> _imageFiles  = new();
        private int          _currentIdx  = -1;
        private Bitmap?      _currentBmp;
        private List<string> _validImages = new();

        // 标定结果缓存
        private double[,]? _cameraMatrix;
        private double[]?  _distCoeffs;
        private double      _rmsError;
        private string      _calibratedSn = "";

        // 已找到的角点（用于叠加绘制）
        private List<Point2f>? _lastCorners;

        // 支持的图片扩展名
        private static readonly string[] ImgExts =
            { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif" };

        // ═══════════════════════════════════════════════════════════════
        public Camera2DCalibrationForm()
        {
            InitializeUi();
            PopulateCameraCombo();
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI 构建
        // ═══════════════════════════════════════════════════════════════
        private void InitializeUi()
        {
            Text            = "2D 相机内参标定工具";
            MinimumSize     = new DrawSize(1280, 800);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState     = FormWindowState.Maximized;
            BackColor       = Color.FromArgb(240, 242, 248);
            AutoScaleMode   = AutoScaleMode.None;

            // 根布局：左(62%) 右(38%)
            var root = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 2,
                RowCount    = 1,
                Padding     = new Padding(16, 12, 16, 12),
                BackColor   = Color.FromArgb(240, 242, 248),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            Controls.Add(root);

            root.Controls.Add(BuildLeftPanel(),  0, 0);
            root.Controls.Add(BuildRightPanel(), 1, 0);
        }

        // ─────────────────────────────────────────────────────────────
        //  左侧面板：图像查看器
        // ─────────────────────────────────────────────────────────────
        private Panel BuildLeftPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0) };

            var layout = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 4,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));   // 标题
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 预览图
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));  // 文件列表
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));   // 翻页导航
            panel.Controls.Add(layout);

            // 行0: 标题
            layout.Controls.Add(new Label
            {
                Text      = "📷  图像预览",
                Font      = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 45, 72),
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            // 行1: 预览图
            var previewContainer = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 22, 30),
                Margin    = new Padding(0, 4, 0, 4),
            };
            _pbPreview = new PictureBox
            {
                Dock      = DockStyle.Fill,
                SizeMode  = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 22, 30),
            };
            _pbPreview.Paint += PbPreview_Paint;
            _pbPreview.MouseDown += PbPreview_MouseDown;
            _pbPreview.MouseMove += PbPreview_MouseMove;
            _pbPreview.MouseUp += PbPreview_MouseUp;
            previewContainer.Controls.Add(_pbPreview);
            layout.Controls.Add(previewContainer, 0, 1);

            // 行2: 文件列表
            var listContainer = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 38),
                Margin    = new Padding(0, 0, 0, 4),
            };
            _lstFiles = new WinForms.ListBox
            {
                Dock                = DockStyle.Fill,
                Font                = new Font("Consolas", 9F),
                BackColor           = Color.FromArgb(28, 30, 38),
                ForeColor           = Color.FromArgb(180, 210, 240),
                BorderStyle         = BorderStyle.None,
                HorizontalScrollbar = true,
                SelectionMode       = WinForms.SelectionMode.One,
            };
            _lstFiles.SelectedIndexChanged += LstFiles_SelectedIndexChanged;
            listContainer.Controls.Add(_lstFiles);
            layout.Controls.Add(listContainer, 0, 2);

            // 行3: 翻页导航
            var navBar = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 4,
                RowCount    = 1,
                BackColor   = Color.Transparent,
                Margin      = new Padding(0),
            };
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));

            _btnPrev = MakeNavBtn("◀  上一张", Color.FromArgb(70, 80, 110));
            _btnPrev.Click += (_, __) => Navigate(-1);
            navBar.Controls.Add(_btnPrev, 0, 0);

            _btnNext = MakeNavBtn("下一张  ▶", Color.FromArgb(70, 80, 110));
            _btnNext.Click += (_, __) => Navigate(+1);
            navBar.Controls.Add(_btnNext, 1, 0);

            _lblImgIndex = new Label
            {
                Text      = "未加载图片",
                Font      = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(100, 110, 130),
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            navBar.Controls.Add(_lblImgIndex, 2, 0);

            _lblStatus = new Label
            {
                Text      = "",
                Font      = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 110),
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            navBar.Controls.Add(_lblStatus, 3, 0);

            layout.Controls.Add(navBar, 0, 3);
            return panel;
        }

        // ─────────────────────────────────────────────────────────────
        //  右侧面板：参数 + 日志 + 结果
        // ─────────────────────────────────────────────────────────────
        private Panel BuildRightPanel()
        {
            var outer = new Panel
            {
                Dock      = DockStyle.Fill,
                Padding   = new Padding(8, 0, 0, 0),
                BackColor = Color.Transparent,
            };

            var layout = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 5,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));   // 标题
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 400F));  // 参数卡
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 按钮行
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));  // 日志
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 结果
            outer.Controls.Add(layout);

            // 行0: 标题
            layout.Controls.Add(new Label
            {
                Text      = "⚙  标定参数",
                Font      = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 45, 72),
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            // 行1: 参数卡
            layout.Controls.Add(BuildParamCard(), 0, 1);

            // 行2: 操作按钮
            layout.Controls.Add(BuildActionBar(), 0, 2);

            // 行3: 日志
            var logCard = BuildDarkCard("📋  操作日志");
            _lstLog = new WinForms.ListBox
            {
                Dock                = DockStyle.Fill,
                Font                = new Font("Consolas", 8.5F),
                BackColor           = Color.FromArgb(24, 26, 34),
                ForeColor           = Color.FromArgb(160, 200, 130),
                BorderStyle         = BorderStyle.None,
                HorizontalScrollbar = true,
            };
            logCard.Controls.Add(_lstLog);
            layout.Controls.Add(logCard, 0, 3);

            // 行4: 结果
            var resultCard = BuildDarkCard("📊  标定结果");
            _txtResult = new WinForms.TextBox
            {
                Dock        = DockStyle.Fill,
                Multiline   = true,
                ReadOnly    = true,
                ScrollBars  = WinForms.ScrollBars.Both,
                Font        = new Font("Consolas", 8.5F),
                BackColor   = Color.FromArgb(24, 26, 34),
                ForeColor   = Color.FromArgb(0, 220, 180),
                WordWrap    = false,
                BorderStyle = BorderStyle.None,
            };
            resultCard.Controls.Add(_txtResult);
            layout.Controls.Add(resultCard, 0, 4);

            return outer;
        }

        // ─────────────────────── 参数卡 ───────────────────────────────
        private Panel BuildParamCard()
        {
            var card = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.White,
                Margin    = new Padding(0, 0, 0, 8),
            };
            card.Paint += DrawCardBorder;

            int y = 10;
            const int LW = 190;   // label宽（足够显示中文标签）
            const int CW = 280;   // control宽（足够显示SN+下拉内容）
            const int RH = 42;    // 行高

            var font     = new Font("Microsoft YaHei UI", 10F);
            var boldFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);

            void AddSectionTitle(string text)
            {
                card.Controls.Add(new Label
                {
                    Text      = text,
                    Font      = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(0, 112, 192),
                    Location  = new DrawPoint(12, y),
                    AutoSize  = true,
                });
                y += 22;
                card.Controls.Add(new Panel
                {
                    Location  = new DrawPoint(12, y),
                    Size      = new DrawSize(LW + CW + 30, 1),
                    BackColor = Color.FromArgb(220, 225, 235),
                });
                y += 6;
            }

            void AddRow(string labelText, WinForms.Control ctrl)
            {
                ctrl.Font     = font;
                ctrl.Location = new DrawPoint(LW + 14, y);
                ctrl.Width    = CW;
                card.Controls.Add(new Label
                {
                    Text      = labelText,
                    Font      = font,
                    ForeColor = Color.FromArgb(60, 70, 90),
                    Location  = new DrawPoint(12, y + 4),
                    AutoSize  = true,
                });
                card.Controls.Add(ctrl);
                y += RH;
            }

            // ── 区域一：相机 ──
            AddSectionTitle("▶  相机选择");
            _cmbCamera = new WinForms.ComboBox { DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            _cmbCamera.SelectedIndexChanged += CmbCamera_SelectedIndexChanged;
            AddRow("相机 SN", _cmbCamera);
            y += 4;

            _btnCaptureImage = new Button
            {
                Text      = "📷 采集当前图像",
                Font      = boldFont,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 160, 120),
                FlatStyle = FlatStyle.Flat,
                Height    = 36,
                Width     = CW,
                Location  = new DrawPoint(LW + 14, y),
                Cursor    = Cursors.Hand,
            };
            _btnCaptureImage.FlatAppearance.BorderSize = 0;
            _btnCaptureImage.Click += BtnCaptureImage_Click;
            card.Controls.Add(_btnCaptureImage);
            y += 42;

            // ── 区域二：图片文件夹 ──
            AddSectionTitle("▶  标定图片");
            _btnLoadDir = new Button
            {
                Text      = "📂  加载图片文件夹",
                Font      = boldFont,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 122, 204),
                FlatStyle = FlatStyle.Flat,
                Height    = 36,
                Width     = CW,
                Location  = new DrawPoint(LW + 14, y),
                Cursor    = Cursors.Hand,
            };
            _btnLoadDir.FlatAppearance.BorderSize = 0;
            _btnLoadDir.Click += BtnLoadDir_Click;
            card.Controls.Add(_btnLoadDir);
            y += 42;

            _lblDirPath = new Label
            {
                Text      = "（未选择文件夹）",
                Font      = new Font("Consolas", 8.5F),
                ForeColor = Color.FromArgb(120, 130, 150),
                Location  = new DrawPoint(12, y),
                Size      = new DrawSize(LW + CW + 30, 20),
            };
            card.Controls.Add(_lblDirPath);
            y += 26;

            // ── 区域三：棋盘格参数 ──
            AddSectionTitle("▶  棋盘格参数");

            _nudCols = new WinForms.NumericUpDown { Minimum = 2, Maximum = 30, Value = 9, DecimalPlaces = 0 };
            AddRow("角点列数 (宽)", _nudCols);

            _nudRows = new WinForms.NumericUpDown { Minimum = 2, Maximum = 30, Value = 6, DecimalPlaces = 0 };
            AddRow("角点行数 (高)", _nudRows);

            _txtSquare = new WinForms.TextBox { Text = "25.0" };
            AddRow("方格边长 (mm)", _txtSquare);

            return card;
        }

        // 操作按钮行
        private Panel BuildActionBar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor     = Color.Transparent,
                WrapContents  = true,
                Padding       = new Padding(0, 4, 0, 4),
            };

            _btnCalibrate = MakeAccentBtn("🎯  执行标定", Color.FromArgb(0, 153, 136));
            _btnCalibrate.Enabled = false;
            _btnCalibrate.Click += BtnCalibrate_Click;
            bar.Controls.Add(_btnCalibrate);

            _btnSave = MakeAccentBtn("💾  保存结果", Color.FromArgb(0, 100, 200));
            _btnSave.Enabled = false;
            _btnSave.Click += BtnSave_Click;
            bar.Controls.Add(_btnSave);

            _btnTestOffset = MakeAccentBtn("🔍  偏移检测(测试)", Color.FromArgb(100, 50, 150));
            _btnTestOffset.Enabled = false;
            _btnTestOffset.Click += BtnTestOffset_Click;
            bar.Controls.Add(_btnTestOffset);

            _btnDrawRoiLeft = MakeAccentBtn("✏  框选左侧 ROI", Color.FromArgb(0, 122, 204));
            _btnDrawRoiLeft.Enabled = false;
            _btnDrawRoiLeft.Click += (s, e) => { _isDrawingLeftRoi = true; _isDrawingRightRoi = false; AppendLog("✏ 请在左侧大图上依次点击4个点绘制左侧 ROI 多边形..."); };
            bar.Controls.Add(_btnDrawRoiLeft);

            _btnDrawRoiRight = MakeAccentBtn("✏  框选右侧 ROI", Color.FromArgb(0, 160, 100));
            _btnDrawRoiRight.Enabled = false;
            _btnDrawRoiRight.Click += (s, e) => { _isDrawingRightRoi = true; _isDrawingLeftRoi = false; AppendLog("✏ 请在左侧大图上依次点击4个点绘制右侧 ROI 多边形..."); };
            bar.Controls.Add(_btnDrawRoiRight);

            _btnSaveRoiRef = MakeAccentBtn("💾 保存基准与ROI", Color.FromArgb(200, 100, 0));
            _btnSaveRoiRef.Enabled = false;
            _btnSaveRoiRef.Click += BtnSaveRoiRef_Click;
            bar.Controls.Add(_btnSaveRoiRef);

            _btnClear = MakeAccentBtn("🗑  清除", Color.FromArgb(160, 60, 60));
            _btnClear.Click += BtnClear_Click;
            bar.Controls.Add(_btnClear);

            return bar;
        }

        // 深色小节卡片（带标题栏）
        private static Panel BuildDarkCard(string title)
        {
            var card = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 24, 32),
                Margin    = new Padding(0, 0, 0, 8),
            };
            card.Controls.Add(new Label
            {
                Text      = title,
                Dock      = DockStyle.Top,
                Height    = 26,
                Font      = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 200, 230),
                BackColor = Color.FromArgb(35, 40, 55),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(8, 0, 0, 0),
            });
            return card;
        }

        // ─────────────────────────── 辅助工厂 ────────────────────────
        private static Button MakeNavBtn(string text, Color color)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.White,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Size      = new DrawSize(130, 44),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 4, 6, 4),
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static Button MakeAccentBtn(string text, Color color)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Size      = new DrawSize(210, 44),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 0, 12, 0),
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static void DrawCardBorder(object? sender, PaintEventArgs e)
        {
            if (sender is not Panel p) return;
            var r = p.ClientRectangle;
            r.Width--; r.Height--;
            using var pen = new Pen(Color.FromArgb(200, 210, 225));
            e.Graphics.DrawRectangle(pen, r);
        }

        // ═══════════════════════════════════════════════════════════════
        //  相机下拉列表
        // ═══════════════════════════════════════════════════════════════
        private void PopulateCameraCombo()
        {
            _cmbCamera.Items.Clear();
            var cameras = ConfigManager.Instance?.Cameras;
            if (cameras == null) return;

            foreach (var c in cameras.Where(x =>
                         x.Type?.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) == true ||
                         x.Type?.Contains("2D", StringComparison.OrdinalIgnoreCase) == true))
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

        // ═══════════════════════════════════════════════════════════════
        //  加载图片文件夹
        // ═══════════════════════════════════════════════════════════════
        private void BtnLoadDir_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description         = "选择包含标定板图片的文件夹",
                ShowNewFolderButton = false,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            string dir = dlg.SelectedPath;
            _lblDirPath.Text = dir.Length > 55 ? "..." + dir[^52..] : dir;

            _imageFiles = Directory.EnumerateFiles(dir)
                .Where(f => ImgExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            _lstFiles.Items.Clear();
            _validImages.Clear();

            if (_imageFiles.Count == 0)
            {
                _lblImgIndex.Text     = "文件夹中没有图片文件";
                _btnCalibrate.Enabled = false;
                AppendLog($"❌ 未找到图片：{dir}");
                return;
            }

            foreach (var f in _imageFiles)
                _lstFiles.Items.Add($"[ ] {Path.GetFileName(f)}");

            AppendLog($"✅ 加载 {_imageFiles.Count} 张图片：{dir}");
            _currentIdx = -1;
            LoadImage(0);
            _btnCalibrate.Enabled = true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  图像显示与翻页
        // ═══════════════════════════════════════════════════════════════
        private void LoadImage(int index)
        {
            if (index < 0 || index >= _imageFiles.Count) return;
            _currentIdx = index;
            _lastCorners = null;

            try
            {
                _currentBmp?.Dispose();
                _currentBmp      = new Bitmap(_imageFiles[index]);
                _pbPreview.Image = _currentBmp;
                _pbPreview.Invalidate();

                _lblImgIndex.Text = $"{index + 1} / {_imageFiles.Count}  —  {Path.GetFileName(_imageFiles[index])}";
                _lblStatus.Text   = _validImages.Contains(_imageFiles[index]) ? "✔ 已验证角点" : "";

                if (_lstFiles.Items.Count > index && _lstFiles.SelectedIndex != index)
                    _lstFiles.SelectedIndex = index;
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ 加载图片失败: {ex.Message}");
            }
        }

        private void Navigate(int delta)
        {
            if (_imageFiles.Count == 0) return;
            int next = Math.Clamp(_currentIdx + delta, 0, _imageFiles.Count - 1);
            if (next == _currentIdx) return;
            LoadImage(next);
        }

        private void LstFiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_lstFiles.SelectedIndex >= 0)
                LoadImage(_lstFiles.SelectedIndex);
        }

        // 叠加绘制
        private void PbPreview_Paint(object? sender, PaintEventArgs e)
        {
            if (_pbPreview.Image == null) return;

            var img  = _pbPreview.Image;
            var ctrl = _pbPreview.ClientSize;
            double scaleX = (double)img.Width  / ctrl.Width;
            double scaleY = (double)img.Height / ctrl.Height;
            double scale  = Math.Max(scaleX, scaleY);
            int offX = (int)((ctrl.Width  - img.Width  / scale) / 2);
            int offY = (int)((ctrl.Height - img.Height / scale) / 2);

            // 绘制角点
            if (_lastCorners != null && _lastCorners.Count > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(210, Color.Lime));
                foreach (var pt in _lastCorners)
                {
                    float cx = (float)(pt.X / scale + offX);
                    float cy = (float)(pt.Y / scale + offY);
                    e.Graphics.FillEllipse(brush, cx - 3.5f, cy - 3.5f, 7, 7);
                }
            }

            // 绘制 ROI 辅助方法
            DrawPoint MapToScreen(DrawPoint pt)
            {
                return new DrawPoint((int)(pt.X / scale + offX), (int)(pt.Y / scale + offY));
            }

            void DrawPoly(List<DrawPoint> pts, Color color, int thickness = 2)
            {
                if (pts == null || pts.Count == 0) return;
                using var pen = new Pen(color, thickness);
                var screenPts = pts.Select(MapToScreen).ToArray();
                if (screenPts.Length > 1)
                {
                    for (int i = 0; i < screenPts.Length - 1; i++)
                        e.Graphics.DrawLine(pen, screenPts[i], screenPts[i + 1]);
                    
                    if (screenPts.Length == 4)
                        e.Graphics.DrawLine(pen, screenPts[3], screenPts[0]);
                }
                foreach (var pt in screenPts)
                    e.Graphics.FillRectangle(new SolidBrush(color), pt.X - 3, pt.Y - 3, 6, 6);
            }

            DrawPoly(_roiLeft, Color.Blue, 3);
            DrawPoly(_roiRight, Color.Green, 3);

            if (_isDrawingLeftRoi || _isDrawingRightRoi)
            {
                var color = _isDrawingLeftRoi ? Color.Blue : Color.Green;
                DrawPoly(_currentDrawingPoints, color, 2);
                
                if (_currentDrawingPoints.Count > 0 && _currentDrawingPoints.Count < 4)
                {
                    var lastPt = MapToScreen(_currentDrawingPoints.Last());
                    var hoverPt = MapToScreen(_mouseHoverPos);
                    using var pen = new Pen(color, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    e.Graphics.DrawLine(pen, lastPt, hoverPt);
                    if (_currentDrawingPoints.Count == 3)
                        e.Graphics.DrawLine(pen, hoverPt, MapToScreen(_currentDrawingPoints.First()));
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  执行标定
        // ═══════════════════════════════════════════════════════════════
        private async void BtnCalibrate_Click(object? sender, EventArgs e)
        {
            if (_imageFiles.Count == 0)
            {
                MessageBox.Show("请先加载标定图片文件夹！", "提示");
                return;
            }
            if (!double.TryParse(_txtSquare.Text, out double squareMm) || squareMm <= 0)
            {
                MessageBox.Show("方格边长输入无效，请输入正数（单位 mm）。", "参数错误");
                return;
            }

            int cols = (int)_nudCols.Value;
            int rows = (int)_nudRows.Value;

            SetBusy(true);
            _validImages.Clear();
            _txtResult.Text = "";

            // 重置列表图标
            for (int i = 0; i < _lstFiles.Items.Count; i++)
                _lstFiles.Items[i] = $"[ ] {Path.GetFileName(_imageFiles[i])}";

            AppendLog($"━━━━ 开始标定  棋盘格: {cols}×{rows}  方格: {squareMm}mm ━━━━");

            var patternSize = new OpenCvSharp.Size(cols, rows);
            var objPts      = new List<Mat>();
            var imgPts      = new List<Mat>();
            OpenCvSharp.Size? imgSize = null;

            // 物体坐标模板（Z=0 平面）
            var objTemplate = new Point3f[cols * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    objTemplate[r * cols + c] = new Point3f(c * (float)squareMm, r * (float)squareMm, 0f);

            int successCount = 0;

            await Task.Run(() =>
            {
                for (int i = 0; i < _imageFiles.Count; i++)
                {
                    string fp = _imageFiles[i];
                    try
                    {
                        using var src = Cv2.ImRead(fp, ImreadModes.Grayscale);
                        if (src.Empty())
                        {
                            BeginInvoke(new Action(() =>
                                AppendLog($"  ⚠ 跳过（无法读取）: {Path.GetFileName(fp)}")));
                            continue;
                        }

                        imgSize ??= src.Size();

                        bool found = Cv2.FindChessboardCorners(src, patternSize,
                            out Point2f[] corners,
                            ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage);

                        if (found && corners.Length == cols * rows)
                        {
                            var criteria = new TermCriteria(
                                CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);
                            Cv2.CornerSubPix(src, corners,
                                new OpenCvSharp.Size(11, 11),
                                new OpenCvSharp.Size(-1, -1), criteria);

                            objPts.Add(Mat.FromArray(objTemplate));
                            imgPts.Add(Mat.FromArray(corners));
                            successCount++;

                            var cornersCopy = corners.ToList();
                            int captIdx     = i;
                            BeginInvoke(new Action(() =>
                            {
                                _validImages.Add(fp);
                                _lstFiles.Items[captIdx] = $"[✔] {Path.GetFileName(fp)}";
                                AppendLog($"  ✅ {Path.GetFileName(fp)} — 找到 {corners.Length} 个角点");
                                if (_currentIdx == captIdx)
                                {
                                    _lastCorners = cornersCopy;
                                    _pbPreview.Invalidate();
                                    _lblStatus.Text = "✔ 已验证角点";
                                }
                            }));
                        }
                        else
                        {
                            int failIdx = i;
                            BeginInvoke(new Action(() =>
                            {
                                _lstFiles.Items[failIdx] = $"[✗] {Path.GetFileName(fp)}";
                                AppendLog($"  ❌ {Path.GetFileName(fp)} — 未找到角点");
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke(new Action(() =>
                            AppendLog($"  ⚠ 处理出错 {Path.GetFileName(fp)}: {ex.Message}")));
                    }
                }
            });

            if (successCount < 3)
            {
                AppendLog($"❌ 有效图片仅 {successCount} 张（需至少 3 张），标定终止。");
                SetBusy(false);
                return;
            }

            AppendLog($"有效图片 {successCount}/{_imageFiles.Count} 张，开始计算内参...");

            try
            {
                double rms     = 0;
                Mat camMat     = new Mat();
                Mat distCoef   = new Mat();

                await Task.Run(() =>
                {
                    rms = Cv2.CalibrateCamera(
                        objPts, imgPts, imgSize!.Value,
                        camMat, distCoef,
                        out Mat[] rvecs, out Mat[] tvecs);
                });

                // 提取内参
                _cameraMatrix    = new double[3, 3];
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        _cameraMatrix[r, c] = camMat.At<double>(r, c);

                int dCols    = distCoef.Cols * distCoef.Rows;
                _distCoeffs  = new double[dCols];
                for (int c = 0; c < dCols; c++)
                    _distCoeffs[c] = distCoef.Rows == 1
                        ? distCoef.At<double>(0, c)
                        : distCoef.At<double>(c, 0);

                _rmsError     = rms;
                _calibratedSn = GetSelectedSn() ?? "unknown";

                double fx = _cameraMatrix[0, 0];
                double fy = _cameraMatrix[1, 1];
                double cx = _cameraMatrix[0, 2];
                double cy = _cameraMatrix[1, 2];

                string[] dNames = { "k1", "k2", "p1", "p2", "k3", "k4", "k5", "k6" };
                string distStr  = string.Join(",  ",
                    _distCoeffs.Select((v, i) => $"{(i < dNames.Length ? dNames[i] : $"d{i}")}={v:F6}"));

                _txtResult.Text =
                    $"✅ 标定成功！  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"相机 SN: {_calibratedSn}\r\n" +
                    $"有效图片: {successCount} / {_imageFiles.Count} 张\r\n" +
                    $"重投影误差 (RMS): {rms:F4} px\r\n\r\n" +
                    $"━━ 内参矩阵 K ━━\r\n" +
                    $"  [ {fx,12:F4}   {0.0,12:F4}   {cx,12:F4} ]\r\n" +
                    $"  [ {0.0,12:F4}   {fy,12:F4}   {cy,12:F4} ]\r\n" +
                    $"  [ {0.0,12:F4}   {0.0,12:F4}   {1.0,12:F4} ]\r\n\r\n" +
                    $"━━ 畸变系数 ━━\r\n" +
                    $"  {distStr}\r\n\r\n" +
                    $"━━ 精度评估 ━━\r\n" +
                    (rms < 0.5 ? "  ⭐ 优秀 (RMS < 0.5 px)" :
                     rms < 1.0 ? "  ✅ 良好 (RMS < 1.0 px)" :
                                 "  ⚠ 误差偏大，建议补充更多角度图片重新标定");

                AppendLog($"✅ 标定完成  RMS={rms:F4}px  fx={fx:F1}  fy={fy:F1}  cx={cx:F1}  cy={cy:F1}");
                _btnSave.Enabled = true;

                camMat.Dispose();
                distCoef.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 标定失败: {ex.Message}");
                _txtResult.Text = $"❌ 标定失败:\r\n{ex.Message}";
            }
            finally
            {
                foreach (var m in objPts.Concat(imgPts)) m.Dispose();
                SetBusy(false);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  保存结果
        // ═══════════════════════════════════════════════════════════════
        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (_cameraMatrix == null || _distCoeffs == null)
            {
                MessageBox.Show("尚无标定结果，请先执行标定。", "提示");
                return;
            }
            try
            {
                var config = ConfigManager.Instance;
                if (config == null) { MessageBox.Show("ConfigManager 未初始化。"); return; }

                config.Camera2DCalibrations ??= new List<Camera2DCalibration>();
                config.Camera2DCalibrations.RemoveAll(c => c.CameraSn == _calibratedSn);
                config.Camera2DCalibrations.Add(new Camera2DCalibration
                {
                    CameraSn     = _calibratedSn,
                    CalibratedAt = DateTime.Now,
                    RmsError     = _rmsError,
                    Fx           = _cameraMatrix[0, 0],
                    Fy           = _cameraMatrix[1, 1],
                    Cx           = _cameraMatrix[0, 2],
                    Cy           = _cameraMatrix[1, 2],
                    DistCoeffs   = _distCoeffs.ToList(),
                });
                ConfigManager.SaveConfig();

                ExportOpenCvYaml(_calibratedSn, _cameraMatrix, _distCoeffs, _rmsError);

                AppendLog($"💾 已保存  SN={_calibratedSn}  RMS={_rmsError:F4}px");
                MessageBox.Show(
                    $"标定结果已保存！\n相机: {_calibratedSn}\n" +
                    $"RMS: {_rmsError:F4} px\n\n已同步导出 OpenCV YAML：\nConfig/calib_{_calibratedSn}.yaml",
                    "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ExportOpenCvYaml(string sn, double[,] K, double[] D, double rms)
        {
            string dir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            string path = Path.Combine(dir, $"calib_{sn}.yaml");
            Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("%YAML:1.0");
            sb.AppendLine($"# 2D Camera Calibration  SN: {sn}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# RMS reprojection error: {rms:F4} px");
            sb.AppendLine("camera_matrix:");
            sb.AppendLine("  rows: 3");
            sb.AppendLine("  cols: 3");
            sb.AppendLine($"  data: [ {K[0,0]:F6}, 0.0, {K[0,2]:F6}, 0.0, {K[1,1]:F6}, {K[1,2]:F6}, 0.0, 0.0, 1.0 ]");
            sb.AppendLine("dist_coeffs:");
            sb.AppendLine("  rows: 1");
            sb.AppendLine($"  cols: {D.Length}");
            sb.AppendLine("  data: [ " + string.Join(", ", D.Select(v => v.ToString("F8"))) + " ]");
            File.WriteAllText(path, sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════
        //  清除
        // ═══════════════════════════════════════════════════════════════
        private void BtnClear_Click(object? sender, EventArgs e)
        {
            _imageFiles.Clear();
            _validImages.Clear();
            _currentIdx  = -1;
            _lastCorners = null;
            _cameraMatrix = null;
            _distCoeffs   = null;
            _currentBmp?.Dispose();
            _currentBmp      = null;
            _pbPreview.Image = null;
            _lstFiles.Items.Clear();
            _lstLog.Items.Clear();
            _txtResult.Text   = "";
            _lblImgIndex.Text = "未加载图片";
            _lblStatus.Text   = "";
            _lblDirPath.Text  = "（未选择文件夹）";
            _btnCalibrate.Enabled = false;
            _btnSave.Enabled      = false;
            _btnTestOffset.Enabled = false;
            _btnDrawRoiLeft.Enabled = false;
            _btnDrawRoiRight.Enabled = false;
            _btnSaveRoiRef.Enabled = false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  堆垛机偏移检测算法 (测试)
        // ═══════════════════════════════════════════════════════════════
        private void BtnTestOffset_Click(object? sender, EventArgs e)
        {
            if (_currentIdx < 0 || _currentIdx >= _imageFiles.Count)
            {
                MessageBox.Show("请先加载并选择一张图片。", "提示");
                return;
            }
            string fp = _imageFiles[_currentIdx];
            AppendLog($"🔍 开始对 {Path.GetFileName(fp)} 进行偏移检测...");
            
            try
            {
                TestOffsetAlgorithm(fp);
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 偏移检测失败: {ex.Message}");
                MessageBox.Show($"检测过程中出错:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private float? TestOffsetAlgorithm(string imagePath, bool isSettingRef = false)
        {
            using var src = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (src.Empty()) throw new Exception("无法读取图像");

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            
            // 1. 高斯滤波与自适应阈值提取黑色孔洞
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);
            
            using var binary = new Mat();
            // 提高 C 常量从 15 到 25，只提取比周围暗得多的区域，减少噪点
            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, 51, 25);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);

            // 2. 轮廓提取
            Cv2.FindContours(binary, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var validEllipses = new List<RotatedRect>();
            int minArea = 50;
            int maxArea = 8000;

            foreach (var contour in contours)
            {
                if (contour.Length < 5) continue;

                double area = Cv2.ContourArea(contour);
                if (area < minArea || area > maxArea) continue;

                var ellipse = Cv2.FitEllipse(contour);
                
                // ROI 过滤判断 (四点多边形)
                if (_roiLeft.Count == 4 || _roiRight.Count == 4)
                {
                    bool inLeft = false, inRight = false;
                    var pt = new OpenCvSharp.Point2f(ellipse.Center.X, ellipse.Center.Y);
                    
                    if (_roiLeft.Count == 4)
                    {
                        var pts = _roiLeft.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                        inLeft = Cv2.PointPolygonTest(pts, pt, false) >= 0;
                    }
                    if (_roiRight.Count == 4)
                    {
                        var pts = _roiRight.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                        inRight = Cv2.PointPolygonTest(pts, pt, false) >= 0;
                    }
                    
                    if (!inLeft && !inRight) continue;
                }

                double width = Math.Min(ellipse.Size.Width, ellipse.Size.Height);
                double height = Math.Max(ellipse.Size.Width, ellipse.Size.Height);
                double aspectRatio = width / height;

                // 计算 Solidity (面积与凸包面积比)，真正的圆孔通常饱满且无凹陷
                var hull = Cv2.ConvexHull(contour);
                double hullArea = Cv2.ContourArea(hull);
                double solidity = hullArea > 0 ? area / hullArea : 0;
                
                // 增加严格的过滤条件：
                // 1. 宽高比 > 0.5 （圆孔在透视下是椭圆，但不会太扁长）
                // 2. Solidity > 0.85 （轮廓必须饱满，排除不规则凹陷形状）
                if (aspectRatio > 0.5 && aspectRatio <= 1.0 && solidity > 0.85)
                {
                    validEllipses.Add(ellipse);
                }
            }

            if (validEllipses.Count < 4)
            {
                if (isSettingRef) return null;
                AppendLog($"  ⚠ 检测到的有效圆孔过少 ({validEllipses.Count})，可能受光照影响或参数不匹配。");
            }

            // 3. 分离左右立柱集群
            validEllipses = validEllipses.OrderBy(e => e.Center.X).ToList();
            if (validEllipses.Count == 0)
            {
                if (isSettingRef) return null;
                throw new Exception("没有找到任何有效孔洞。");
            }

            int splitIndex = -1;
            double maxGap = 0;
            for (int i = 0; i < validEllipses.Count - 1; i++)
            {
                double gap = validEllipses[i + 1].Center.X - validEllipses[i].Center.X;
                if (gap > maxGap)
                {
                    maxGap = gap;
                    splitIndex = i;
                }
            }

            if (splitIndex == -1 || maxGap < src.Width * 0.05)
            {
                if (isSettingRef) return null;
                throw new Exception("无法区分左右立柱 (未找到明显间隙)。");
            }

            var leftCluster = validEllipses.Take(splitIndex + 1).ToList();
            var rightCluster = validEllipses.Skip(splitIndex + 1).ToList();

            // 4. 在集群中分离列并提取最内侧列
            var leftInnerColumn = GetInnerColumn(leftCluster, isLeftPillar: true);
            var rightInnerColumn = GetInnerColumn(rightCluster, isLeftPillar: false);

            if (leftInnerColumn.Count < 2 || rightInnerColumn.Count < 2)
            {
                if (isSettingRef) return null;
                throw new Exception($"最内侧列点数不足 (左={leftInnerColumn.Count}, 右={rightInnerColumn.Count})，无法拟合直线。");
            }

            // 5. 拟合直线
            var leftPts = leftInnerColumn.Select(e => new Point2f(e.Center.X, e.Center.Y)).ToArray();
            var rightPts = rightInnerColumn.Select(e => new Point2f(e.Center.X, e.Center.Y)).ToArray();

            var leftLine = Cv2.FitLine(leftPts, DistanceTypes.L2, 0, 0.01, 0.01);
            var rightLine = Cv2.FitLine(rightPts, DistanceTypes.L2, 0, 0.01, 0.01);

            Point2f ptL1 = new Point2f((float)(leftLine.X1 - leftLine.Vx * 10000), (float)(leftLine.Y1 - leftLine.Vy * 10000));
            Point2f ptL2 = new Point2f((float)(leftLine.X1 + leftLine.Vx * 10000), (float)(leftLine.Y1 + leftLine.Vy * 10000));
            Point2f ptR1 = new Point2f((float)(rightLine.X1 - rightLine.Vx * 10000), (float)(rightLine.Y1 - rightLine.Vy * 10000));
            Point2f ptR2 = new Point2f((float)(rightLine.X1 + rightLine.Vx * 10000), (float)(rightLine.Y1 + rightLine.Vy * 10000));

            foreach (var e in validEllipses)
            {
                if (!leftInnerColumn.Contains(e) && !rightInnerColumn.Contains(e))
                {
                    Cv2.Ellipse(src, e, Scalar.LightGreen, 2);
                }
            }
            foreach (var e in leftInnerColumn) Cv2.Ellipse(src, e, Scalar.Red, 3);
            foreach (var e in rightInnerColumn) Cv2.Ellipse(src, e, Scalar.Red, 3);

            Cv2.Line(src, new OpenCvSharp.Point((int)ptL1.X, (int)ptL1.Y), new OpenCvSharp.Point((int)ptL2.X, (int)ptL2.Y), Scalar.Red, 2);
            Cv2.Line(src, new OpenCvSharp.Point((int)ptR1.X, (int)ptR1.Y), new OpenCvSharp.Point((int)ptR2.X, (int)ptR2.Y), Scalar.Red, 2);

            // 6. 计算中心线与偏移量
            double m1 = leftLine.Vx / leftLine.Vy;
            double c1 = leftLine.X1 - m1 * leftLine.Y1;
            double m2 = rightLine.Vx / rightLine.Vy;
            double c2 = rightLine.X1 - m2 * rightLine.Y1;

            float midY = src.Height / 2f;
            float leftX = (float)(m1 * midY + c1);
            float rightX = (float)(m2 * midY + c2);
            float actualCenterX = (leftX + rightX) / 2f;

            if (isSettingRef) return actualCenterX;

            float offset = 0;
            if (_referenceOffsetX.HasValue)
            {
                offset = actualCenterX - _referenceOffsetX.Value;
            }
            else
            {
                float imageCenterX = src.Width / 2f;
                offset = actualCenterX - imageCenterX;
            }

            OpenCvSharp.Point ptCenterTop = new OpenCvSharp.Point((int)((m1 * 0 + c1 + m2 * 0 + c2) / 2), 0);
            OpenCvSharp.Point ptCenterBot = new OpenCvSharp.Point((int)((m1 * src.Height + c1 + m2 * src.Height + c2) / 2), src.Height);
            Cv2.Line(src, ptCenterTop, ptCenterBot, Scalar.Yellow, 2, LineTypes.AntiAlias);

            if (_referenceOffsetX.HasValue)
            {
                Cv2.Line(src, new OpenCvSharp.Point((int)_referenceOffsetX.Value, 0), new OpenCvSharp.Point((int)_referenceOffsetX.Value, src.Height), Scalar.Purple, 2, LineTypes.Link4);
            }
            else
            {
                Cv2.Line(src, new OpenCvSharp.Point((int)(src.Width / 2f), 0), new OpenCvSharp.Point((int)(src.Width / 2f), src.Height), Scalar.Blue, 2, LineTypes.Link4);
            }

            string refText = _referenceOffsetX.HasValue ? "(Rel to Ref)" : "(Rel to ImgCenter)";
            string text = $"Offset: {offset:F1} px {refText}";
            Cv2.PutText(src, text, new OpenCvSharp.Point(50, 80), HersheyFonts.HersheySimplex, 1.5, Scalar.Cyan, 3);

            AppendLog($"  ✅ 检测完成。左内列: {leftInnerColumn.Count}个, 右内列: {rightInnerColumn.Count}个");
            AppendLog($"  📊 像素偏移量: {offset:F2} px");

            using var display = new Mat();
            double scale = 1200.0 / Math.Max(src.Width, src.Height);
            if (scale < 1.0)
                Cv2.Resize(src, display, new OpenCvSharp.Size(0, 0), scale, scale);
            else
                src.CopyTo(display);

            Cv2.ImShow("Offset Detection Result", display);
            Cv2.WaitKey(1);
            
            return actualCenterX;
        }

        private List<RotatedRect> GetInnerColumn(List<RotatedRect> cluster, bool isLeftPillar)
        {
            if (cluster.Count == 0) return new List<RotatedRect>();
            
            // 使用 K-Means (K=2) 将集群分为两列
            var xValues = cluster.Select(e => e.Center.X).ToArray();
            
            float span = xValues.Max() - xValues.Min();
            if (span < 40) // X 跨度太小，认为只有一列
            {
                return cluster; 
            }

            double c1 = xValues.Min();
            double c2 = xValues.Max();
            for (int iter = 0; iter < 10; iter++)
            {
                var g1 = new List<double>();
                var g2 = new List<double>();
                foreach (var x in xValues)
                {
                    if (Math.Abs(x - c1) < Math.Abs(x - c2)) g1.Add(x);
                    else g2.Add(x);
                }
                c1 = g1.Count > 0 ? g1.Average() : c1;
                c2 = g2.Count > 0 ? g2.Average() : c2;
            }

            if (c1 > c2)
            {
                (c1, c2) = (c2, c1);
            }

            var leftSubCol = cluster.Where(e => Math.Abs(e.Center.X - c1) < Math.Abs(e.Center.X - c2)).ToList();
            var rightSubCol = cluster.Where(e => Math.Abs(e.Center.X - c1) >= Math.Abs(e.Center.X - c2)).ToList();

            if (leftSubCol.Count == 0) return rightSubCol;
            if (rightSubCol.Count == 0) return leftSubCol;

            if (isLeftPillar)
            {
                // 左立柱，取靠右的一列（即 X 较大的 c2）
                return rightSubCol;
            }
            else
            {
                // 右立柱，取靠左的一列（即 X 较小的 c1）
                return leftSubCol;
            }
        }


        // ═══════════════════════════════════════════════════════════════
        //  图像采集与 ROI 操作逻辑
        // ═══════════════════════════════════════════════════════════════
        private void CmbCamera_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn)) return;
            var calib = ConfigManager.Instance?.Camera2DCalibrations?.FirstOrDefault(c => c.CameraSn == sn);
            
            _roiLeft.Clear();
            _roiRight.Clear();
            _referenceOffsetX = null;

            if (calib != null)
            {
                if (calib.RoiLeft != null && calib.RoiLeft.Count == 8)
                {
                    for (int i = 0; i < 8; i += 2)
                        _roiLeft.Add(new DrawPoint(calib.RoiLeft[i], calib.RoiLeft[i+1]));
                }

                if (calib.RoiRight != null && calib.RoiRight.Count == 8)
                {
                    for (int i = 0; i < 8; i += 2)
                        _roiRight.Add(new DrawPoint(calib.RoiRight[i], calib.RoiRight[i+1]));
                }

                _referenceOffsetX = calib.ReferenceOffsetX;
            }
            _pbPreview.Invalidate();
        }

        private async void BtnCaptureImage_Click(object? sender, EventArgs e)
        {
            var sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn))
            {
                MessageBox.Show("请先选择一个相机 SN", "提示");
                return;
            }

            SetBusy(true);
            try
            {
                AppendLog($"准备连接并采集图像: {sn}...");
                if (!material_box_storage_detection_system_Net.Devices.DeviceManager.IsInitialized)
                {
                    material_box_storage_detection_system_Net.Devices.DeviceManager.Initialize(ConfigManager.Instance!.Cameras, AppendLog);
                }

                var cam = material_box_storage_detection_system_Net.Devices.DeviceManager.GetCamera(sn);
                if (cam == null)
                {
                    var cfg = ConfigManager.Instance!.Cameras.FirstOrDefault(c => c.Sn == sn);
                    if (cfg != null)
                    {
                        material_box_storage_detection_system_Net.Devices.DeviceManager.Initialize(new List<material_box_storage_detection_system_Net.Config.CameraConfig> { cfg }, AppendLog);
                        cam = material_box_storage_detection_system_Net.Devices.DeviceManager.GetCamera(sn);
                    }
                }

                if (cam == null || !cam.Connect())
                {
                    AppendLog("❌ 相机连接失败或未找到实例。");
                    return;
                }

                var frameObj = await cam.GrabFrameAsync();
                if (frameObj is Bitmap bmp)
                {
                    string dir = _imageFiles.Count > 0 ? Path.GetDirectoryName(_imageFiles[0])! : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string fp = Path.Combine(dir, $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    bmp.Save(fp, System.Drawing.Imaging.ImageFormat.Jpeg);
                    
                    _imageFiles.Add(fp);
                    _validImages.Add(fp);
                    _currentIdx = _imageFiles.Count - 1;
                    LoadImage(_currentIdx);
                    
                    _lstFiles.Items.Add($"[ ] {Path.GetFileName(fp)}");
                    _lstFiles.SelectedIndex = _lstFiles.Items.Count - 1;

                    AppendLog($"✅ 图像采集成功，已保存至: {fp}");
                }
                else
                {
                    AppendLog("❌ 抓取到的帧格式不支持，非 Bitmap。");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 图像采集发生异常: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PbPreview_MouseDown(object? sender, MouseEventArgs e)
        {
            if (!_isDrawingLeftRoi && !_isDrawingRightRoi) return;
            if (_pbPreview.Image == null) return;
            if (e.Button != MouseButtons.Left) return;

            float scaleX = (float)_pbPreview.Image.Width / _pbPreview.Width;
            float scaleY = (float)_pbPreview.Image.Height / _pbPreview.Height;
            float scale = Math.Max(scaleX, scaleY);
            
            int imgW = (int)(_pbPreview.Image.Width / scale);
            int imgH = (int)(_pbPreview.Image.Height / scale);
            int offsetX = (_pbPreview.Width - imgW) / 2;
            int offsetY = (_pbPreview.Height - imgH) / 2;

            int realX = (int)((e.X - offsetX) * scale);
            int realY = (int)((e.Y - offsetY) * scale);

            realX = Math.Max(0, Math.Min(realX, _pbPreview.Image.Width));
            realY = Math.Max(0, Math.Min(realY, _pbPreview.Image.Height));

            _currentDrawingPoints.Add(new DrawPoint(realX, realY));
            _pbPreview.Invalidate();

            if (_currentDrawingPoints.Count == 4)
            {
                if (_isDrawingLeftRoi)
                {
                    _roiLeft.Clear();
                    _roiLeft.AddRange(_currentDrawingPoints);
                    _isDrawingLeftRoi = false;
                    AppendLog($"✅ 左侧 ROI 绘制完成 (4点)");
                }
                else if (_isDrawingRightRoi)
                {
                    _roiRight.Clear();
                    _roiRight.AddRange(_currentDrawingPoints);
                    _isDrawingRightRoi = false;
                    AppendLog($"✅ 右侧 ROI 绘制完成 (4点)");
                }
                _currentDrawingPoints.Clear();
                _pbPreview.Invalidate();
            }
        }

        private void PbPreview_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDrawingLeftRoi && !_isDrawingRightRoi) return;
            
            float scaleX = (float)_pbPreview.Image!.Width / _pbPreview.Width;
            float scaleY = (float)_pbPreview.Image.Height / _pbPreview.Height;
            float scale = Math.Max(scaleX, scaleY);
            
            int imgW = (int)(_pbPreview.Image.Width / scale);
            int imgH = (int)(_pbPreview.Image.Height / scale);
            int offsetX = (_pbPreview.Width - imgW) / 2;
            int offsetY = (_pbPreview.Height - imgH) / 2;

            int realX = (int)((e.X - offsetX) * scale);
            int realY = (int)((e.Y - offsetY) * scale);
            
            _mouseHoverPos = new DrawPoint(realX, realY);
            if (_currentDrawingPoints.Count > 0)
            {
                _pbPreview.Invalidate();
            }
        }

        private void PbPreview_MouseUp(object? sender, MouseEventArgs e)
        {
            // Do nothing
        }

        private void BtnSaveRoiRef_Click(object? sender, EventArgs e)
        {
            var sn = GetSelectedSn();
            if (string.IsNullOrEmpty(sn))
            {
                MessageBox.Show("请先选择一个相机 SN", "提示");
                return;
            }
            if (_currentIdx < 0 || _currentIdx >= _imageFiles.Count)
            {
                MessageBox.Show("请加载一张图片（建议为零位标准图片），然后再保存基准值。", "提示");
                return;
            }

            try
            {
                float? refX = TestOffsetAlgorithm(_imageFiles[_currentIdx], true);
                if (refX == null)
                {
                    AppendLog("❌ 保存基准失败：当前图像未能计算出有效的堆垛机中心线。");
                    MessageBox.Show("无法在当前图像中计算出中心线，请检查 ROI 框选是否正确包含了目标孔洞。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _referenceOffsetX = refX;

                if (ConfigManager.Instance?.Camera2DCalibrations == null)
                    ConfigManager.Instance!.Camera2DCalibrations = new List<material_box_storage_detection_system_Net.Config.Camera2DCalibration>();

                var calib = ConfigManager.Instance.Camera2DCalibrations.FirstOrDefault(c => c.CameraSn == sn);
                if (calib == null)
                {
                    calib = new material_box_storage_detection_system_Net.Config.Camera2DCalibration { CameraSn = sn };
                    ConfigManager.Instance.Camera2DCalibrations.Add(calib);
                }

                calib.RoiLeft = _roiLeft.SelectMany(p => new int[] { p.X, p.Y }).ToList();
                calib.RoiRight = _roiRight.SelectMany(p => new int[] { p.X, p.Y }).ToList();
                calib.ReferenceOffsetX = _referenceOffsetX;

                ConfigManager.SaveConfig();
                AppendLog($"💾 基准值及 ROI 已保存至配置中。基准中心X: {refX:F1}");
                MessageBox.Show("保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch(Exception ex)
            {
                AppendLog($"❌ 保存基准发生异常: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  辅助
        // ═══════════════════════════════════════════════════════════════
        private void AppendLog(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), msg); return; }
            _lstLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            _lstLog.TopIndex = _lstLog.Items.Count - 1;
        }

        private void SetBusy(bool busy)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool>(SetBusy), busy); return; }
            _btnCalibrate.Enabled = !busy && _imageFiles.Count > 0;
            _btnLoadDir.Enabled   = !busy;
            _btnPrev.Enabled      = !busy;
            _btnNext.Enabled      = !busy;
            bool hasImages = _imageFiles.Count > 0;
            _btnTestOffset.Enabled = !busy && hasImages;
            _btnDrawRoiLeft.Enabled = !busy && hasImages;
            _btnDrawRoiRight.Enabled = !busy && hasImages;
            _btnSaveRoiRef.Enabled = !busy && hasImages;
            Cursor                = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _currentBmp?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
