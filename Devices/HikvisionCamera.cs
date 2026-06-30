using System;
using System.Drawing;
using System.Threading.Tasks;
using MvCamCtrl.NET;
using System.Runtime.InteropServices;

namespace pallet_storage_detection_system_Net_V2.Devices
{
    /// <summary>
    /// 海康威视相机驱动实现类。
    /// 基于海康 MvCamCtrl.NET SDK 构建，支持 GigE 与 USB 相机映射与取拍。
    /// </summary>
    public class HikvisionCamera : ICameraDevice
    {
        private MyCamera _device;
        private int _exposureIndex = 0;

        /// <summary>
        /// 目标设备的唯一序列号。
        /// </summary>
        public string SerialNumber { get; private set; }

        /// <summary>
        /// 设备是否已经由于 OpenDevice 成功进入就绪状态。
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// 设备是否当前正处于 StartGrabbing 指令开启的采集流中。
        /// </summary>
        public bool IsCapturing { get; private set; } = false;

        /// <summary>
        /// 实例化海康相机对象（尚未连接）。
        /// </summary>
        /// <param name="sn">相机真实 SN 序列号。</param>
        public HikvisionCamera(string sn)
        {
            SerialNumber = sn;
            _device = new MyCamera();
        }

        /// <summary>
        /// 扫描网络/总线，检索匹配序列号的相机并执行设备打开与初始化。
        /// </summary>
        /// <returns>连接并开启配置是否成功。</returns>
        public bool Connect()
        {
            if (IsConnected) return true;

            MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            // 枚举支持的物理传输后端
            int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
            if (nRet != MyCamera.MV_OK) throw new Exception($"海康 SDK 枚举设备失败: {nRet:X}");

            MyCamera.MV_CC_DEVICE_INFO? targetDev = null;
            for (int i = 0; i < stDevList.nDeviceNum; i++)
            {
                MyCamera.MV_CC_DEVICE_INFO dev = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(stDevList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                string sn = "";
                
                if (dev.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(dev.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    sn = gigeInfo.chSerialNumber.Replace("\0", "").Trim();
                }
                else if (dev.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(dev.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    sn = usbInfo.chSerialNumber.Replace("\0", "").Trim();
                }

                if (sn.Equals(SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    targetDev = dev;
                    break;
                }
            }

            if (targetDev == null) 
            {
                throw new Exception($"未找到 SN 为 {SerialNumber} 的海康相机。请检查线缆连接与 IP 设置。");
            }

            MyCamera.MV_CC_DEVICE_INFO actualDev = targetDev.Value;
            nRet = _device.MV_CC_CreateDevice_NET(ref actualDev);
            if (nRet != MyCamera.MV_OK) throw new Exception($"创建海康设备句柄失败: {nRet:X}");

            nRet = _device.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK) throw new Exception($"打开海康相机失败: {nRet:X}");

            // 1. 优化网络包大小 (仅限 GigE 相机)，防止因 Jumbo Frame 未开启导致 80000007 无数据报错
            if (actualDev.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                int nPacketSize = _device.MV_CC_GetOptimalPacketSize_NET();
                if (nPacketSize > 0)
                {
                    _device.MV_CC_SetIntValue_NET("GevSCPSPacketSize", (uint)nPacketSize);
                }
            }

            // 2. 强制使用软触发模式 (TriggerMode=1, TriggerSource=7)
            // 这样既不会因为连续采图把网络千兆带宽塞满(导致丢包和 80000007)，
            // 又能确保软件调用时随时能拍到当前最新画面。
            _device.MV_CC_SetEnumValue_NET("TriggerMode", 1);
            _device.MV_CC_SetEnumValueByString_NET("TriggerSource", "Software");

            IsConnected = true;
            return true;
        }

        /// <summary>
        /// 关闭底层流采集并销毁设备句柄。
        /// </summary>
        public void Disconnect()
        {
            if (IsConnected)
            {
                StopGrabbing();
                _device.MV_CC_CloseDevice_NET();
                _device.MV_CC_DestroyDevice_NET();
                IsConnected = false;
                _exposureIndex = 0;
            }
        }

        /// <summary>
        /// 发送指令进入采集状态。
        /// </summary>
        public bool StartGrabbing()
        {
            if (!IsConnected) return false;
            if (IsCapturing) return true;

            _exposureIndex = 0; // 重置曝光轮询序号
            int nRet = _device.MV_CC_StartGrabbing_NET();
            if (nRet == MyCamera.MV_OK)
            {
                IsCapturing = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 发送指令停止采集。
        /// </summary>
        public void StopGrabbing()
        {
            if (!IsCapturing) return;
            _device.MV_CC_StopGrabbing_NET();
            IsCapturing = false;
            _exposureIndex = 0; // 重置曝光轮询序号
        }

        /// <summary>
        /// 核心方法：异步抓取。
        /// 基于 GetImageBuffer 获取压缩/原始格式，转化为 BGR 排布以适配 WinForms 控件显示。
        /// </summary>
        /// <returns>返回 System.Drawing.Bitmap 位图对象。</returns>
        public async Task<object> GrabFrameAsync()
        {
            if (!IsConnected) throw new Exception("相机未连接");
            if (!IsCapturing) 
            {
                if (!StartGrabbing()) throw new Exception($"海康相机 {SerialNumber} 开启采集流失败");
            }

            return await Task.Run(() =>
            {
                // 自动曝光轮询逻辑 (方案 3)
                var viCfg = Config.ConfigManager.Instance?.Algorithms?.VisualInventory;
                if (viCfg != null && viCfg.EnableExposureCycle && viCfg.ExposureTimeSequence != null && viCfg.ExposureTimeSequence.Count > 0)
                {
                    // 1. 关闭自动曝光模式 (0: Off)，允许手动控制参数
                    _device.MV_CC_SetEnumValue_NET("ExposureAuto", 0);

                    // 2. 取出并写入下一个预设曝光值 (us)
                    float expTimeUs = viCfg.ExposureTimeSequence[_exposureIndex % viCfg.ExposureTimeSequence.Count];
                    _exposureIndex = (_exposureIndex + 1) % viCfg.ExposureTimeSequence.Count;

                    int nRetExp = _device.MV_CC_SetFloatValue_NET("ExposureTime", expTimeUs);
                    if (nRetExp != MyCamera.MV_OK)
                    {
                        Console.WriteLine($"警告: 相机 {SerialNumber} 设置曝光时间 {expTimeUs} us 失败: {nRetExp:X}");
                    }
                }

                // 发送软件触发指令，要求相机立刻拍一张
                int nRetTrigger = _device.MV_CC_SetCommandValue_NET("TriggerSoftware");
                if (nRetTrigger != MyCamera.MV_OK)
                {
                    Console.WriteLine($"警告: 相机 {SerialNumber} 软触发命令发送失败: {nRetTrigger:X}");
                }

                MyCamera.MV_FRAME_OUT stFrameOut = new MyCamera.MV_FRAME_OUT();
                int nRet = _device.MV_CC_GetImageBuffer_NET(ref stFrameOut, 1500); // 1.5s 物理抓拍超时
                if (nRet != MyCamera.MV_OK) throw new Exception($"海康相机 {SerialNumber} 抓图超时或报错: {nRet:X}");

                try
                {
                    // 像素格式转换参数设置：转换为 BGR8 24bit，适配 24bppRgb 格式的 Bitmap
                    MyCamera.MV_PIXEL_CONVERT_PARAM stConvertParam = new MyCamera.MV_PIXEL_CONVERT_PARAM();
                    stConvertParam.nWidth = stFrameOut.stFrameInfo.nWidth;
                    stConvertParam.nHeight = stFrameOut.stFrameInfo.nHeight;
                    stConvertParam.enSrcPixelType = stFrameOut.stFrameInfo.enPixelType;
                    stConvertParam.pSrcData = stFrameOut.pBufAddr;
                    stConvertParam.nSrcDataLen = stFrameOut.stFrameInfo.nFrameLen;
                    stConvertParam.enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed;
                    
                    int nSize = stFrameOut.stFrameInfo.nWidth * stFrameOut.stFrameInfo.nHeight * 3;
                    IntPtr pDstBuf = Marshal.AllocHGlobal(nSize);
                    stConvertParam.pDstBuffer = pDstBuf;
                    stConvertParam.nDstLen = (uint)nSize;
                    stConvertParam.nDstBufferSize = (uint)nSize;

                    nRet = _device.MV_CC_ConvertPixelType_NET(ref stConvertParam);
                    if (nRet != MyCamera.MV_OK) 
                    {
                        Marshal.FreeHGlobal(pDstBuf);
                        throw new Exception($"海康像素转换失败: {nRet:X}");
                    }

                    Bitmap bmp = new Bitmap(stFrameOut.stFrameInfo.nWidth, stFrameOut.stFrameInfo.nHeight, 
                                          System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    
                    var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), 
                                             System.Drawing.Imaging.ImageLockMode.WriteOnly, 
                                             bmp.PixelFormat);
                    
                    // 将外部 Native 缓冲区的 BGR 数据拷贝入 Bitmap 托管区域
                    byte[] rawRgb = new byte[nSize];
                    Marshal.Copy(pDstBuf, rawRgb, 0, nSize);
                    Marshal.Copy(rawRgb, 0, bmpData.Scan0, nSize);
                    
                    bmp.UnlockBits(bmpData);
                    Marshal.FreeHGlobal(pDstBuf);
                    
                    return (object)bmp;
                }
                finally
                {
                    _device.MV_CC_FreeImageBuffer_NET(ref stFrameOut);
                }
            });
        }
    }
}

