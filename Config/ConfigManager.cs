using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using material_box_storage_detection_system_Net.Models;

namespace material_box_storage_detection_system_Net.Config
{
    /// <summary>
    /// 全局配置管理器，负责基于 JSON 的持久化与内存单例管理。
    /// 默认配置来源于 AppConfig 的硬编码属性，修改后持久化到 config.json。
    /// </summary>
    public class ConfigManager
    {
        private static AppConfig appconfig;
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "config.json");

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// 获取全局唯一的配置实例。如果本地存在 JSON 则加载覆盖，否则使用默认硬编码值。
        /// </summary>
        public static AppConfig Instance
        {
            get
            {
                if (appconfig == null)
                {
                    LoadConfig();
                }
                return appconfig;
            }
        }

        private static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    appconfig = JsonSerializer.Deserialize<AppConfig>(json, GetJsonOptions());
                    Console.WriteLine("已成功从本地 config.json 加载持久化配置。");
                }
                else
                {
                    appconfig = new AppConfig();
                    SaveConfig(); // 生成默认对应的 JSON 文件方便现场改动
                    Console.WriteLine("未找到 config.json，已使用系统默认内置参数并生成文件。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置失败，回退至安全默认值: {ex.Message}");
                appconfig = new AppConfig();
            }
        }

        /// <summary>
        /// 将内存中的配置同步持久化到 JSON 文件。
        /// 用于 UI 修改参数后调用。
        /// 同时回写到源码目录的 Config\config.json，确保重新生成解决方案后数据不丢失。
        /// </summary>
        public static void SaveConfig()
        {
            try
            {
                if (appconfig == null) return;

                string directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory)) 
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(appconfig, GetJsonOptions());

                // 写入运行时输出目录
                File.WriteAllText(ConfigPath, json);
                Console.WriteLine($"配置修改已持久化至: {ConfigPath}");

                // 同时回写到源码目录，防止重新生成时被初始模板覆盖
                string? sourceConfigPath = FindSourceConfigPath();
                if (sourceConfigPath != null)
                {
                    File.WriteAllText(sourceConfigPath, json);
                    Console.WriteLine($"配置已同步回写至源码目录: {sourceConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从输出目录向上查找项目根目录，定位源码中的 Config\config.json。
        /// 返回 null 表示不在开发环境中运行（如发布部署场景）。
        /// </summary>
        private static string? FindSourceConfigPath()
        {
            try
            {
                var current = AppDomain.CurrentDomain.BaseDirectory;
                // 从输出目录（如 bin/Debug/net8.0-windows7.0/）向上最多走 5 层找项目根目录
                for (int i = 0; i < 5; i++)
                {
                    var parent = Directory.GetParent(current);
                    if (parent == null) break;
                    current = parent.FullName;

                    // 检查是否包含项目标识文件（.csproj / .slnx / .sln）
                    bool isProjectRoot = Directory.GetFiles(current, "*.csproj").Length > 0
                                      || Directory.GetFiles(current, "*.slnx").Length > 0
                                      || Directory.GetFiles(current, "*.sln").Length > 0;

                    if (isProjectRoot)
                    {
                        var srcConfigDir = Path.Combine(current, "Config");
                        if (Directory.Exists(srcConfigDir))
                            return Path.Combine(srcConfigDir, "config.json");
                    }
                }
            }
            catch { /* 非开发环境或无权限时静默跳过 */ }
            return null;
        }

        /// <summary>
        /// 核心路由逻辑：根据任务类型 (Flag) 和检测侧位 (Side)，从配置中找出应触发的相机 SN 列表。
        /// </summary>
        /// <param name="flag">任务类型标识。</param>
        /// <param name="side">检测侧位 ("left"/"right")。</param>
        /// <returns>目标相机 SN 列表。如果未找到映射，则返回模拟 SN 列表以保证流程完整性。</returns>
        public static List<string> GetTargetCameraSNs(int flag, string side)
        {
            if (Instance == null || Instance.Algorithms == null) 
                return new List<string>();

            CameraMapping mapping = null;
            
            // 根据确定的 Flag 映射到具体的算法配置子项
            switch (flag)
            {
                case 1: mapping = Instance.Algorithms.SlotOccupancy?.CameraMapping; break;
                case 2: mapping = Instance.Algorithms.StackerOffset?.CameraMapping; break;
                case 3: mapping = Instance.Algorithms.RackDeformation?.CameraMapping; break;
                case 4:
                case 5: mapping = Instance.Algorithms.VisualInventory?.CameraMapping; break;
            }

            if (mapping == null) 
                return new List<string>(); // 兜底返回空列表

            if (side?.ToLower() == "left" && mapping.LeftSideSns != null)
                return mapping.LeftSideSns;
            else if (side?.ToLower() == "right" && mapping.RightSideSns != null)
                return mapping.RightSideSns;

            return new List<string>();
        }

        /// <summary>
        /// 根据相机 SN 获取其外参标定结果，未标定时返回 null。
        /// </summary>
        public static CameraCalibration? GetCalibration(string sn)
        {
            if (Instance?.Calibrations == null) return null;
            return Instance.Calibrations.FirstOrDefault(c => c.CameraSn == sn);
        }
    }
}

