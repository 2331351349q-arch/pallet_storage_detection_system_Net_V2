using System.Data;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;

namespace material_box_storage_detection_system_Net
{
    /// <summary>
    /// 相机批量控制工具 — 支持一键开启/关闭所有 2D、3D 相机。
    /// </summary>
    public class CameraControlForm : Form
    {
        private DataTable? _cameraTable;
        private DataGridView? _grid;
        private ListBox? _logBox;
        private Button? _btnStartAll;
        private Button? _btnStopAll;
        private Button? _btnRefresh;
        private bool _isBatchBusy;
        private Dictionary<string, PictureBox> _pictureBoxes = new Dictionary<string, PictureBox>();
        private FlowLayoutPanel? _previewPanel;
        private CancellationTokenSource? _previewCts;

        public CameraControlForm()
        {
            InitializeComponent();
            LoadCameraList();
        }

        private void InitializeComponent()
        {
            try
            {
                SuspendLayout();
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Color.FromArgb(240, 242, 248);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = true;
                MinimizeBox = true;
                WindowState = FormWindowState.Maximized;

                var root = new TableLayoutPanel
                {
                    ColumnCount = 1,
                    RowCount = 3,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(32, 24, 32, 24),
                    BackColor = Color.FromArgb(240, 242, 248),
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));

                // ── 标题行 ──
                var header = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                };
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                header.Controls.Add(new Label
                {
                    Text = "📷  相机批量控制",
                    Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(28, 45, 72),
                    AutoSize = true,
                    Dock = DockStyle.Left,
                    TextAlign = ContentAlignment.MiddleLeft,
                }, 0, 0);

                _btnRefresh = new Button
                {
                    Text = "🔄 刷新状态",
                    Font = new Font("Microsoft YaHei UI", 9.5F),
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(0, 122, 204),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(130, 36),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 10, 0),
                };
                _btnRefresh.FlatAppearance.BorderSize = 0;
                _btnRefresh.Click += BtnRefresh_Click;
                header.Controls.Add(_btnRefresh, 1, 0);

                root.Controls.Add(header, 0, 0);

                // ── 相机列表 DataGridView ──
                _cameraTable = new DataTable();
                _cameraTable.Columns.Add("Sn", typeof(string));
                _cameraTable.Columns.Add("Name", typeof(string));
                _cameraTable.Columns.Add("Type", typeof(string));
                _cameraTable.Columns.Add("Status", typeof(string));

                _grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    AllowUserToResizeRows = false,
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = Color.White,
                    BorderStyle = BorderStyle.None,
                    GridColor = Color.FromArgb(220, 225, 235),
                    Font = new Font("Microsoft YaHei UI", 10F),
                    Margin = new Padding(0, 12, 0, 0),
                    MultiSelect = false,
                };

                // ⚠ 手动绑定列：避免 DataGridView 自动生成列导致的 NullReferenceException
                var colSn = new DataGridViewTextBoxColumn
                {
                    Name = "Sn",
                    DataPropertyName = "Sn",
                    HeaderText = "序列号 (SN)",
                    FillWeight = 35F,
                };
                var colName = new DataGridViewTextBoxColumn
                {
                    Name = "Name",
                    DataPropertyName = "Name",
                    HeaderText = "名称",
                    FillWeight = 25F,
                };
                var colType = new DataGridViewTextBoxColumn
                {
                    Name = "Type",
                    DataPropertyName = "Type",
                    HeaderText = "类型",
                    FillWeight = 15F,
                };
                var colStatus = new DataGridViewTextBoxColumn
                {
                    Name = "Status",
                    DataPropertyName = "Status",
                    HeaderText = "运行状态",
                    FillWeight = 25F,
                };

                _grid.Columns.Add(colSn);
                _grid.Columns.Add(colName);
                _grid.Columns.Add(colType);
                _grid.Columns.Add(colStatus);
                _grid.AutoGenerateColumns = false;
                _grid.DataSource = _cameraTable;

                _grid.EnableHeadersVisualStyles = false;
                _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(28, 45, 72);
                _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
                _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                _grid.ColumnHeadersHeight = 38;

                _grid.DefaultCellStyle.BackColor = Color.White;
                _grid.DefaultCellStyle.ForeColor = Color.FromArgb(40, 45, 55);
                _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 255);
                _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(28, 45, 72);
                _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

                _grid.CellFormatting += Grid_CellFormatting;

                // ── 中间主体区域（左侧列表 + 右侧预览） ──
                var middlePanel = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 12, 0, 0),
                };
                middlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
                middlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));

                _grid.Margin = new Padding(0); // Override previous margin
                middlePanel.Controls.Add(_grid, 0, 0);

                _previewPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(26, 28, 36),
                    Margin = new Padding(12, 0, 0, 0),
                    Padding = new Padding(10)
                };
                middlePanel.Controls.Add(_previewPanel, 1, 0);

                root.Controls.Add(middlePanel, 0, 1);

                // ── 底部区域：按钮 + 日志 ──
                var bottomPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 14, 0, 0),
                    ColumnCount = 1,
                    RowCount = 3
                };
                bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                // 按钮栏
                var btnPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.LeftToRight,
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    BackColor = Color.Transparent,
                    WrapContents = true,
                };

                _btnStartAll = CreateAccentButton("🚀  一键开启全部相机", Color.FromArgb(0, 153, 136));
                _btnStartAll.Click += BtnStartAll_Click;
                btnPanel.Controls.Add(_btnStartAll);

                _btnStopAll = CreateAccentButton("⏹  一键关闭全部相机", Color.FromArgb(200, 60, 60));
                _btnStopAll.Click += BtnStopAll_Click;
                btnPanel.Controls.Add(_btnStopAll);

                bottomPanel.Controls.Add(btnPanel, 0, 0);

                // 日志区标题
                var logLabel = new Label
                {
                    Text = "操作日志",
                    Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(100, 110, 130),
                    Dock = DockStyle.Fill,
                    Height = 28,
                    Padding = new Padding(4, 6, 0, 0),
                };
                bottomPanel.Controls.Add(logLabel, 0, 1);

                // 日志列表
                _logBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(30, 30, 40),
                    ForeColor = Color.FromArgb(180, 220, 140),
                    Font = new Font("Consolas", 10.5F),
                    BorderStyle = BorderStyle.None,
                    HorizontalScrollbar = true
                };
                bottomPanel.Controls.Add(_logBox, 0, 2);

                root.Controls.Add(bottomPanel, 0, 2);

                Controls.Add(root);
                ClientSize = new Size(1200, 700); // 增大窗口以容纳预览区
                Text = "相机批量控制";
                ResumeLayout(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"相机控制窗体初始化失败:\n{ex.Message}\n\n{ex.StackTrace}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        private Button CreateAccentButton(string text, Color color)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 40),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 14, 0),
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPreviewLoop();
            base.OnFormClosed(e);
        }

        private void StartPreviewLoop()
        {
            if (_previewCts != null && !_previewCts.IsCancellationRequested) return;
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var config = ConfigManager.Instance;
                    var cameras = config?.Cameras;
                    if (cameras != null)
                    {
                        foreach (var cfg in cameras)
                        {
                            if (token.IsCancellationRequested) break;
                            if (string.IsNullOrEmpty(cfg.Sn)) continue;

                            var dev = DeviceManager.GetCamera(cfg.Sn);
                            if (dev != null && dev.IsCapturing)
                            {
                                try
                                {
                                    var frame = await dev.GrabFrameAsync();
                                    if (frame != null && _pictureBoxes.TryGetValue(cfg.Sn, out var pb))
                                    {
                                        Image? imgToDisplay = null;
                                        if (frame is Image img) imgToDisplay = (Image)img.Clone();
                                        else if (frame is DepthFrameData depth && depth.PreviewImage != null) imgToDisplay = (Image)depth.PreviewImage.Clone();

                                        if (imgToDisplay != null && !pb.IsDisposed && pb.IsHandleCreated)
                                        {
                                            pb.BeginInvoke(new Action(() =>
                                            {
                                                var old = pb.Image;
                                                pb.Image = imgToDisplay;
                                                old?.Dispose();
                                            }));
                                        }
                                    }
                                }
                                catch
                                {
                                    // 忽略抓图异常，防止刷屏
                                }
                            }
                        }
                    }
                    try { await Task.Delay(500, token); } catch { }
                }
            }, token);
        }

        private void StopPreviewLoop()
        {
            _previewCts?.Cancel();
            _previewCts = null;
        }

        private void AppendLog(string msg)
        {
            if (_logBox == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), msg);
                return;
            }
            _logBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            _logBox.TopIndex = _logBox.Items.Count - 1;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetButtonsEnabled), enabled);
                return;
            }
            if (_btnStartAll != null) _btnStartAll.Enabled = enabled;
            if (_btnStopAll != null) _btnStopAll.Enabled = enabled;
            if (_btnRefresh != null) _btnRefresh.Enabled = enabled;
        }

        /// <summary>
        /// 从配置文件加载相机列表到表格。
        /// </summary>
        private void LoadCameraList()
        {
            try
            {
                if (_cameraTable == null) return;
                _cameraTable.Rows.Clear();
                _pictureBoxes.Clear();
                if (_previewPanel != null) _previewPanel.Controls.Clear();

                var config = ConfigManager.Instance;
                var cameras = config?.Cameras;
                if (cameras == null || cameras.Count == 0) return;

                foreach (var cfg in cameras)
                {
                    if (cfg == null || string.IsNullOrEmpty(cfg.Sn)) continue;
                    string typeStr = cfg.Type ?? "";
                    string typeLabel = typeStr.Contains("Hikvision") ? "2D 相机" : "3D 相机";
                    var dev = DeviceManager.GetCamera(cfg.Sn);
                    string status = GetStatusText(dev);
                    _cameraTable.Rows.Add(cfg.Sn, cfg.Name ?? "N/A", typeLabel, status);
                    
                    AddPreviewBox(cfg.Sn, cfg.Name ?? cfg.Sn);
                }

            }
            catch (Exception ex)
            {
                // 加载失败不阻塞窗口打开
                System.Diagnostics.Debug.WriteLine($"LoadCameraList 失败: {ex.Message}");
            }
        }

        private void AddPreviewBox(string sn, string title)
        {
            if (_previewPanel == null) return;

            var container = new Panel
            {
                Width = 360,
                Height = 300,
                Margin = new Padding(12),
                BackColor = Color.FromArgb(40, 45, 55),
            };

            var pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(2),
            };

            var lbl = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            };

            container.Controls.Add(pb);
            container.Controls.Add(lbl);

            _previewPanel.Controls.Add(container);
            _pictureBoxes[sn] = pb;
        }

        private static string GetStatusText(ICameraDevice? dev)
        {
            if (dev == null) return "未连接";
            try
            {
                if (!dev.IsConnected) return "未连接";
                return dev.IsCapturing ? "🟢 采集中" : "🟡 已就绪";
            }
            catch
            {
                return "未连接";
            }
        }

        private void RefreshStatus()
        {
            try
            {
                if (_cameraTable == null || _cameraTable.Rows.Count == 0) return;
                var config = ConfigManager.Instance;
                var cameras = config?.Cameras;
                if (cameras == null) return;

                for (int i = 0; i < cameras.Count && i < _cameraTable.Rows.Count; i++)
                {
                    var cfg = cameras[i];
                    if (cfg == null || string.IsNullOrEmpty(cfg.Sn)) continue;
                    var dev = DeviceManager.GetCamera(cfg.Sn);
                    _cameraTable.Rows[i]["Status"] = GetStatusText(dev);
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshStatus 失败: {ex.Message}");
            }
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.Value == null) return;

            try
            {
                if (_cameraTable == null || _grid == null) return;
                if (e.ColumnIndex >= _grid.Columns.Count) return;
                if (_grid.Columns[e.ColumnIndex].Name != "Status") return;

                if (e.Value is string status)
                {
                    if (status.Contains("采集中"))
                        e.CellStyle!.ForeColor = Color.FromArgb(0, 150, 80);
                    else if (status.Contains("已就绪"))
                        e.CellStyle!.ForeColor = Color.FromArgb(200, 150, 0);
                    else
                        e.CellStyle!.ForeColor = Color.FromArgb(180, 50, 50);
                }
            }
            catch
            {
                // 格式化异常不阻断渲染
            }
        }

        private async void BtnStartAll_Click(object? sender, EventArgs e)
        {
            if (_isBatchBusy) return;
            _isBatchBusy = true;
            SetButtonsEnabled(false);

            try
            {
                AppendLog("━━━━ 一键开启全部相机 ━━━━");
                int success = 0, fail = 0;

                var config = ConfigManager.Instance;

                // 1. 开启所有 2D / 3D 相机采集
                var cameras = config?.Cameras;
                if (cameras != null)
                {
                    foreach (var cfg in cameras)
                    {
                        if (cfg == null || string.IsNullOrEmpty(cfg.Sn)) continue;
                        var dev = DeviceManager.GetCamera(cfg.Sn);
                        if (dev == null)
                        {
                            AppendLog($"⚠ {cfg.Name} ({cfg.Sn}) — 设备未初始化，跳过");
                            fail++;
                            continue;
                        }

                        try
                        {
                            bool ok = await Task.Run(() =>
                            {
                                if (!dev.IsConnected)
                                {
                                    AppendLog($"  → {cfg.Name} 正在连接...");
                                    if (!dev.Connect())
                                    {
                                        AppendLog($"❌ {cfg.Name} 连接失败");
                                        return false;
                                    }
                                }

                                if (!dev.IsCapturing)
                                {
                                    AppendLog($"  → {cfg.Name} 正在启动采集...");
                                    if (!dev.StartGrabbing())
                                    {
                                        AppendLog($"❌ {cfg.Name} 启动采集失败");
                                        return false;
                                    }
                                }
                                return true;
                            });

                            if (ok)
                            {
                                AppendLog($"✅ {cfg.Name} ({cfg.Sn}) 已开启采集");
                                success++;
                            }
                            else { fail++; }
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"❌ {cfg.Name} 异常: {ex.Message}");
                            fail++;
                        }
                    }
                }

                AppendLog($"━━━━ 完成: 成功 {success} 台, 失败 {fail} 台 ━━━━");
                RefreshStatus();
                
                // 开启所有预览
                StartPreviewLoop();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 批量开启异常: {ex.Message}");
            }
            finally
            {
                _isBatchBusy = false;
                SetButtonsEnabled(true);
            }
        }

        private async void BtnStopAll_Click(object? sender, EventArgs e)
        {
            if (_isBatchBusy) return;
            _isBatchBusy = true;
            SetButtonsEnabled(false);

            try
            {
                AppendLog("━━━━ 一键关闭全部相机 ━━━━");
                int stopped = 0;

                var config = ConfigManager.Instance;

                // 1. 停止所有 2D / 3D 相机采集
                var cameras = config?.Cameras;
                if (cameras != null)
                {
                    foreach (var cfg in cameras)
                    {
                        if (cfg == null || string.IsNullOrEmpty(cfg.Sn)) continue;
                        var dev = DeviceManager.GetCamera(cfg.Sn);
                        if (dev == null || !dev.IsConnected)
                        {
                            AppendLog($"  → {cfg.Name} 未连接，跳过");
                            stopped++;
                            continue;
                        }

                        try
                        {
                            await Task.Run(() =>
                            {
                                if (dev.IsCapturing) dev.StopGrabbing();
                            });
                            AppendLog($"✅ {cfg.Name} ({cfg.Sn}) 已停止采集");
                            stopped++;
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"❌ {cfg.Name} 停止异常: {ex.Message}");
                        }
                    }
                }

                AppendLog($"━━━━ 完成: 已停止 {stopped} 台相机采集 ━━━━");
                RefreshStatus();
                StopPreviewLoop();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 批量关闭异常: {ex.Message}");
            }
            finally
            {
                _isBatchBusy = false;
                SetButtonsEnabled(true);
            }
        }

        private void BtnRefresh_Click(object? sender, EventArgs e)
        {
            RefreshStatus();
        }
    }
}
