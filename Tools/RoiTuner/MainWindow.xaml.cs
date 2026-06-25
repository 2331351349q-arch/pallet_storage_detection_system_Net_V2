using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;

namespace RoiTuner;

public partial class MainWindow : Window
{
    private readonly PointsVisual3D _pointVisual = new() { Color = Colors.Lime, Size = 1.2 };
    private readonly LinesVisual3D _roiVisual = new() { Color = Colors.OrangeRed, Thickness = 2.0 };

    private string _workspaceRoot = string.Empty;
    private string _configPath = string.Empty;
    private AppConfig? _config;
    private DepthFrameData? _currentFrame;

    public MainWindow()
    {
        InitializeComponent();
        viewport.Children.Add(_pointVisual);
        viewport.Children.Add(_roiVisual);

        _workspaceRoot = ResolveWorkspaceRoot();
        _configPath = Path.Combine(_workspaceRoot, "Config", "config.json");
        txtConfigPath.Text = _configPath;

        txtSampleStep.Text = ((int)sldSampleStep.Value).ToString();
        LoadConfigAndRefreshUi();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private string ResolveWorkspaceRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string projectFile = Path.Combine(current, "material_box_storage_detection_system_Net.csproj");
            if (File.Exists(projectFile)) return current;
            current = Directory.GetParent(current)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private void LoadConfigAndRefreshUi()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                _config = new AppConfig();
                SaveConfig();
                AppendLog("未检测到 config.json，已创建默认配置。", false);
            }
            else
            {
                string json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<AppConfig>(json, CreateJsonOptions()) ?? new AppConfig();
            }

            LoadRoiFromCurrentSide();
            UpdateCameraSnFromConfig();
            RedrawRoiAndStats();
            AppendLog("配置加载成功。", false);
        }
        catch (Exception ex)
        {
            AppendLog($"配置加载失败: {ex.Message}", true);
        }
    }

    private void SaveConfig()
    {
        if (_config == null) return;

        string directory = Path.GetDirectoryName(_configPath) ?? string.Empty;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(_config, CreateJsonOptions());
        File.WriteAllText(_configPath, json, Encoding.UTF8);
    }

    private string CurrentSide()
    {
        if (cmbSide.SelectedItem is ComboBoxItem item && item.Content is string text)
        {
            return text;
        }

        return "left";
    }

    private void UpdateCameraSnFromConfig()
    {
        if (_config?.Algorithms?.SlotOccupancy?.CameraMapping == null)
        {
            return;
        }

        var mapping = _config.Algorithms.SlotOccupancy.CameraMapping;
        string side = CurrentSide();
        string sn = side == "right"
            ? (mapping.RightSideSns?.FirstOrDefault() ?? string.Empty)
            : (mapping.LeftSideSns?.FirstOrDefault() ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(sn) && string.IsNullOrWhiteSpace(txtCameraSn.Text))
        {
            txtCameraSn.Text = sn;
        }
    }

    private void LoadRoiFromCurrentSide()
    {
        var roi = ReadRoiFromConfig(CurrentSide());
        txtMinX.Text = roi.MinX.ToString("F0");
        txtMaxX.Text = roi.MaxX.ToString("F0");
        txtMinY.Text = roi.MinY.ToString("F0");
        txtMaxY.Text = roi.MaxY.ToString("F0");
        txtMinZ.Text = roi.MinZ.ToString("F0");
        txtMaxZ.Text = roi.MaxZ.ToString("F0");
    }

    private Roi3D ReadRoiFromConfig(string side)
    {
        var fallback = new Roi3D(-500, 500, -500, 500, 1000, 3000);

        var slot = _config?.Algorithms?.SlotOccupancy;
        if (slot == null) return fallback;

        var list = side == "right" ? slot.Roi3dRight : slot.Roi3dLeft;
        if (list == null || list.Count < 6) return fallback;

        return new Roi3D(list[0], list[1], list[2], list[3], list[4], list[5]);
    }

    private bool TryReadRoiFromText(out Roi3D roi)
    {
        roi = default;
        if (!double.TryParse(txtMinX.Text, out var minX)) return false;
        if (!double.TryParse(txtMaxX.Text, out var maxX)) return false;
        if (!double.TryParse(txtMinY.Text, out var minY)) return false;
        if (!double.TryParse(txtMaxY.Text, out var maxY)) return false;
        if (!double.TryParse(txtMinZ.Text, out var minZ)) return false;
        if (!double.TryParse(txtMaxZ.Text, out var maxZ)) return false;

        roi = new Roi3D(minX, maxX, minY, maxY, minZ, maxZ);
        return true;
    }

    private void RedrawRoiAndStats()
    {
        if (!TryReadRoiFromText(out var roi))
        {
            txtStats.Text = "ROI 输入非法。";
            return;
        }

        DrawRoiCube(roi);

        if (_currentFrame == null)
        {
            txtStats.Text = "当前无深度帧。请先初始化并抓取一帧。";
            return;
        }

        int sampleStep = Math.Max(1, (int)sldSampleStep.Value);
        int countInRoi = CountPointsInRoi(_currentFrame, roi);
        int threshold = _config?.Algorithms?.SlotOccupancy?.PointThreshold ?? 10000;
        bool occupied = countInRoi > threshold;

        txtStats.Text = $"分辨率: {_currentFrame.Width}x{_currentFrame.Height} | " +
                        $"采样步长: {sampleStep}\n" +
                        $"ROI内点数: {countInRoi} | 阈值: {threshold} | 判定: {(occupied ? "有货" : "无货")}";
    }

    private static int CountPointsInRoi(DepthFrameData frame, Roi3D roi)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.DepthRaw == null || frame.DepthRaw.Length == 0)
            return 0;

        // ★ 使用 SDK 原生点云（或回退手动公式），统一经过 GetPointCloud()
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

    private void DrawRoiCube(Roi3D roi)
    {
        var p000 = new Point3D(roi.MinX, -roi.MinY, roi.MinZ);
        var p100 = new Point3D(roi.MaxX, -roi.MinY, roi.MinZ);
        var p010 = new Point3D(roi.MinX, -roi.MaxY, roi.MinZ);
        var p110 = new Point3D(roi.MaxX, -roi.MaxY, roi.MinZ);
        var p001 = new Point3D(roi.MinX, -roi.MinY, roi.MaxZ);
        var p101 = new Point3D(roi.MaxX, -roi.MinY, roi.MaxZ);
        var p011 = new Point3D(roi.MinX, -roi.MaxY, roi.MaxZ);
        var p111 = new Point3D(roi.MaxX, -roi.MaxY, roi.MaxZ);

        var points = new Point3DCollection();
        AddEdge(points, p000, p100);
        AddEdge(points, p100, p110);
        AddEdge(points, p110, p010);
        AddEdge(points, p010, p000);

        AddEdge(points, p001, p101);
        AddEdge(points, p101, p111);
        AddEdge(points, p111, p011);
        AddEdge(points, p011, p001);

        AddEdge(points, p000, p001);
        AddEdge(points, p100, p101);
        AddEdge(points, p110, p111);
        AddEdge(points, p010, p011);

        _roiVisual.Points = points;
    }

    private static void AddEdge(Point3DCollection points, Point3D a, Point3D b)
    {
        points.Add(a);
        points.Add(b);
    }

    private void RenderPointCloud(DepthFrameData frame)
    {
        int sampleStep = Math.Max(1, (int)sldSampleStep.Value);

        if (frame.Width <= 0 || frame.Height <= 0 || frame.DepthRaw == null || frame.DepthRaw.Length == 0)
        {
            _pointVisual.Points = new Point3DCollection();
            return;
        }

        // ★ 使用 SDK 原生点云（或回退手动公式），统一经过 GetPointCloud()
        var cloud = frame.GetPointCloud();
        var points = new Point3DCollection();
        int stride = sampleStep * sampleStep;
        for (int i = 0; i < cloud.Count; i += stride)
        {
            var pt = cloud[i];
            points.Add(new Point3D(pt.X, -pt.Y, pt.Z));
        }

        _pointVisual.Points = points;
        viewport.ZoomExtents();
    }

    private static BitmapSource ConvertBitmapToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        ms.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void AppendLog(string message, bool isError)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {(isError ? "[ERR]" : "[INF]")} {message}{Environment.NewLine}");
        txtLog.ScrollToEnd();
    }

    private static bool IsMainSoftwareRunning()
    {
        try
        {
            string current = Environment.ProcessPath ?? string.Empty;
            string currentName = Path.GetFileNameWithoutExtension(current);
            return System.Diagnostics.Process.GetProcessesByName("material_box_storage_detection_system_Net")
                .Any(p => !string.Equals(p.ProcessName, currentName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void AppendExclusiveUseHintIfNeeded(string sn)
    {
        if (string.IsNullOrWhiteSpace(sn)) return;

        if (IsMainSoftwareRunning())
        {
            AppendLog($"检测到主程序正在运行，3D相机可能被主程序独占: {sn}。如需ROI工具抓图，请先停止主程序或释放相机连接。", true);
        }
    }

    private async void BtnInitCameras_Click(object sender, RoutedEventArgs e)
    {
        if (_config?.Cameras == null || _config.Cameras.Count == 0)
        {
            AppendLog("配置中没有相机信息。", true);
            return;
        }

        string targetSn = txtCameraSn.Text.Trim();
        var candidateConfigs = _config.Cameras
            .Where(c => !string.IsNullOrWhiteSpace(c.Sn)
                        && (c.Type?.Contains("Tycam", StringComparison.OrdinalIgnoreCase) == true
                            || c.Type?.Contains("Percipio", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        if (!string.IsNullOrWhiteSpace(targetSn))
        {
            candidateConfigs = candidateConfigs.Where(c => string.Equals(c.Sn, targetSn, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (candidateConfigs.Count == 0)
        {
            AppendLog("未找到可初始化的3D相机配置（Tycam/Percipio）。", true);
            return;
        }

        btnInitCameras.IsEnabled = false;
        try
        {
            int count = await Task.Run(() => DeviceManager.Initialize(candidateConfigs, msg => Dispatcher.Invoke(() => AppendLog(msg, false))));
            AppendLog($"初始化完成，本次目标设备数: {candidateConfigs.Count}，在线设备总数: {count}", false);

            if (!string.IsNullOrWhiteSpace(targetSn) && DeviceManager.GetCamera(targetSn) == null)
            {
                AppendLog($"目标相机仍不可用: {targetSn}。请检查电源、网线、IP与SN映射。", true);
                AppendExclusiveUseHintIfNeeded(targetSn);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"初始化异常: {ex.Message}", true);
        }
        finally
        {
            btnInitCameras.IsEnabled = true;
        }
    }

    private async void BtnGrab_Click(object sender, RoutedEventArgs e)
    {
        string sn = txtCameraSn.Text.Trim();
        if (string.IsNullOrWhiteSpace(sn))
        {
            AppendLog("请先填写目标相机 SN。", true);
            return;
        }

        var camera = DeviceManager.GetCamera(sn);
        if (camera == null)
        {
            AppendLog($"相机未初始化，尝试自动初始化: {sn}", false);

            var cfg = _config?.Cameras?
                .FirstOrDefault(c => string.Equals(c.Sn, sn, StringComparison.OrdinalIgnoreCase)
                                     && (c.Type?.Contains("Tycam", StringComparison.OrdinalIgnoreCase) == true
                                         || c.Type?.Contains("Percipio", StringComparison.OrdinalIgnoreCase) == true));

            if (cfg == null)
            {
                AppendLog($"配置中未找到该SN对应的3D相机: {sn}", true);
                return;
            }

            try
            {
                await Task.Run(() => DeviceManager.Initialize(new List<CameraConfig> { cfg }, msg => Dispatcher.Invoke(() => AppendLog(msg, false))));
                camera = DeviceManager.GetCamera(sn);
            }
            catch (Exception ex)
            {
                AppendLog($"自动初始化失败: {ex.Message}", true);
                return;
            }
        }

        if (camera == null)
        {
            AppendLog($"未找到已初始化相机: {sn}", true);
            AppendExclusiveUseHintIfNeeded(sn);
            return;
        }

        btnGrab.IsEnabled = false;
        try
        {
            object frameObj = await camera.GrabFrameAsync();
            if (frameObj is not DepthFrameData depthFrame)
            {
                AppendLog("抓取结果不是深度帧，无法进行点云ROI测试。", true);
                return;
            }

            _currentFrame = depthFrame;
            imgPreview.Source = ConvertBitmapToBitmapSource(depthFrame.PreviewImage);

            RenderPointCloud(depthFrame);
            RedrawRoiAndStats();
            AppendLog($"抓取成功: {depthFrame.CameraSn}, {depthFrame.Width}x{depthFrame.Height}", false);
        }
        catch (Exception ex)
        {
            AppendLog($"抓取失败: {ex.Message}", true);
        }
        finally
        {
            btnGrab.IsEnabled = true;
        }
    }

    private void BtnReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        txtCameraSn.Text = string.Empty;
        LoadConfigAndRefreshUi();
    }

    private void BtnSaveRoi_Click(object sender, RoutedEventArgs e)
    {
        if (_config?.Algorithms?.SlotOccupancy == null)
        {
            AppendLog("配置对象无效。", true);
            return;
        }

        if (!TryReadRoiFromText(out var roi))
        {
            AppendLog("ROI 输入格式错误，保存失败。", true);
            return;
        }

        var list = new List<int>
        {
            (int)Math.Round(roi.MinX),
            (int)Math.Round(roi.MaxX),
            (int)Math.Round(roi.MinY),
            (int)Math.Round(roi.MaxY),
            (int)Math.Round(roi.MinZ),
            (int)Math.Round(roi.MaxZ)
        };

        if (CurrentSide() == "right")
        {
            _config.Algorithms.SlotOccupancy.Roi3dRight = list;
        }
        else
        {
            _config.Algorithms.SlotOccupancy.Roi3dLeft = list;
        }

        try
        {
            SaveConfig();
            AppendLog($"已保存 {CurrentSide()} ROI 到 config.json", false);
        }
        catch (Exception ex)
        {
            AppendLog($"保存失败: {ex.Message}", true);
        }
    }

    private void CmbSide_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        txtCameraSn.Text = string.Empty;
        UpdateCameraSnFromConfig();
        LoadRoiFromCurrentSide();
        RedrawRoiAndStats();
    }

    private void RoiText_LostFocus(object sender, RoutedEventArgs e)
    {
        RedrawRoiAndStats();
    }

    private void SldSampleStep_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (txtSampleStep != null)
        {
            txtSampleStep.Text = ((int)e.NewValue).ToString();
        }

        if (_currentFrame != null)
        {
            RenderPointCloud(_currentFrame);
            RedrawRoiAndStats();
        }
    }

    private readonly record struct Roi3D(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);
}
