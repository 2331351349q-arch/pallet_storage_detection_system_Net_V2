using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UCV.CameraApi.Interop;
using UCV.CameraApi.Interop.Utils;
using UCV.DataModel.Interop;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace material_box_storage_detection_system_Net.Devices
{
    /// <summary>
    /// 图漾 (Percipio/Tycam) 3D 相机驱动实现类。
    /// 基于 UCV Interop SDK 构建，支持深度图采集、OpenCV 伪彩色预处理及异步超时机制。
    /// </summary>
    public class PercipioCamera : ICameraDevice
    {
        private CameraCapture _device;
        private TaskCompletionSource<object> _frameTcs;
        private object? _lastFrameInfo; // 缓存最新帧的 .Info 用于元数据探索
        private (double fx, double fy, double cx, double cy)? _cachedIntrinsics; // 内参缓存（只需提取一次）
        private static bool _isSdkInitialized = false;

        /// <summary>
        /// 目标设备的唯一序列号。
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// 设备是否已经由于 Connect 成功进入就绪状态。
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// 设备是否当前正处于采集线程运行中。
        /// </summary>
        public bool IsCapturing { get; private set; } = false;

        /// <summary>
        /// 实例化图漾相机驱动对象。
        /// 内部包含全局单例化的 SDK 环境初始化逻辑。
        /// </summary>
        /// <param name="sn">相机真实 SN 序列号。</param>
        public PercipioCamera(string sn)
        {
            SerialNumber = sn;
            // 确保 SDK 仅全局初始化一次，防止重复调用导致句柄泄露或崩溃。
            if (!_isSdkInitialized)
            {
                var initStatus = CameraUtils.Init(true);
                if (initStatus != CameraApiStatus.Success)
                {
                    initStatus = CameraUtils.Init(false);
                }
                // 官方说明：获取实例前必须显式执行一次 Discover。
                CameraUtils.DiscoverCameras(); 
                _isSdkInitialized = true;
            }
        }

        /// <summary>
        /// 在总线上检索匹配的 SN，建立逻辑连接并配置传感器流参数。
        /// </summary>
        /// <returns>连接结果。</returns>
        public bool Connect()
        {
            if (!_isSdkInitialized) throw new Exception("【致命】TuYang SDK未能成功启用.");

            // 扫描当前拓扑结构并刷新列表。
            var camInfos = CameraUtils.DiscoverCameras();

            // 获取具体的相机实例。
            _device = CameraCapture.GetCameraBySerialNumber(SerialNumber);
            if (_device == null)
            {
                string foundSns = string.Join(", ", System.Linq.Enumerable.Select(camInfos, c => c.Sn));
                throw new Exception($"未从总线扫描到该 SN 码。目前扫描到的硬件有 {camInfos.Count} 台: [{foundSns}]");
            }

            var status = _device.Connect();
            if (status != CameraApiStatus.Success)
            {
                throw new Exception($"CameraConnect API 遇到底层错误: {status}");
            }

            _device.StopCapture(); // 幂等操作，确保启动前处于干净状态。
            
            // 默认生产环境仅开启深度图传感器。
            _device.SetSensorEnabled(SensorType.Depth, true);

            // 配置连续采集模式 (Continuous)。
            CameraFeature feat;
            if (_device.GetFeature("AcquisitionMode", out feat) == CameraApiStatus.Success)
            {
                feat.SetValue(new CameraValue(CameraValueType.Int32, 2)); 
            }

            // 注册底层 C++ 回调事件。
            _device.FrameSetReceived += Device_FrameSetReceived;
            IsConnected = true;
            Console.WriteLine($"相机 {SerialNumber} 连接成功。");
            return true;
        }

        /// <summary>
        /// 关闭底层流采集并退订事件，安全断开物理连接。
        /// </summary>
        public void Disconnect()
        {
            if (IsConnected && _device != null)
            {
                StopGrabbing();
                _device.FrameSetReceived -= Device_FrameSetReceived;
                _device.Disconnect();
                IsConnected = false;
                Console.WriteLine($"相机 {SerialNumber} 断开连接。");
            }
        }

        /// <summary>
        /// 开启硬件取流引擎。
        /// </summary>
        public bool StartGrabbing()
        {
            if (!IsConnected || _device == null) return false;
            if (IsCapturing) return true;

            var status = _device.StartCapture();
            if (status == CameraApiStatus.Success)
            {
                IsCapturing = true;
                return true;
            }
            
            Console.WriteLine($"相机 {SerialNumber} 开启采集推流失败: {status}");
            return false;
        }

        /// <summary>
        /// 停止硬件取流。
        /// </summary>
        public void StopGrabbing()
        {
            if (!IsCapturing || _device == null) return;
            
            _device.StopCapture();
            IsCapturing = false;
        }

        /// <summary>
        /// 图漾 SDK 的底层的异步回调处理。
        /// 负责原始 16位深度数据的转换、归一化、伪彩色映射（OpenCV）及结果注入。
        /// </summary>
        private void Device_FrameSetReceived(object sender, FrameSetReceivedEventArgs e)
        {
            // 性能优化：如果没有上层 Task 正在等待该图像，直接放弃后续昂贵的 OpenCV 转换。
            if (_frameTcs == null || _frameTcs.Task.IsCompleted) return;

            try
            {
                var frameSet = e.FrameSet;
                var frame = frameSet.GetFrame(SensorType.Depth);
                if (frame == null || frame.Image == null) return;

                // 缓存帧元数据，供内参读取等场景使用
                try { _lastFrameInfo = frame.Info; } catch { }

                var image = frame.Image;
                IntPtr rawDataPtr = image.GetData();
                int width = (int)image.Width;
                int height = (int)image.Height;
                int depthCount = width * height;

                // 原始 16bit 深度拷贝（单位 mm）
                short[] signedDepth = new short[depthCount];
                Marshal.Copy(rawDataPtr, signedDepth, 0, depthCount);
                ushort[] rawDepth = new ushort[depthCount];
                Buffer.BlockCopy(signedDepth, 0, rawDepth, 0, depthCount * sizeof(ushort));

                // 使用 OpenCVSharp 进行高性能预处理：
                // 1. 将物理缓存 16位无符号整型映射为 CV_16UC1 矩阵。
                using var depthMat = Mat.FromPixelData(height, width, MatType.CV_16UC1, rawDataPtr).Clone();

                // 2. 将检测到的 16位原始深度范围通过归一化（Normalize）压缩到 0-255 范围。
                using var grayMat = new Mat();
                Cv2.Normalize(depthMat, depthMat, 0, 255, NormTypes.MinMax);
                depthMat.ConvertTo(grayMat, MatType.CV_8UC1);

                // 3. 应用 Jet 伪彩色查找表，将单通道深度图转为彩色，方便肉眼识别障碍。
                using var colorMat = new Mat();
                Cv2.ApplyColorMap(grayMat, colorMat, ColormapTypes.Jet);

                // 4. 将处理后的彩色 Mat 转换为托管位图。
                Bitmap bmp = BitmapConverter.ToBitmap(colorMat);

                var depthFrame = new DepthFrameData(SerialNumber, width, height, rawDepth, bmp);

                // ★ 方案A：尝试从帧元数据中提取 SDK 内置内参（只需成功一次，后续复用缓存）
                if (_cachedIntrinsics == null)
                {
                    try
                    {
                        var intrinsicData = TryReadIntrinsicFromFrameMetadata(msg => { });
                        if (intrinsicData != null && intrinsicData.Length >= 4)
                        {
                            _cachedIntrinsics = ParseIntrinsicLayout(msg => { }, intrinsicData);
                            if (_cachedIntrinsics.HasValue)
                                Console.WriteLine($"[内参] 从帧元数据提取成功并缓存: fx={_cachedIntrinsics.Value.fx:F2} fy={_cachedIntrinsics.Value.fy:F2} cx={_cachedIntrinsics.Value.cx:F2} cy={_cachedIntrinsics.Value.cy:F2}");
                        }
                    }
                    catch { /* 提取失败不影响主流 */ }
                }
                depthFrame.Intrinsics = _cachedIntrinsics;

                // ★ 方案A升级：调用 SDK 原生 DepthImageToPoint3d 生成点云，替代手动公式
                try
                {
                    var status = ImageProc.DepthImageToPoint3d(frame, out PointCloud pointCloud);
                    if (status == CameraApiStatus.Success && pointCloud != null)
                    {
                        int pointCount = (int)pointCloud.GetPointCount();
                        int floatCount = pointCount * 3;
                        float[] packedXyz = new float[floatCount];
                        Marshal.Copy(pointCloud.GetData(), packedXyz, 0, floatCount);
                        depthFrame.SetSdkPointCloud(packedXyz, pointCount);
                    }
                }
                catch (Exception ex)
                {
                    // SDK 点云转换失败，回退到手动公式
                    Console.WriteLine($"[点云] SDK DepthImageToPoint3d 异常: {ex.Message}，将使用手动公式");
                }

                // 将预览图 + 原始深度统一封装后回传。
                _frameTcs.TrySetResult(depthFrame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理 {SerialNumber} 深度图回调异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从相机 SDK 读取出厂标定的内参 (fx, fy, cx, cy)，失败返回 null。
        /// </summary>
        public (double fx, double fy, double cx, double cy)? GetIntrinsics()
        {
            return GetIntrinsicsWithLog(null);
        }

        /// <summary>
        /// 带诊断日志的内参读取。log 委托同时输出到 Console 和 UI。
        /// </summary>
        public (double fx, double fy, double cx, double cy)? GetIntrinsicsWithLog(Action<string>? log)
        {
            void L(string msg) { log?.Invoke(msg); Console.WriteLine(msg); }

            if (_device == null || !IsConnected) { L("[内参] _device 为 null 或未连接"); return null; }

            try
            {
                L("[内参] === 开始读取内参 ===");
                L($"[内参] 相机 SN={SerialNumber}, IsCapturing={IsCapturing}");

                // 步骤1: 枚举相机所有可读特征名
                var availableFeatures = EnumerateFeatures(L);

                // 步骤2: 尝试多种路径读取内参
                float[]? data = null;

                // 路径A: 标准 Intrinsic/Intrinsic2 + SourceSelector
                data = TryReadIntrinsicFeature(L, availableFeatures, "Intrinsic", "Intrinsic2", useSelector: true);
                if (data == null)
                {
                    // 路径B: 不带 SourceSelector 直接读 (某些固件不需要)
                    data = TryReadIntrinsicFeature(L, availableFeatures, "Intrinsic", "Intrinsic2", useSelector: false);
                }
                if (data == null)
                {
                    // 路径C: 读取 CalibrationData 完整标定块
                    data = TryReadCalibrationData(L);
                }

                if (data == null || data.Length < 4)
                {
                    // 路径E: 探索深度帧元数据 .Info
                    data = TryReadIntrinsicFromFrameMetadata(L);
                }

                if (data == null || data.Length < 4)
                {
                    // 路径F: 读取标量特征 (IntrinsicWidth/IntrinsicHeight/DepthScaleUnit)
                    var scalarResult = TryReadScalarIntrinsicFeatures(L);
                    if (scalarResult.HasValue)
                    {
                        L($"[内参] 标量特征路径成功!");
                        return scalarResult;
                    }
                }

                if (data == null || data.Length < 4)
                {
                    L($"[内参] 所有路径均未能获取到足够内参数据 (len={data?.Length ?? 0})");
                    return null;
                }

                var preview = string.Join(", ", data.Take(Math.Min(12, data.Length)).Select(f => f.ToString("F3")));
                L($"[内参] 原始数据({data.Length}): [{preview}]");

                // 步骤3: 解析内参布局
                var result = ParseIntrinsicLayout(L, data);
                if (result.HasValue)
                {
                    L($"[内参] ✓ 成功! fx={result.Value.fx:F2} fy={result.Value.fy:F2} cx={result.Value.cx:F2} cy={result.Value.cy:F2}");
                }
                return result;
            }
            catch (Exception ex)
            {
                L($"[内参] 异常: {ex.GetType().Name}: {ex.Message}");
                L($"[内参] 堆栈: {ex.StackTrace}");
            }

            return null;
        }

        // ---- 内参读取辅助方法 ----

        /// <summary>枚举可用的相关特征名</summary>
        private HashSet<string> EnumerateFeatures(Action<string> L)
        {
            var names = new HashSet<string>();
            try
            {
                foreach (var name in new[] {
                    "DeviceSerialNumber", "DeviceModelName", "DeviceVendorName",
                    "Intrinsic", "Intrinsic2", "IntrinsicWidth", "IntrinsicHeight",
                    "SensorWidth", "SensorHeight", "SourceSelector", "ComponentSelector",
                    "CalibrationData", "Distortion", "Extrinsic" })
                {
                    if (_device!.GetFeature(name, out _) == CameraApiStatus.Success)
                        names.Add(name);
                }
                L($"[内参] 可用特征: [{string.Join(", ", names)}]");
            }
            catch (Exception ex) { L($"[内参] 枚举特征异常: {ex.Message}"); }
            return names;
        }

        /// <summary>路径A/B: 读取 Intrinsic / Intrinsic2 特征</summary>
        private float[]? TryReadIntrinsicFeature(Action<string> L, HashSet<string> available,
            string featureName, string fallbackName, bool useSelector)
        {
            if (!available.Contains(featureName) && !available.Contains(fallbackName))
            {
                L($"[内参] 特征 {featureName}/{fallbackName} 均不可用");
                return null;
            }

            // 可选: 设置 SourceSelector
            if (useSelector && available.Contains("SourceSelector"))
            {
                string[] sourceValues = { "Depth", "Left", "Right" };
                bool ok = false;
                foreach (var src in sourceValues)
                {
                    var s1 = _device!.GetFeature("SourceSelector", out CameraFeature selFeat);
                    if (s1 != CameraApiStatus.Success) continue;
                    try
                    {
                        var strVal = new CameraValue(CameraValueType.String, src);
                        var s2 = selFeat.SetValue(strVal);
                        if (s2 == CameraApiStatus.Success || s2.ToString() == "Opened")
                        {
                            L($"[内参] SourceSelector 设为 \"{src}\" ✓ (返回: {s2})");
                            ok = true;
                            break;
                        }
                        else
                            L($"[内参] SetValue(\"{src}\") 返回 {s2}");
                    }
                    catch (Exception ex) { L($"[内参] SetValue 异常: {ex.Message}"); }
                }
                if (!ok)
                    L($"[内参] SourceSelector 设置失败，继续尝试读取");
            }
            else if (useSelector && !available.Contains("SourceSelector"))
            {
                L("[内参] 设备不支持 SourceSelector，直接读取");
            }
            else if (!useSelector)
            {
                L("[内参] 跳过 SourceSelector，直接读取特征");
            }

            // 获取 CameraFeature
            CameraFeature feat;
            var r1 = _device!.GetFeature(featureName, out feat);
            if (r1 != CameraApiStatus.Success)
            {
                L($"[内参] GetFeature(\"{featureName}\") 返回 {r1}, 回退 {fallbackName}");
                r1 = _device.GetFeature(fallbackName, out feat);
                if (r1 != CameraApiStatus.Success)
                {
                    L($"[内参] GetFeature(\"{fallbackName}\") 也返回 {r1}");
                    return null;
                }
                L($"[内参] 使用回退特征 {fallbackName}");
            }

            // 诊断: 输出特征类型信息
            try
            {
                CameraFeatureAccessMode accessMode;
                feat.GetAccessMode(out accessMode);
                L($"[内参] FeatureType={feat.FeatureType}, AccessMode={accessMode}");
            }
            catch (Exception ex) { L($"[内参] 特征类型诊断异常: {ex.Message}"); }

            // GetValue
            var r2 = feat.GetValue(out CameraValue camVal);
            if (r2 != CameraApiStatus.Success && r2.ToString() != "Opened")
            {
                L($"[内参] GetValue() 返回 {r2}");
                return null;
            }
            L($"[内参] GetValue() 返回: {r2}, ValueType={camVal.ValueType}");

            // 尝试多种方式提取数据
            var data = ExtractDataFromCameraValue(L, camVal);
            if (data != null && data.Length >= 4)
                return data;

            // 路径D: CameraFeature 反射 — 查找隐藏的寄存器读取方法
            L("[内参] CameraValue 提取失败，尝试 CameraFeature 反射探索");
            data = TryReadFromFeatureRegisters(L, feat);
            if (data != null && data.Length >= 4)
                return data;

            return null;
        }

        /// <summary>路径C: 读取 CalibrationData 完整标定数据</summary>
        private float[]? TryReadCalibrationData(Action<string> L)
        {
            L("[内参] 尝试路径C: CalibrationData");

            var r1 = _device!.GetFeature("CalibrationData", out CameraFeature feat);
            if (r1 != CameraApiStatus.Success)
            {
                L($"[内参] CalibrationData 特征不可用 ({r1})");
                return null;
            }

            try
            {
                CameraFeatureAccessMode accessMode;
                feat.GetAccessMode(out accessMode);
                L($"[内参] CalibrationData FeatureType={feat.FeatureType}, AccessMode={accessMode}");
            }
            catch { }

            var r2 = feat.GetValue(out CameraValue camVal);
            if (r2 != CameraApiStatus.Success && r2.ToString() != "Opened")
            {
                L($"[内参] CalibrationData GetValue() 返回 {r2}");
                return null;
            }
            L($"[内参] CalibrationData GetValue() 返回: {r2}, ValueType={camVal.ValueType}");

            // 先尝试标准提取
            var data = ExtractDataFromCameraValue(L, camVal);
            if (data != null && data.Length >= 4)
            {
                L($"[内参] CalibrationData 提取到 {data.Length} floats");
                // CalibrationData 通常包含更完整的数据，内参在 data[0:6] 区域
                return data;
            }

            // 如果标准提取失败，用反射深入探索
            L("[内参] CalibrationData 标准提取失败，使用反射深入探索");
            return ExtractDataViaReflection(L, camVal);
        }

        /// <summary>路径D: 通过反射探索 CameraFeature 的隐藏寄存器读取方法</summary>
        private float[]? TryReadFromFeatureRegisters(Action<string> L, CameraFeature feat)
        {
            try
            {
                var featType = feat.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;

                // 枚举 CameraFeature 的所有方法和属性
                var methods = featType.GetMethods(flags)
                    .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")
                                && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
                    .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})")
                    .Distinct()
                    .ToArray();
                L($"[内参] CameraFeature 方法({methods.Length}): [{string.Join("; ", methods)}]");

                var props = featType.GetProperties(flags);
                L($"[内参] CameraFeature 属性({props.Length}): [{string.Join(", ", props.Select(p => $"{p.PropertyType.Name} {p.Name}"))}]");

                // 候选方法: 可能用于读取寄存器/缓冲数据
                string[] regMethods = { "Read", "ReadRegister", "ReadBuffer", "ReadRaw",
                    "GetBuffer", "GetRawData", "GetRegister", "GetBytes", "ReadNode",
                    "ReadFeatureValue", "GetNodeMap", "ReadMem", "AccessRaw", "StreamRead" };
                foreach (var methodName in regMethods)
                {
                    try
                    {
                        var method = featType.GetMethod(methodName, flags);
                        if (method == null) continue;

                        L($"[内参] 尝试 CameraFeature.{methodName}()");
                        var parameters = method.GetParameters();

                        // 尝试不同签名调用
                        object? result = null;
                        if (parameters.Length == 0)
                        {
                            result = method.Invoke(feat, null);
                        }
                        else if (parameters.Length == 1)
                        {
                            var pType = parameters[0].ParameterType;
                            if (pType == typeof(byte[]))
                                result = method.Invoke(feat, new object[] { new byte[512] });
                            else if (pType == typeof(int))
                                result = method.Invoke(feat, new object[] { 0 });
                            else if (pType == typeof(IntPtr))
                                result = method.Invoke(feat, new object[] { IntPtr.Zero });
                            else
                                L($"[内参]  {methodName} 参数类型为 {pType.Name}，跳过");
                        }
                        else if (parameters.Length == 2)
                        {
                            var buf = new byte[1024];
                            result = method.Invoke(feat, new object[] { buf, buf.Length });
                            L($"[内参]  {methodName}(byte[{buf.Length}], int) 调用完成");
                        }

                        if (result != null)
                            L($"[内参]  {methodName} 返回: {result.GetType().Name} = {result}");

                        // 尝试从结果提取数据
                        if (result is float[] fArr && fArr.Length >= 4)
                        {
                            L($"[内参] CameraFeature.{methodName} → {fArr.Length} floats");
                            return fArr;
                        }
                        if (result is byte[] bArr && bArr.Length >= 16)
                        {
                            var floats = new float[bArr.Length / sizeof(float)];
                            Buffer.BlockCopy(bArr, 0, floats, 0, bArr.Length);
                            L($"[内参] CameraFeature.{methodName}(byte[]) → {floats.Length} floats");
                            return floats;
                        }
                        if (result is IntPtr ptr && ptr != IntPtr.Zero)
                        {
                            L($"[内参] CameraFeature.{methodName} 返回 IntPtr=0x{ptr:X}");
                        }
                    }
                    catch (Exception ex) { L($"[内参] CameraFeature.{methodName} 异常: {ex.Message}"); }
                }

                L("[内参] CameraFeature 反射未找到寄存器读取方法");
            }
            catch (Exception ex) { L($"[内参] CameraFeature 反射异常: {ex.Message}"); }
            return null;
        }

        /// <summary>路径E: 通过深度帧元数据 .Info 读取内参（重点探索 CalibInfo 子对象）</summary>
        private float[]? TryReadIntrinsicFromFrameMetadata(Action<string> L)
        {
            L("[内参] 尝试路径E: 帧元数据 .Info 反射探索");
            try
            {
                if (_lastFrameInfo == null)
                {
                    L("[内参] 无缓存帧元数据，需先抓取一帧");
                    return null;
                }

                var infoType = _lastFrameInfo.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;

                L($"[内参] frame.Info 类型: {infoType.FullName}");

                // 枚举所有字段（CalibInfo 在这里）
                var fields = infoType.GetFields(flags);
                L($"[内参] Info字段({fields.Length}): [{string.Join(", ", fields.Select(f => $"{f.FieldType.Name} {f.Name}"))}]");

                // ★ 核心：深入探索 CalibInfo 子对象
                foreach (var field in fields)
                {
                    try
                    {
                        if (!field.Name.ToLower().Contains("calib") && !field.Name.ToLower().Contains("intrinsic"))
                            continue;

                        var fieldVal = field.GetValue(_lastFrameInfo);
                        if (fieldVal == null) continue;

                        L($"[内参] ✓ 发现 Info.{field.Name} ({fieldVal.GetType().FullName})");

                        // 递归探索 CalibInfo 内部结构
                        var calibType = fieldVal.GetType();
                        var calibFields = calibType.GetFields(flags);
                        L($"[内参] CalibInfo 字段({calibFields.Length}): [{string.Join(", ", calibFields.Select(f => $"{f.FieldType.Name} {f.Name}"))}]");

                        var calibProps = calibType.GetProperties(flags);
                        L($"[内参] CalibInfo 属性({calibProps.Length}): [{string.Join(", ", calibProps.Select(p => $"{p.PropertyType.Name} {p.Name}"))}]");

                        var calibMethods = calibType.GetMethods(flags)
                            .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")
                                        && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
                            .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                            .Distinct()
                            .ToArray();
                        L($"[内参] CalibInfo 方法({calibMethods.Length}): [{string.Join("; ", calibMethods)}]");

                        // 尝试从 CalibInfo 中提取内参数据
                        foreach (var cf in calibFields)
                        {
                            try
                            {
                                var cv = cf.GetValue(fieldVal);
                                if (cv == null) continue;
                                L($"[内参] CalibInfo.{cf.Name} = {cv} ({cv.GetType().Name})");

                                if (cv is float[] fArr && fArr.Length >= 4)
                                {
                                    L($"[内参] 从 CalibInfo.{cf.Name} (float[]) → {fArr.Length} floats");
                                    return fArr;
                                }
                                if (cv is double[] dArr && dArr.Length >= 4)
                                {
                                    var floats = dArr.Select(d => (float)d).ToArray();
                                    L($"[内参] 从 CalibInfo.{cf.Name} (double[]) → {floats.Length} floats");
                                    return floats;
                                }

                                // 如果是嵌套结构体（包括 struct），继续探索
                                // 注意：CameraIntrinsic 是 struct，所以不能用 !IsValueType 过滤
                                if (cv.GetType() != typeof(string) && !cv.GetType().IsPrimitive)
                                {
                                    // 探索子字段（float[] 或 double[]）
                                    var subFields = cv.GetType().GetFields(flags);
                                    foreach (var sf in subFields)
                                    {
                                        try
                                        {
                                            var sv = sf.GetValue(cv);
                                            if (sv != null)
                                                L($"[内参] CalibInfo.{cf.Name}.{sf.Name} = {sv} ({sv.GetType().Name})");

                                            if (sv is float[] sfArr && sfArr.Length >= 4)
                                            {
                                                L($"[内参] 从 CalibInfo.{cf.Name}.{sf.Name} → {sfArr.Length} floats");
                                                return sfArr;
                                            }
                                        }
                                        catch { }
                                    }

                                    // ★ 探索子属性（CameraIntrinsic 常用属性：Fx, Fy, Cx, Cy, K1, K2 等）
                                    var subProps = cv.GetType().GetProperties(flags);
                                    var floatProps = new List<float>();
                                    double fx = 0, fy = 0, cx = 0, cy = 0;
                                    foreach (var sp in subProps)
                                    {
                                        try
                                        {
                                            var sv = sp.GetValue(cv);
                                            if (sv != null)
                                            {
                                                L($"[内参] CalibInfo.{cf.Name}.prop {sp.Name} = {sv} ({sv.GetType().Name})");
                                                if (sv is float fv)
                                                {
                                                    floatProps.Add(fv);
                                                    if (string.Equals(sp.Name, "Fx", StringComparison.OrdinalIgnoreCase)) fx = fv;
                                                    if (string.Equals(sp.Name, "Fy", StringComparison.OrdinalIgnoreCase)) fy = fv;
                                                    if (string.Equals(sp.Name, "Cx", StringComparison.OrdinalIgnoreCase)) cx = fv;
                                                    if (string.Equals(sp.Name, "Cy", StringComparison.OrdinalIgnoreCase)) cy = fv;
                                                }
                                                else if (sv is double dv)
                                                {
                                                    floatProps.Add((float)dv);
                                                    if (string.Equals(sp.Name, "Fx", StringComparison.OrdinalIgnoreCase)) fx = dv;
                                                    if (string.Equals(sp.Name, "Fy", StringComparison.OrdinalIgnoreCase)) fy = dv;
                                                    if (string.Equals(sp.Name, "Cx", StringComparison.OrdinalIgnoreCase)) cx = dv;
                                                    if (string.Equals(sp.Name, "Cy", StringComparison.OrdinalIgnoreCase)) cy = dv;
                                                }
                                                else if (sv is float[] sfArr2 && sfArr2.Length >= 4)
                                                {
                                                    L($"[内参] 从 CalibInfo.{cf.Name}.{sp.Name} (float[]) → {sfArr2.Length} floats");
                                                    return sfArr2;
                                                }
                                                else if (sv is double[] sdArr && sdArr.Length >= 4)
                                                {
                                                    var floats = sdArr.Select(d => (float)d).ToArray();
                                                    L($"[内参] 从 CalibInfo.{cf.Name}.{sp.Name} (double[]) → {floats.Length} floats");
                                                    return floats;
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    // 成功提取到 fx/fy/cx/cy（通过属性名匹配）
                                    if (fx > 0 && fy > 0)
                                    {
                                        var result = new float[] { (float)fx, (float)fy, (float)cx, (float)cy };
                                        L($"[内参] 从 CalibInfo.{cf.Name} 属性提取成功: fx={fx:F2} fy={fy:F2} cx={cx:F2} cy={cy:F2}");
                                        return result;
                                    }

                                    // 回退：收集到的任意 float 属性值
                                    if (floatProps.Count >= 4)
                                    {
                                        L($"[内参] 从 CalibInfo.{cf.Name} 属性收集 → {floatProps.Count} floats");
                                        return floatProps.ToArray();
                                    }
                                }
                            }
                            catch (Exception ex) { L($"[内参] CalibInfo.{cf.Name} 异常: {ex.Message}"); }
                        }

                        // 也尝试属性
                        foreach (var cp in calibProps)
                        {
                            try
                            {
                                var cv = cp.GetValue(fieldVal);
                                if (cv == null) continue;
                                L($"[内参] CalibInfo.{cp.Name} = {cv} ({cv.GetType().Name})");

                                if (cv is float[] fArr2 && fArr2.Length >= 4)
                                {
                                    L($"[内参] 从 CalibInfo.{cp.Name} → {fArr2.Length} floats");
                                    return fArr2;
                                }
                            }
                            catch (Exception ex) { L($"[内参] CalibInfo.{cp.Name} 异常: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { L($"[内参] Info.{field.Name} 反射异常: {ex.Message}"); }
                }

                // 回退：也检查其他字段（如 Timestamp, ScaleUnit 等可能隐藏内参的字段）
                foreach (var field in fields)
                {
                    try
                    {
                        if (field.Name.ToLower().Contains("calib") || field.Name.ToLower().Contains("intrinsic"))
                            continue; // 已在上方处理

                        var val = field.GetValue(_lastFrameInfo);
                        if (val == null) continue;

                        // 只关注可能包含内参数据的类型
                        if (val is float[] fDirect && fDirect.Length >= 4)
                        {
                            L($"[内参] 从 Info.{field.Name} (float[]) → {fDirect.Length} floats");
                            return fDirect;
                        }
                        if (val is double[] dDirect && dDirect.Length >= 4)
                        {
                            L($"[内参] 从 Info.{field.Name} (double[]) → {dDirect.Length} floats");
                            return dDirect.Select(d => (float)d).ToArray();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { L($"[内参] 帧元数据探索异常: {ex.Message}"); }
            return null;
        }

        /// <summary>路径F: 读取 IntrinsicWidth/IntrinsicHeight/DepthScaleUnit 等标量特征</summary>
        private (double fx, double fy, double cx, double cy)? TryReadScalarIntrinsicFeatures(Action<string> L)
        {
            L("[内参] 尝试路径F: 读取标量特征辅助重建内参");

            int width = 0, height = 0;
            double scaleUnit = 1.0;

            try
            {
                // 读取 IntrinsicWidth
                if (_device!.GetFeature("IntrinsicWidth", out CameraFeature fw) == CameraApiStatus.Success)
                {
                    if (fw.GetValue(out CameraValue vw) == CameraApiStatus.Success)
                    {
                        width = Convert.ToInt32(vw.SingleValue);
                        L($"[内参] IntrinsicWidth = {width}");
                    }
                }
            }
            catch (Exception ex) { L($"[内参] IntrinsicWidth 异常: {ex.Message}"); }

            try
            {
                // 读取 IntrinsicHeight
                if (_device!.GetFeature("IntrinsicHeight", out CameraFeature fh) == CameraApiStatus.Success)
                {
                    if (fh.GetValue(out CameraValue vh) == CameraApiStatus.Success)
                    {
                        height = Convert.ToInt32(vh.SingleValue);
                        L($"[内参] IntrinsicHeight = {height}");
                    }
                }
            }
            catch (Exception ex) { L($"[内参] IntrinsicHeight 异常: {ex.Message}"); }

            try
            {
                // 读取 DepthScaleUnit (用于确认深度单位)
                if (_device!.GetFeature("DepthScaleUnit", out CameraFeature fsu) == CameraApiStatus.Success)
                {
                    if (fsu.GetValue(out CameraValue vsu) == CameraApiStatus.Success)
                    {
                        scaleUnit = Convert.ToDouble(vsu.SingleValue);
                        L($"[内参] DepthScaleUnit = {scaleUnit}");
                    }
                }
            }
            catch (Exception ex) { L($"[内参] DepthScaleUnit 异常: {ex.Message}"); }

            try
            {
                // 读取 SensorWidth / SensorHeight
                if (_device!.GetFeature("SensorWidth", out CameraFeature fsw) == CameraApiStatus.Success)
                {
                    if (fsw.GetValue(out CameraValue vsw) == CameraApiStatus.Success)
                        L($"[内参] SensorWidth = {vsw.SingleValue}");
                }
                if (_device!.GetFeature("SensorHeight", out CameraFeature fsh) == CameraApiStatus.Success)
                {
                    if (fsh.GetValue(out CameraValue vsh) == CameraApiStatus.Success)
                        L($"[内参] SensorHeight = {vsh.SingleValue}");
                }
            }
            catch (Exception ex) { L($"[内参] SensorWidth/Height 异常: {ex.Message}"); }

            // 还尝试读取 SourceIDValue 作为组件信息
            try
            {
                if (_device!.GetFeature("SourceIDValue", out CameraFeature fsid) == CameraApiStatus.Success)
                {
                    if (fsid.GetValue(out CameraValue vsid) == CameraApiStatus.Success)
                        L($"[内参] SourceIDValue = {vsid.SingleValue}");
                }
            }
            catch { }

            if (width > 0 && height > 0)
            {
                L($"[内参] 获取到 IntrinsicWidth={width}, IntrinsicHeight={height}, 但无法获取 fx/fy/cx/cy");
                L("[内参] 使用主点默认值 cx=width/2, cy=height/2");
            }
            return null;
        }

        /// <summary>从 CameraValue 提取 float 数组 (标准路径)</summary>
        private float[]? ExtractDataFromCameraValue(Action<string> L, CameraValue camVal)
        {
            try
            {
                // 路径1: ArrayValue
                if (camVal.ArrayValue != null)
                {
                    var floatList = new List<float>();
                    foreach (var item in camVal.ArrayValue)
                    {
                        if (item.SingleValue != null)
                            floatList.Add(Convert.ToSingle(item.SingleValue));
                    }
                    if (floatList.Count > 0)
                    {
                        L($"[内参] ArrayValue 解析 → {floatList.Count} floats");
                        return floatList.ToArray();
                    }
                }

                // 路径2: StructValue
                if (camVal.StructValue != null)
                {
                    L($"[内参] StructValue 键: [{string.Join(", ", camVal.StructValue.Keys)}]");
                    // 尝试按 fx/fy/cx/cy 键名提取
                    var dict = camVal.StructValue;
                    var keys = new[] { "fx", "fy", "cx", "cy", "k1", "k2", "p1", "p2",
                                       "data", "intrinsic", "width", "height" };
                    var floatList = new List<float>();
                    foreach (var k in keys)
                    {
                        if (dict.TryGetValue(k, out var val) && val.SingleValue != null)
                        {
                            floatList.Add(Convert.ToSingle(val.SingleValue));
                            L($"[内参] StructValue[\"{k}\"] = {val.SingleValue}");
                        }
                    }
                    if (floatList.Count >= 4)
                    {
                        L($"[内参] StructValue 键值提取 → {floatList.Count} floats");
                        return floatList.ToArray();
                    }
                }

                // 路径3: SingleValue (可能是单浮点数)
                L($"[内参] ValueType={camVal.ValueType}, SingleValue={camVal.SingleValue ?? "(null)"}");

                // 路径4: 反射提取
                return ExtractDataViaReflection(L, camVal);
            }
            catch (Exception ex)
            {
                L($"[内参] 标准提取异常: {ex.Message}");
                return ExtractDataViaReflection(L, camVal);
            }
        }

        /// <summary>使用反射从 CameraValue 中提取原始 float 数据</summary>
        private float[]? ExtractDataViaReflection(Action<string> L, CameraValue camVal)
        {
            try
            {
                var type = camVal.GetType();
                L($"[内参] 反射探索 CameraValue 类型: {type.FullName}");

                // 1) 枚举所有公共/非公共字段
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;
                var fields = type.GetFields(flags);
                L($"[内参] 字段({fields.Length}): [{string.Join(", ", fields.Select(f => $"{f.FieldType.Name} {f.Name}"))}]");

                // 2) 枚举所有公共/非公共属性
                var props = type.GetProperties(flags);
                L($"[内参] 属性({props.Length}): [{string.Join(", ", props.Select(p => $"{p.PropertyType.Name} {p.Name}"))}]");

                // 3) 枚举所有公共/非公共方法
                var methods = type.GetMethods(flags)
                    .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")
                                && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
                    .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                    .Distinct()
                    .ToArray();
                L($"[内参] 方法({methods.Length}): [{string.Join("; ", methods)}]");

                // 4) 尝试查找内部原始数据
                // 常见内部字段名: _data, _buffer, _rawBuffer, _byteArray, data, buffer, rawBuffer
                foreach (var field in fields)
                {
                    try
                    {
                        var val = field.GetValue(camVal);
                        if (val == null) continue;
                        L($"[内参] 字段 {field.Name}={val} (类型:{val.GetType().Name})");

                        // 如果是 byte[] 或 IntPtr
                        if (val is byte[] byteArr && byteArr.Length >= 16)
                        {
                            var floats = new float[byteArr.Length / sizeof(float)];
                            Buffer.BlockCopy(byteArr, 0, floats, 0, byteArr.Length);
                            L($"[内参] 通过 byte[] 字段 '{field.Name}' 提取 → {floats.Length} floats");
                            return floats;
                        }
                        if (val is IntPtr ptr && ptr != IntPtr.Zero)
                        {
                            L($"[内参] 发现 IntPtr 字段 '{field.Name}'=0x{ptr:X}, 但缺少 size 信息");
                        }
                        if (val is float[] fArr && fArr.Length >= 4)
                        {
                            L($"[内参] 通过 float[] 字段 '{field.Name}' 提取 → {fArr.Length} floats");
                            return fArr;
                        }
                        if (val is double[] dArr && dArr.Length >= 4)
                        {
                            var floats = dArr.Select(d => (float)d).ToArray();
                            L($"[内参] 通过 double[] 字段 '{field.Name}' 提取 → {floats.Length} floats");
                            return floats;
                        }
                    }
                    catch (Exception ex) { L($"[内参] 字段 {field.Name} 读取异常: {ex.Message}"); }
                }

                // 5) 尝试调用可能的数据提取方法
                string[] candidateMethods = { "GetBuffer", "GetData", "GetRawData", "GetFloatArray",
                    "GetDoubleArray", "ToArray", "ToByteArray", "CopyTo", "GetByteArray" };
                foreach (var methodName in candidateMethods)
                {
                    try
                    {
                        var method = type.GetMethod(methodName, flags);
                        if (method == null) continue;

                        var result = method.Invoke(camVal, null);
                        if (result == null) continue;
                        L($"[内参] 方法 '{methodName}' 返回: {result.GetType().Name}");

                        if (result is float[] fltArr && fltArr.Length >= 4)
                        {
                            L($"[内参] 通过 '{methodName}' 提取 → {fltArr.Length} floats");
                            return fltArr;
                        }
                        if (result is double[] dblArr && dblArr.Length >= 4)
                        {
                            var floats = dblArr.Select(d => (float)d).ToArray();
                            L($"[内参] 通过 '{methodName}(double[])' 提取 → {floats.Length} floats");
                            return floats;
                        }
                        if (result is byte[] byteArr2 && byteArr2.Length >= 16)
                        {
                            var floats = new float[byteArr2.Length / sizeof(float)];
                            Buffer.BlockCopy(byteArr2, 0, floats, 0, byteArr2.Length);
                            L($"[内参] 通过 '{methodName}' (byte[]) 提取 → {floats.Length} floats");
                            return floats;
                        }
                        if (result is IntPtr ptr2 && ptr2 != IntPtr.Zero)
                        {
                            L($"[内参] 方法 '{methodName}' 返回 IntPtr=0x{ptr2:X}");
                        }
                    }
                    catch (Exception ex) { L($"[内参] 方法 '{methodName}' 调用异常: {ex.Message}"); }
                }

                // 6) 最后的尝试: 通过 Marshal 从任何 IntPtr+size 组合中复制
                // 寻找 size 相关字段
                long knownSize = 0;
                IntPtr knownPtr = IntPtr.Zero;
                foreach (var field in fields)
                {
                    try
                    {
                        var val = field.GetValue(camVal);
                        if (val == null) continue;
                        if (field.Name.ToLower().Contains("size") && val is int s && s > 0)
                            knownSize = s;
                        if ((field.Name.ToLower().Contains("size") || field.Name.ToLower().Contains("length"))
                            && val is long sl && sl > 0)
                            knownSize = sl;
                        if (val is IntPtr p && p != IntPtr.Zero)
                            knownPtr = p;
                    }
                    catch { }
                }

                // 同时检查属性中的 size 信息
                foreach (var prop in props)
                {
                    try
                    {
                        if (prop.Name.ToLower().Contains("size") || prop.Name.ToLower().Contains("length"))
                        {
                            var val = prop.GetValue(camVal);
                            if (val is int si && si > 0) knownSize = si;
                            if (val is long sl && sl > 0) knownSize = sl;
                        }
                    }
                    catch { }
                }

                if (knownSize > 0 && knownPtr != IntPtr.Zero && knownSize >= 16)
                {
                    int floatCount = (int)(knownSize / sizeof(float));
                    var floats = new float[floatCount];
                    Marshal.Copy(knownPtr, floats, 0, floatCount);
                    L($"[内参] Marshal.Copy(ptr+size) → {floatCount} floats");
                    return floats;
                }

                L("[内参] 反射探索未找到可提取的原始数据");
            }
            catch (Exception ex)
            {
                L($"[内参] 反射提取异常: {ex.Message}");
                L($"[内参] 堆栈: {ex.StackTrace}");
            }
            return null;
        }

        /// <summary>从 float 数组中解析内参布局</summary>
        private static (double fx, double fy, double cx, double cy)? ParseIntrinsicLayout(
            Action<string> L, float[] data)
        {
            double fx, fy, cx, cy;

            // 尝试多种常见布局

            // Layout B: [fx, fy, cx, cy, ...]   fx/fy > 1 且 cx/cy 合理
            if (data.Length >= 4 && data[0] > 1 && data[1] > 1
                && Math.Abs(data[2]) >= 0 && Math.Abs(data[3]) >= 0)
            {
                fx = data[0]; fy = data[1]; cx = data[2]; cy = data[3];
                L($"[内参] Layout-B: fx={fx:F2} fy={fy:F2} cx={cx:F2} cy={cy:F2}");
                if (fx > 0 && fy > 0) return (fx, fy, cx, cy);
            }

            // Layout A: [?, ?, fx, fy, cx, cy, ...]  第2-4个元素是内参
            if (data.Length >= 6 && data[2] > 1 && data[3] > 1)
            {
                fx = data[2]; fy = data[3]; cx = data[4]; cy = data[5];
                L($"[内参] Layout-A: fx={fx:F2} fy={fy:F2} cx={cx:F2} cy={cy:F2}");
                if (fx > 0 && fy > 0) return (fx, fy, cx, cy);
            }

            // Layout C: 只找前4个 > 0 的值
            {
                var candidates = data.Where(v => v > 0).Take(4).ToArray();
                if (candidates.Length == 4 && candidates[0] > 50 && candidates[1] > 50)
                {
                    fx = candidates[0]; fy = candidates[1]; cx = candidates[2]; cy = candidates[3];
                    L($"[内参] Layout-C (正数提取): fx={fx:F2} fy={fy:F2} cx={cx:F2} cy={cy:F2}");
                    return (fx, fy, cx, cy);
                }
            }

            // Layout D: 跳过可能的前缀校准字段，在整个数组中滑动窗口找合理的内参
            for (int offset = 0; offset <= data.Length - 4; offset++)
            {
                double f0 = data[offset], f1 = data[offset + 1];
                if (f0 > 100 && f0 < 10000 && f1 > 100 && f1 < 10000)
                {
                    fx = f0; fy = f1; cx = data[offset + 2]; cy = data[offset + 3];
                    L($"[内参] Layout-D (offset={offset}): fx={fx:F2} fy={fy:F2} cx={cx:F2} cy={cy:F2}");
                    return (fx, fy, cx, cy);
                }
            }

            L($"[内参] ✗ 无法从 {data.Length} 个值中解析出合理内参");
            return null;
        }

        /// <summary>
        /// 异步抓取图像流程。
        /// 基于 TaskCompletionSource 将底层异步回调机制转换为上层易用的 await 语法，并内建 5 秒熔断机制。
        /// StartGrabbing 冷启动后增加 150ms 预等待，降低首帧超时概率。
        /// </summary>
        /// <returns>返回处理后的深度帧 DepthFrameData；超时返回红色警告 Bitmap。</returns>
        public async Task<object> GrabFrameAsync()
        {
            bool wasColdStart = !IsCapturing;
            if (!IsCapturing)
            {
                bool started = StartGrabbing();
                if (!started)
                {
                    Console.WriteLine($"[错误] 相机 {SerialNumber} 启动取流失败，可能未连接或硬件异常");
                    var errBmp = new Bitmap(640, 480);
                    using (var g = Graphics.FromImage(errBmp))
                    {
                        g.Clear(Color.DarkRed);
                        g.DrawString($"START FAIL\nSN: {SerialNumber}", new Font("Arial", 24, FontStyle.Bold), Brushes.White, 40, 200);
                    }
                    return errBmp;
                }

                // 冷启动后等待 150ms 让 SDK 完成首帧初始化
                await Task.Delay(150);
            }

            _frameTcs = new TaskCompletionSource<object>();

            // 构建 5 秒超时熔断任务。
            var timeoutTask = Task.Delay(5000);
            
            // 并发竞争：看回调先到还是时间先到。
            var completedTask = await Task.WhenAny(_frameTcs.Task, timeoutTask);

            if (completedTask == _frameTcs.Task)
            {
                return await _frameTcs.Task;
            }
            else
            {
                string hint = wasColdStart ? "（冷启动）" : "";
                Console.WriteLine($"[警告] 从图漾 3D 相机 {SerialNumber} 抓图超时{hint}。返回占位图以保障流程。");
                _frameTcs.TrySetCanceled();
                
                // 生成红色警示背景的错误提示图。
                var failBmp = new Bitmap(640, 480);
                using (var g = Graphics.FromImage(failBmp))
                {
                    g.Clear(Color.DarkRed);
                    g.DrawString($"TIMEOUT\nSN: {SerialNumber}\n{hint}", new Font("Arial", 24, FontStyle.Bold), Brushes.White, 80, 150);
                }
                return failBmp;
            }
        }
    }
}

