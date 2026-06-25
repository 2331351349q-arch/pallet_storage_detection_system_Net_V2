using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using material_box_storage_detection_system_Net.Config;

namespace material_box_storage_detection_system_Net.Devices
{
    /// <summary>
    /// 设备管理器，负责全系统相机的生命周期管理、初始化及硬件对象的缓存。
    /// </summary>
    public class DeviceManager
    {
        /// <summary>
        /// 缓存所有全系统共享且保持心跳的相机。
        /// 使用 ConcurrentDictionary 以支持多线程安全访问。
        /// ICameraDevice：相机设备接口，抽象了不同厂商的实现细节。
        /// </summary>
        private static ConcurrentDictionary<string, ICameraDevice> _cameras = new ConcurrentDictionary<string, ICameraDevice>();

        /// <summary>
        /// 记录相机初始化是否已完成。
        /// </summary>
        public static bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// 根据配置列表初始化所有相机硬件块。
        /// </summary>
        /// <param name="configs">相机配置列表。</param>
        /// <param name="onLog">用于输出初始化进度日志的回调函数。</param>
        public static int Initialize(List<CameraConfig> configs, Action<string> onLog)
        {
            if (configs == null || configs.Count == 0)
            {
                onLog?.Invoke("配置文件中没有找到任何相机配置 (configs == null)");
                IsInitialized = true;
                return 0;
            }

            foreach (var cfg in configs)
            {
                try
                {
                    ICameraDevice cam = null;
                    // 根据配置中的 Type 字符串动态实例化对应的驱动类。
                    if (cfg.Type.Contains("Hikvision"))
                    {
                        cam = new HikvisionCamera(cfg.Sn);
                    }
                    else if (cfg.Type.Contains("Tycam") || cfg.Type.Contains("Percipio"))
                    {
                        cam = new PercipioCamera(cfg.Sn);
                    }

                    if (cam != null)
                    {
                        try
                        {
                            bool connected = cam.Connect();
                            if (connected)
                            {
                                _cameras[cfg.Sn] = cam;
                                onLog?.Invoke($"✅ [连接成功] 相机: {cfg.Name}, SN: {cfg.Sn}");
                            }
                            else
                            {
                                onLog?.Invoke($"❌ [连接失败] 设备响应异常 - 相机: {cfg.Name}, SN: {cfg.Sn}");
                            }
                        }
                        catch (Exception ex)
                        {
                            onLog?.Invoke($"❌ [连接异常] {cfg.Name}({cfg.Sn}): {ex.Message}");
                        }
                    }
                }
                catch(Exception ex)
                {
                    onLog?.Invoke($"❌ [相机代码异常] 初始化 SN={cfg.Sn} 发生错误: {ex.Message}");
                }
            }
            IsInitialized = true;
            return _cameras.Count;
        }

        /// <summary>
        /// 根据序列号从硬件池中获取相机对象。
        /// </summary>
        /// <param name="sn">目标序列号。</param>
        /// <returns>返回匹配的 ICameraDevice；如果未找到，则返回 null。</returns>
        public static ICameraDevice GetCamera(string sn)
        {
            if (_cameras.TryGetValue(sn, out var existingCam))
            {
                return existingCam;
            }

            return null;
        }

        /// <summary>
        /// 获取当前已成功连接的所有相机设备实例。
        /// </summary>
        public static IReadOnlyList<ICameraDevice> GetAllCameras()
        {
            return _cameras.Values.ToList().AsReadOnly();
        }
    }
}

