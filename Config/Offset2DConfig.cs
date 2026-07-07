using System;
using System.Collections.Generic;

namespace pallet_storage_detection_system_Net_V2.Config
{
    /// <summary>
    /// 2D 偏移测试多相机全局配置文件结构
    /// </summary>
    public class OffsetTestConfig
    {
        public Dictionary<string, Camera2DOffsetParam> Cameras { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public void MigrateAll()
        {
            if (Cameras != null)
            {
                foreach (var param in Cameras.Values)
                {
                    param.MigrateOldConfig();
                }
            }
        }
    }

    /// <summary>
    /// 2D 偏移测试单相机参数设定
    /// </summary>
    public class Camera2DOffsetParam
    {
        public int CValue { get; set; } = 15;
        public int BlockSize { get; set; } = 51;
        public int MinArea { get; set; } = 30;
        public int MaxArea { get; set; } = 8000;
        public int LinesToFit { get; set; } = 1;
        
        public double RoundAspectMin { get; set; } = 0.55;
        public double RoundSolidMin { get; set; } = 0.95;
        
        public double SlotAspectMin { get; set; } = 0.10;
        public double SlotAspectMax { get; set; } = 0.55;
        public double SlotSolidMin { get; set; } = 0.90;

        // 区分货架尺寸 (1380 和 2180) 的 ROI 与标准参考值
        public List<System.Drawing.Point> RoiPoints1380 { get; set; } = new();
        public float? ReferenceOffsetX1380 { get; set; }

        public List<System.Drawing.Point> RoiPoints2180 { get; set; } = new();
        public float? ReferenceOffsetX2180 { get; set; }

        // 旧的单组属性，用于兼容以前保存的数据
        public List<System.Drawing.Point> RoiPoints { get; set; } = new();
        public float? ReferenceOffsetX { get; set; }

        public void MigrateOldConfig()
        {
            if (RoiPoints != null && RoiPoints.Count == 4)
            {
                if (RoiPoints1380 == null || RoiPoints1380.Count == 0)
                {
                    RoiPoints1380 = new List<System.Drawing.Point>(RoiPoints);
                }
            }
            if (ReferenceOffsetX.HasValue)
            {
                if (!ReferenceOffsetX1380.HasValue)
                {
                    ReferenceOffsetX1380 = ReferenceOffsetX;
                }
            }
        }
    }
}
