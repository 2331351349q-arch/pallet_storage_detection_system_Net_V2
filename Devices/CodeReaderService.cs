using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using material_box_storage_detection_system_Net.Config;
using MvCodeReaderSDKNet;

namespace material_box_storage_detection_system_Net.Devices
{
    public static class CodeReaderService
    {
        private static MvCodeReader? _device;
        private static Thread? _grabThread;
        private static volatile bool _grabbing;
        private static HashSet<string> _barcodes = new HashSet<string>();

        /// <summary>
        /// 读码器是否正在采集运行中。
        /// </summary>
        public static bool IsRunning => _grabbing;
        private static Action<Bitmap>? _imageCallback;
        private static readonly object _lock = new object();
        private static readonly object _imageLock = new object();
        private static Bitmap? _latestFrame;
        private static MvCodeReader.cbExceptiondelegate? _cbExceptiondelegate;

        public static int TestConnection(Action<string> onLog)
        {
            try
            {
                MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST stDeviceList = new MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
                int nRet = MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref stDeviceList, MvCodeReader.MV_CODEREADER_GIGE_DEVICE | MvCodeReader.MV_CODEREADER_USB_DEVICE);
                if (nRet == MvCodeReader.MV_CODEREADER_OK && stDeviceList.nDeviceNum > 0)
                {
                    var target = ConfigManager.Instance?.CodeReader;
                    var matched = FindTargetDevice(stDeviceList, target?.SerialNumber, target?.IpAddress, out _, out _);
                    if (matched)
                    {
                        onLog?.Invoke($"✅ [连接成功] 目标读码器已找到 (SN={target?.SerialNumber}, IP={target?.IpAddress})");
                    }
                    else
                    {
                        onLog?.Invoke($"⚠ [发现设备] 已枚举到 {stDeviceList.nDeviceNum} 台读码器，但未匹配到目标设备 (SN={target?.SerialNumber}, IP={target?.IpAddress})");
                    }
                    return (int)stDeviceList.nDeviceNum;
                }
                else
                {
                    onLog?.Invoke("❌ [连接失败] 未发现网内枚举的海康读码器设备。");
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"❌ [异常] 读码器状态自检失败: {ex.Message}");
            }
            return 0;
        }

        public static bool StartScan(Action<Bitmap> imageCallback, Action<string>? onLog = null)
        {
            try
            {
                var scanStartTime = DateTime.Now;
                StopScan();

                _imageCallback = imageCallback;
                lock (_lock)
                {
                    _barcodes.Clear();
                }
                lock (_imageLock)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = null;
                }

                var target = ConfigManager.Instance?.CodeReader;
                string targetSn = target?.SerialNumber ?? string.Empty;
                string targetIp = target?.IpAddress ?? string.Empty;

                onLog?.Invoke($"[-] 正在启动读码器实时预览，请稍候... 目标SN={targetSn}, 目标IP={targetIp}");

                int nRet;

                // 如果句柄尚未建立，才进行完整的枚举和打开设备流程
                if (_device == null)
                {
                    // 1. 枚举设备
                    var enumStartTime = DateTime.Now;
                    MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST stDeviceList = new MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
                    nRet = MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref stDeviceList, MvCodeReader.MV_CODEREADER_GIGE_DEVICE | MvCodeReader.MV_CODEREADER_USB_DEVICE);
                    var enumTime = (DateTime.Now - enumStartTime).TotalMilliseconds;
                    
                    if (nRet != MvCodeReader.MV_CODEREADER_OK || stDeviceList.nDeviceNum == 0)
                    {
                        onLog?.Invoke("❌ [启动失败] 未找到可用读码器设备，请检查连接或驱动状态。");
                        Console.WriteLine("CodeReader: 获取设备列表失败或无设备!");
                        return false;
                    }

                    onLog?.Invoke($"⏱️  [读码器枚举设备] 耗时: {enumTime:F2}ms");

                    if (!FindTargetDevice(stDeviceList, targetSn, targetIp, out var stDeviceInfo, out string matchedInfo))
                    {
                        onLog?.Invoke($"❌ [启动失败] 未找到目标读码器，已枚举 {stDeviceList.nDeviceNum} 台设备，目标SN={targetSn}, 目标IP={targetIp}");
                        Console.WriteLine($"CodeReader: 未找到目标设备。SN={targetSn}, IP={targetIp}");
                        return false;
                    }

                    onLog?.Invoke($"✅ [读码器选择] 已匹配设备: {matchedInfo}");

                    // 创建句柄并打开设备
                    var deviceInitStartTime = DateTime.Now;
                    _device = new MvCodeReader();
                    nRet = _device.MV_CODEREADER_CreateHandle_NET(ref stDeviceInfo);
                    if (nRet != MvCodeReader.MV_CODEREADER_OK)
                    {
                        onLog?.Invoke($"❌ [启动失败] 创建读码器句柄失败: 0x{nRet:X8}");
                        Console.WriteLine($"CodeReader: 创建句柄失败! {nRet:X8}");
                        _device = null;
                        return false;
                    }

                    // 3. 打开设备
                    nRet = _device.MV_CODEREADER_OpenDevice_NET();
                    if (nRet != MvCodeReader.MV_CODEREADER_OK)
                    {
                        onLog?.Invoke($"❌ [启动失败] 打开读码器设备失败: 0x{nRet:X8}");
                        Console.WriteLine($"CodeReader: 打开设备失败! {nRet:X8}");
                        CloseDevice();
                        return false;
                    }

                    var deviceInitTime = (DateTime.Now - deviceInitStartTime).TotalMilliseconds;
                    onLog?.Invoke($"⏱️  [读码器初始化(创建句柄→打开)] 耗时: {deviceInitTime:F2}ms");

                    // 设置异常回调
                    _cbExceptiondelegate = new MvCodeReader.cbExceptiondelegate(ExceptionCallback);
                    _device.MV_CODEREADER_RegisterExceptionCallBack_NET(_cbExceptiondelegate, IntPtr.Zero);

                    // 连续触发模式
                    _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode", (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);
                }
                else
                {
                    onLog?.Invoke($"✅ [重用连接] 读码器连接已保持，跳过枚举和打开设备的耗时过程。");
                }

                // 4. 开始取流
                var grabStartTime = DateTime.Now;
                nRet = _device.MV_CODEREADER_StartGrabbing_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK)
                {
                    onLog?.Invoke($"❌ [启动失败] 读码器开始取流失败: 0x{nRet:X8}，连接可能已断开，清理句柄尝试下次重连。");
                    Console.WriteLine($"CodeReader: 开始取流失败! {nRet:X8}");
                    CloseDevice();
                    return false;
                }

                var grabTime = (DateTime.Now - grabStartTime).TotalMilliseconds;
                onLog?.Invoke($"⏱️  [读码器启动取流] 耗时: {grabTime:F2}ms");

                // 5. 启动取流线程
                _grabbing = true;
                _grabThread = new Thread(ReceiveThreadProcess);
                _grabThread.IsBackground = true;
                _grabThread.Start();

                var totalTime = (DateTime.Now - scanStartTime).TotalMilliseconds;
                onLog?.Invoke("✅ 读码器已启动，开始连续扫码...");
                onLog?.Invoke($"⏱️  [读码器启动总耗时] {totalTime:F2}ms");
                Console.WriteLine("读码器已启动，开始连续扫码...");
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"❌ [异常] 读码器启动失败: {ex.Message}");
                Console.WriteLine("CodeReader StartScan Exception: " + ex);
                CloseDevice();
                _grabbing = false;
                return false;
            }
        }

        private static bool FindTargetDevice(MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST stDeviceList, string targetSn, string targetIp, out MvCodeReader.MV_CODEREADER_DEVICE_INFO deviceInfo, out string matchedInfo)
        {
            deviceInfo = default;
            matchedInfo = string.Empty;

            for (int i = 0; i < stDeviceList.nDeviceNum; i++)
            {
                var info = (MvCodeReader.MV_CODEREADER_DEVICE_INFO)Marshal.PtrToStructure(
                    stDeviceList.pDeviceInfo[i], typeof(MvCodeReader.MV_CODEREADER_DEVICE_INFO))!;

                if (TryMatchDevice(info, targetSn, targetIp, out matchedInfo))
                {
                    deviceInfo = info;
                    return true;
                }
            }

            return false;
        }

        private static bool TryMatchDevice(MvCodeReader.MV_CODEREADER_DEVICE_INFO info, string targetSn, string targetIp, out string matchedInfo)
        {
            matchedInfo = string.Empty;

            if (info.nTLayerType == MvCodeReader.MV_CODEREADER_GIGE_DEVICE)
            {
                var gigeInfo = GetGigeInfo(info);
                string deviceSn = gigeInfo.chSerialNumber?.Trim() ?? string.Empty;
                string deviceIp = FormatIp(gigeInfo.nCurrentIp);
                matchedInfo = $"SN={deviceSn}, IP={deviceIp}";

                if (!string.IsNullOrWhiteSpace(targetSn) && string.Equals(deviceSn, targetSn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(targetIp) && string.Equals(deviceIp, targetIp.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (info.nTLayerType == MvCodeReader.MV_CODEREADER_USB_DEVICE)
            {
                var usbInfo = GetUsbInfo(info);
                string deviceSn = usbInfo.chSerialNumber?.Trim() ?? string.Empty;
                matchedInfo = $"SN={deviceSn}, USB";

                if (!string.IsNullOrWhiteSpace(targetSn) && string.Equals(deviceSn, targetSn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO GetGigeInfo(MvCodeReader.MV_CODEREADER_DEVICE_INFO info)
        {
            var handle = GCHandle.Alloc(info.SpecialInfo.stGigEInfo, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static MvCodeReader.MV_CODEREADER_USB3_DEVICE_INFO GetUsbInfo(MvCodeReader.MV_CODEREADER_DEVICE_INFO info)
        {
            var handle = GCHandle.Alloc(info.SpecialInfo.stUsb3VInfo, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<MvCodeReader.MV_CODEREADER_USB3_DEVICE_INFO>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static string FormatIp(uint ip)
        {
            var bytes = BitConverter.GetBytes(ip);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return new IPAddress(bytes).ToString();
        }

        public static HashSet<string> StopScan()
        {
            _grabbing = false;

            // 先停止取流，使 GetOneFrameTimeoutEx2_NET 及时返回，线程才能退出
            if (_device != null)
            {
                try { _device.MV_CODEREADER_StopGrabbing_NET(); } catch { }
            }

            if (_grabThread != null)
            {
                _grabThread.Join(2000);
                _grabThread = null;
            }

            // 【优化点】不再每次扫描结束时断开网络连接 (CloseDevice)，
            // 而是保持句柄打开状态。这样下次开启扫描时，即可跳过漫长的网络枚举与初始化，耗时降至几毫秒。
            // CloseDevice();

            lock (_lock)
            {
                Console.WriteLine($"CodeReader: 扫描已停止，共获取去重条码数量: {_barcodes.Count}");
                return new HashSet<string>(_barcodes);
            }
        }

        public static Bitmap? GetLatestFrameSnapshot()
        {
            lock (_imageLock)
            {
                return _latestFrame == null ? null : (Bitmap)_latestFrame.Clone();
            }
        }

        private static void CloseDevice()
        {
            if (_device != null)
            {
                try { _device.MV_CODEREADER_CloseDevice_NET(); } catch { }
                try { _device.MV_CODEREADER_DestroyHandle_NET(); } catch { }
                _device = null;
            }
        }

        private static void ReceiveThreadProcess()
        {
            IntPtr pData = IntPtr.Zero;
            MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2 frameInfo = new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2();
            IntPtr pFrameInfoEx2 = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)));
            Marshal.StructureToPtr(frameInfo, pFrameInfoEx2, false);

            try
            {
                while (_grabbing)
                {
                    var dev = _device;
                    if (dev == null) break;

                    int nRet = dev.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pFrameInfoEx2, 1000);
                    if (nRet != MvCodeReader.MV_CODEREADER_OK)
                    {
                        continue;
                    }

                    frameInfo = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(
                        pFrameInfoEx2, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2))!;

                    // --- 解析图像并输出到回调 ---
                    if (_imageCallback != null && pData != IntPtr.Zero && frameInfo.nFrameLen > 0)
                    {
                        try
                        {
                            Bitmap? bmp = null;
                            if (frameInfo.enPixelType == MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Mono8)
                            {
                                bmp = new Bitmap((int)frameInfo.nWidth, (int)frameInfo.nHeight, PixelFormat.Format8bppIndexed);
                                ColorPalette cp = bmp.Palette;
                                for (int i = 0; i < 256; i++) { cp.Entries[i] = Color.FromArgb(i, i, i); }
                                bmp.Palette = cp;

                                BitmapData bmpData = bmp.LockBits(
                                    new Rectangle(0, 0, (int)frameInfo.nWidth, (int)frameInfo.nHeight),
                                    ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                                byte[] rawBuf = new byte[frameInfo.nWidth * frameInfo.nHeight];
                                Marshal.Copy(pData, rawBuf, 0, rawBuf.Length);
                                Marshal.Copy(rawBuf, 0, bmpData.Scan0, rawBuf.Length);
                                bmp.UnlockBits(bmpData);
                            }
                            else if (frameInfo.enPixelType == MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Jpeg)
                            {
                                byte[] jpegBuf = new byte[frameInfo.nFrameLen];
                                Marshal.Copy(pData, jpegBuf, 0, (int)frameInfo.nFrameLen);
                                bmp = new Bitmap(new System.IO.MemoryStream(jpegBuf));
                            }

                            if (bmp != null)
                            {
                                lock (_imageLock)
                                {
                                    _latestFrame?.Dispose();
                                    _latestFrame = (Bitmap)bmp.Clone();
                                }
                                _imageCallback.Invoke(bmp);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("CodeReader Image Render Exception: " + ex.Message);
                        }
                    }

                    // --- 解析条码结果并去重 ---
                    if (frameInfo.UnparsedBcrList.pstCodeListEx2 != IntPtr.Zero)
                    {
                        MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2 stBcrResult =
                            (MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2)Marshal.PtrToStructure(
                                frameInfo.UnparsedBcrList.pstCodeListEx2,
                                typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2))!;

                        if (stBcrResult.nCodeNum > 0 && stBcrResult.stBcrInfoEx2 != null)
                        {
                            for (int i = 0; i < stBcrResult.nCodeNum; i++)
                            {
                                var bcr = stBcrResult.stBcrInfoEx2[i];
                                if (bcr.chCode != null)
                                {
                                    string strCode = System.Text.Encoding.UTF8.GetString(bcr.chCode)
                                        .Trim().TrimEnd('\0');
                                    lock (_lock)
                                    {
                                        if (!string.IsNullOrEmpty(strCode) && _barcodes.Add(strCode))
                                        {
                                            Console.WriteLine($"CodeReader 扫到新条码: {strCode}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                } // end while (_grabbing)
            }
            catch (Exception ex)
            {
                Console.WriteLine("CodeReader ReceiveThreadProcess Exception: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(pFrameInfoEx2);
            }
        }

        private static void ExceptionCallback(uint nMsgType, IntPtr pUser)
        {
            if (nMsgType == MvCodeReader.MV_CODEREADER_EXCEPTION_DEV_DISCONNECT)
            {
                Console.WriteLine("CodeReader: 设备断开连接！");
                _grabbing = false;
                CloseDevice();
            }
        }
    }
}
