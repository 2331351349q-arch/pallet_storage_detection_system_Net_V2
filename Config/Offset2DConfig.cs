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

        public List<System.Drawing.Point> RoiPoints { get; set; } = new();
        public float? ReferenceOffsetX { get; set; }
    }
}
