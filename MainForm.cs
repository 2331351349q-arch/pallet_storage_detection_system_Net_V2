using material_box_storage_detection_system_Net.Communication;
using material_box_storage_detection_system_Net.Control;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;

namespace material_box_storage_detection_system_Net
{
    /// <summary>
    /// 主界面类，负责全系统的可视化呈现、用户交互及核心组件的生命周期绑定。
    /// </summary>
    public partial class MainForm : Form
    {
        private RedisCommunicator _redis;
        private TaskManager _manager;
        private System.Threading.CancellationTokenSource _ctsLiveLeft;
        private System.Threading.CancellationTokenSource _ctsLiveRight;
        private ToolHubForm? _toolHubForm;
        private readonly Dictionary<int, Label> _cameraLabels = new();

        /// <summary>
        /// 构造函数：初始化主界面，设置列表框样式并最大化窗口。
        /// （1）SetupListBox配置日志列表框的 UI 表现与上下文菜单。
        /// （2）ListBox_Log_KeyDown：处理列表框的快捷键（Ctrl+C, Ctrl+A）。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            SetupListBox();
            this.WindowState = FormWindowState.Maximized;
            DisableControlButtons();
        }
        private void SetupListBox()
        {
            listBox_Log.BackColor = System.Drawing.Color.White;
            listBox_Log.ForeColor = System.Drawing.Color.FromArgb(30, 30, 35);
            listBox_Log.Font = new System.Drawing.Font("Microsoft YaHei UI", 10.5F, System.Drawing.FontStyle.Regular);
            listBox_Log.KeyDown += ListBox_Log_KeyDown;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var copySelected = new System.Windows.Forms.ToolStripMenuItem("复制单条日志");
            copySelected.Click += (s, e) =>
            {
                if (listBox_Log.SelectedItem != null)
                    System.Windows.Forms.Clipboard.SetText(listBox_Log.SelectedItem.ToString());
            };

            var copyAll = new System.Windows.Forms.ToolStripMenuItem("复制全部日志");
            copyAll.Click += (s, e) =>
            {
                if (listBox_Log.Items.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in listBox_Log.Items) sb.AppendLine(item.ToString());
                    System.Windows.Forms.Clipboard.SetText(sb.ToString());
                }
            };

            contextMenu.Items.Add(copySelected);
            contextMenu.Items.Add(copyAll);
            listBox_Log.ContextMenuStrip = contextMenu;
        }
        private void ListBox_Log_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == System.Windows.Forms.Keys.C)
            {
                if (listBox_Log.SelectedItem != null)
                    System.Windows.Forms.Clipboard.SetText(listBox_Log.SelectedItem.ToString());
            }
            else if (e.Control && e.KeyCode == System.Windows.Forms.Keys.A)
            {
                if (listBox_Log.Items.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in listBox_Log.Items) sb.AppendLine(item.ToString());
                    System.Windows.Forms.Clipboard.SetText(sb.ToString());
                }
            }
        }

        /// <summary>
        /// 禁用所有功能按钮，防止相机初始化前误操作。
        /// </summary>
        private void DisableControlButtons()
        {
            if (btn_LiveLeft != null) btn_LiveLeft.Enabled = false;
            if (btn_LiveRight != null) btn_LiveRight.Enabled = false;
            if (btn_LiveCodeReader != null) btn_LiveCodeReader.Enabled = false;
            if (btn_ToolHub != null) btn_ToolHub.Enabled = false;
        }

        /// <summary>
        /// 启用所有功能按钮，供相机初始化完成后调用。
        /// </summary>
        private void EnableControlButtons()
        {
            if (btn_LiveLeft != null) btn_LiveLeft.Enabled = true;
            if (btn_LiveRight != null) btn_LiveRight.Enabled = true;
            if (btn_LiveCodeReader != null) btn_LiveCodeReader.Enabled = true;
            if (btn_ToolHub != null) btn_ToolHub.Enabled = true;
        }

        /// <summary>
        /// 窗体加载事件：执行 Redis 连接、硬件初始化及业务引擎启动。
        /// </summary>
        private async void MainForm_Load(object sender, EventArgs e)
        {
            _redis = new RedisCommunicator();
            _manager = new TaskManager(_redis);

            // 01、订阅日志与图像更新事件，以便在 UI 上进行实时反馈。
            _manager.OnLogMessage += Manager_OnLogMessage;
            _manager.OnImageUpdated += Manager_OnImageUpdated;

            var config = ConfigManager.Instance;
            string host = config.Redis?.Host ?? "127.0.0.1";
            string portText = config.Redis?.Port ?? "6379";
            string pwd = config.Redis?.Password ?? "";
            int taskDb = config.Redis?.TaskDb ?? 0;
            int resultDb = config.Redis?.ResultDb ?? 1;
            int port = int.TryParse(portText, out int parsedPort) ? parsedPort : 6379;
            //02、连接Redis并判断是否连接成功？
            bool redisConnected = _redis.Connect(host, port, pwd, taskDb, resultDb);
            
            if (!redisConnected)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Redis 连接失败!\n请检查 config.yaml 以及本地 Redis 服务是否启动。",
                    "严重错误",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            //03、异步初始化Redis：清理残留任务触发状态。
            await _redis.ClearTaskKeysAsync();
            Manager_OnLogMessage("✔ Redis 连接成功，已清理残留任务状态。");



            //04、异步初始化硬件，防止阻断 UI 响应。
            Manager_OnLogMessage("[-] 系统正在初始化相机阵列，请稍候...");
            await Task.Run(() =>
            {
                int camCount = DeviceManager.Initialize(config.Cameras, Manager_OnLogMessage);
                int readerCount = CodeReaderService.TestConnection(Manager_OnLogMessage);
                if (readerCount > 0)
                {
                    Manager_OnLogMessage("[-] 正在预热读码器，消除首次启动延迟...");
                    // 主动触发一次取流并停止，提前完成设备句柄创建与连接，避免首次 flag=4 耗时过长
                    CodeReaderService.StartScan(_ => { });
                    CodeReaderService.StopScan();
                }
                Manager_OnLogMessage($"🚀 所有硬件检测完毕，共成功装载并点亮了 {camCount + readerCount} 台相机及读码设备");
            });

            //05、依次启动业务队列处理器与外部监听服务。
            _manager.Start();
            _redis.StartListening();

            Manager_OnLogMessage("系统启动完毕: 业务引擎已就绪。");
            
            // 06、相机初始化完成，启用所有功能按钮。
            EnableControlButtons();

            // 07、给每个 PictureBox 添加相机 SN 标签
            SetupCameraLabels();
        }

        /// <summary>
        /// 窗体关闭时，确保后台线程与 SDK 资源得到正确释放。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _ctsLiveLeft?.Cancel();
            _ctsLiveRight?.Cancel();
            _redis?.StopListening();
            _manager?.Stop();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 跨线程安全地将日志追加到列表框。
        /// </summary>
        /// <param name="message">日志文本。</param>
        private void Manager_OnLogMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(Manager_OnLogMessage), message);
                return;
            }

            listBox_Log.Items.Add(message);
            listBox_Log.TopIndex = listBox_Log.Items.Count - 1;
        }

        /// <summary>
        /// 跨线程安全地更新 PictureBox 显示获取的图像。
        /// </summary>
        /// <param name="cameraIndex">相机槽位索引。</param>
        /// <param name="imageObj">Bitmap 图像对象。</param>
        private void Manager_OnImageUpdated(int cameraIndex, object imageObj)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<int, object>(Manager_OnImageUpdated), cameraIndex, imageObj);
                return;
            }

            System.Drawing.Image? displayImage = null;
            if (imageObj is material_box_storage_detection_system_Net.Devices.DepthFrameData depthFrame)
            {
                displayImage = depthFrame.PreviewImage;
            }
            else if (imageObj is System.Drawing.Image img)
            {
                displayImage = img;
            }

            if (displayImage != null)
            {
                if (cameraIndex == 1)
                {
                    pictureBox_Camera1.Image?.Dispose();
                    pictureBox_Camera1.Image = (System.Drawing.Image)displayImage.Clone();
                }
                else if (cameraIndex == 2)
                {
                    pictureBox_Camera2.Image?.Dispose();
                    pictureBox_Camera2.Image = (System.Drawing.Image)displayImage.Clone();
                }
                else if (cameraIndex == 3)
                {
                    pictureBox_Camera3.Image?.Dispose();
                    pictureBox_Camera3.Image = (System.Drawing.Image)displayImage.Clone();
                }
                else if (cameraIndex == 4)
                {
                    pictureBox_CodeReader.Image?.Dispose();
                    pictureBox_CodeReader.Image = (System.Drawing.Image)displayImage.Clone();
                }
            }
        }

        /// <summary>
        /// 打开配置管理对话框。
        /// </summary>
        private void btn_Config_Click(object sender, EventArgs e)
        {
            using (var configForm = new ConfigForm())
            {
                if (configForm.ShowDialog() == DialogResult.OK)
                {
                    Manager_OnLogMessage("✔ [配置已更新] 核心参数已刷新并应用。");
                }
            }
        }

        private void btn_LiveLeft_Click(object sender, EventArgs e)
        {
            // 互斥，停掉右侧
            if (_ctsLiveRight != null && !_ctsLiveRight.IsCancellationRequested)
            {
                _ctsLiveRight.Cancel();
                btn_LiveRight.Text = "▶ 右侧实时预览 (2台)";
                btn_LiveRight.ForeColor = Color.FromArgb(30, 50, 80);
                Manager_OnLogMessage("⏹ 已自动停止右侧实时预览。");
            }

            if (_ctsLiveLeft != null && !_ctsLiveLeft.IsCancellationRequested)
            {
                // 关闭当前侧
                _ctsLiveLeft.Cancel();
                btn_LiveLeft.Text = "▶ 左侧实时预览 (2台)";
                btn_LiveLeft.ForeColor = Color.FromArgb(30, 50, 80);
                Manager_OnLogMessage("⏹ 已手动停止左侧实时预览。");
            }
            else
            {
                // 开启当前侧
                _ctsLiveLeft = new System.Threading.CancellationTokenSource();
                btn_LiveLeft.Text = "⏹ 停止左侧预览";
                btn_LiveLeft.ForeColor = Color.Red;
                string[] sns = GetPreviewSnsForSide("left");
                UpdateCameraLabels("left");
                Manager_OnLogMessage($"▶ 正在开启左侧 {sns.Length} 路相机实时连续采集...");
                Task.Run(() => StartLivePreviewAsync(sns, _ctsLiveLeft.Token));
            }
        }

        private void btn_LiveRight_Click(object sender, EventArgs e)
        {
            // 互斥，停掉左侧
            if (_ctsLiveLeft != null && !_ctsLiveLeft.IsCancellationRequested)
            {
                _ctsLiveLeft.Cancel();
                btn_LiveLeft.Text = "▶ 左侧实时预览 (2台)";
                btn_LiveLeft.ForeColor = Color.FromArgb(30, 50, 80);
                Manager_OnLogMessage("⏹ 已自动停止左侧实时预览。");
            }

            if (_ctsLiveRight != null && !_ctsLiveRight.IsCancellationRequested)
            {
                // 关闭当前侧
                _ctsLiveRight.Cancel();
                btn_LiveRight.Text = "▶ 右侧实时预览 (2台)";
                btn_LiveRight.ForeColor = Color.FromArgb(30, 50, 80);
                Manager_OnLogMessage("⏹ 已手动停止右侧实时预览。");
            }
            else
            {
                // 开启当前侧
                _ctsLiveRight = new System.Threading.CancellationTokenSource();
                btn_LiveRight.Text = "⏹ 停止右侧预览";
                btn_LiveRight.ForeColor = Color.Red;
                string[] sns = GetPreviewSnsForSide("right");
                UpdateCameraLabels("right");
                Manager_OnLogMessage($"▶ 正在开启右侧 {sns.Length} 路相机实时连续采集...");
                Task.Run(() => StartLivePreviewAsync(sns, _ctsLiveRight.Token));
            }
        }

        private string[] GetPreviewSnsForSide(string side)
        {
            var merged = new List<string>();

            void AddUnique(List<string> sns)
            {
                if (sns == null) return;
                foreach (var sn in sns)
                {
                    if (!string.IsNullOrWhiteSpace(sn) && !merged.Contains(sn))
                        merged.Add(sn);
                }
            }

            // 3D 相机: 来自 StackerOffset (Flag=2) 配置
            AddUnique(ConfigManager.GetTargetCameraSNs(2, side));

            // 2D 相机: 从全局相机列表中按类型筛选，根据命名规则匹配侧位
            var globalCameras = ConfigManager.Instance?.Cameras;
            if (globalCameras != null)
            {
                foreach (var cam in globalCameras)
                {
                    if (cam.Type == "Hikvision2D" && !string.IsNullOrWhiteSpace(cam.Name))
                    {
                        // 2DCam#2 → left, 2DCam#1 → right
                        bool isLeft = side == "left" && cam.Name.Contains("2DCam#2");
                        bool isRight = side == "right" && cam.Name.Contains("2DCam#1");
                        if (isLeft || isRight)
                        {
                            if (!merged.Contains(cam.Sn))
                                merged.Add(cam.Sn);
                        }
                    }
                }
            }

            return merged.ToArray();
        }

        private async Task StartLivePreviewAsync(string[] sns, System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tasks = new List<Task>();
                    for (int i = 0; i < sns.Length; i++)
                    {
                        var cam = DeviceManager.GetCamera(sns[i]);
                        if (cam != null && cam.IsConnected)
                        {
                            int index = i + 1; // PictureBox index 1-4
                            tasks.Add(cam.GrabFrameAsync().ContinueWith(t => 
                            {
                                if (t.Result != null && !token.IsCancellationRequested)
                                    Manager_OnImageUpdated(index, t.Result);
                            }));
                        }
                    }
                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(100, token); // 控制 ~10FPS 避免界面卡死
                }
                catch (TaskCanceledException) { break; }
            }
        }

        private bool _isLiveCodeReader = false;
        private bool _isCodeReaderBusy = false;
        private async void btn_LiveCodeReader_Click(object sender, EventArgs e)
        {
            if (_isCodeReaderBusy) return;

            if (!_isLiveCodeReader)
            {
                // 先切按钮状态为"启动中"，防止重复点击
                _isCodeReaderBusy = true;
                btn_LiveCodeReader.Enabled = false;
                btn_LiveCodeReader.Text = "⏳ 正在启动读码器...";
                btn_LiveCodeReader.ForeColor = Color.Orange;
                Manager_OnLogMessage("▶ 正在开启读码器实时预览...");

                // 在后台线程初始化读码器，避免阻塞 UI
                bool started = await Task.Run(() =>
                    CodeReaderService.StartScan(
                        img => Manager_OnImageUpdated(4, img),
                        Manager_OnLogMessage));

                _isLiveCodeReader = started;
                _isCodeReaderBusy = false;
                btn_LiveCodeReader.Enabled = true;

                if (started)
                {
                    btn_LiveCodeReader.Text = "⏹ 停止读码器";
                    btn_LiveCodeReader.ForeColor = Color.Red;
                }
                else
                {
                    btn_LiveCodeReader.Text = "▶ 读码器实时采集";
                    btn_LiveCodeReader.ForeColor = Color.FromArgb(30, 50, 80);
                }
            }
            else
            {
                _isLiveCodeReader = false;
                btn_LiveCodeReader.Text = "▶ 读码器实时采集";
                btn_LiveCodeReader.ForeColor = Color.FromArgb(30, 50, 80);
                try
                {
                    CodeReaderService.StopScan();
                    Manager_OnLogMessage("⏹ 已停止读码器实时预览。");
                }
                catch (Exception ex)
                {
                    Manager_OnLogMessage($"❌ 停止读码器实时预览异常: {ex.Message}");
                }
            }
        }

        private void SetupCameraLabels()
        {
            const int labelHeight = 22;

            void Wrap(PictureBox pb, int col, int row, int index)
            {
                tableLayoutPanel_Images.Controls.Remove(pb);

                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(4),
                    BackColor = Color.FromArgb(30, 30, 40)
                };

                var label = new Label
                {
                    Dock = DockStyle.Top,
                    Height = labelHeight,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.FromArgb(200, 15, 15, 35),
                    ForeColor = Color.FromArgb(200, 210, 255),
                    Font = new Font("Consolas", 9F, FontStyle.Regular),
                    Padding = new Padding(8, 0, 0, 0)
                };

                pb.Dock = DockStyle.Fill;
                panel.Controls.Add(pb);
                panel.Controls.Add(label);

                _cameraLabels[index] = label;
                tableLayoutPanel_Images.Controls.Add(panel, col, row);
            }

            Wrap(pictureBox_Camera1, 0, 0, 1);
            Wrap(pictureBox_Camera2, 1, 0, 2);
            Wrap(pictureBox_Camera3, 0, 1, 3);
            Wrap(pictureBox_CodeReader, 1, 1, 4);

            // 读码器 SN 固定
            var readerSn = ConfigManager.Instance?.CodeReader?.SerialNumber ?? "N/A";
            _cameraLabels[4].Text = $"  CodeReader   {readerSn}";
        }

        private string GetCameraDisplayName(string sn)
        {
            var cams = ConfigManager.Instance?.Cameras;
            if (cams != null)
            {
                foreach (var cam in cams)
                {
                    if (cam.Sn == sn)
                        return cam.Name;
                }
            }
            return sn; // fallback: 直接显示 SN
        }

        private void UpdateCameraLabels(string side)
        {
            var sns = GetPreviewSnsForSide(side);
            for (int i = 0; i < sns.Length && i < 3; i++)
            {
                if (_cameraLabels.TryGetValue(i + 1, out var label))
                {
                    string name = GetCameraDisplayName(sns[i]);
                    label.Text = $"  {name}   {sns[i]}";
                }
            }
        }

        private void btn_ToolHub_Click(object sender, EventArgs e)
        {
            if (_toolHubForm == null || _toolHubForm.IsDisposed)
            {
                _toolHubForm = new ToolHubForm();
                _toolHubForm.FormClosed += (_, __) => _toolHubForm = null;
                _toolHubForm.Show(this);
                return;
            }

            if (!_toolHubForm.Visible)
            {
                _toolHubForm.Show(this);
            }

            _toolHubForm.BringToFront();
            _toolHubForm.Focus();
        }
    }
}

