using System.Collections.Generic;
using System.ComponentModel;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Config
{
    /// <summary>
    /// 全局应用配置根对象，直接在代码中定义系统所有的默认配置参数。
    /// </summary>
    public class AppConfig
    {
    /// <summary>
    /// 系统级通用配置（日志级别等）。
    /// </summary>
    [Browsable(false)]
    public SystemConfig System { get; set; } = new SystemConfig();

    /// <summary>
    /// Redis 连接与数据库分区配置。
    /// </summary>
    [Browsable(false)]
    public RedisConfig Redis { get; set; } = new RedisConfig();

    /// <summary>
    /// 系统注册的相机硬件列表。
        /// </summary>
        [Browsable(false)]
        public List<CameraConfig> Cameras { get; set; } = new List<CameraConfig>
        {
            new CameraConfig { Sn = "207000168577", Type = "Tycam3D", Name = "3DCam-Right#1" },
            new CameraConfig { Sn = "207000168598", Type = "Tycam3D", Name = "3DCam-Right#2" },
            new CameraConfig { Sn = "207000168918", Type = "Tycam3D", Name = "3DCam-Left#1" },
            new CameraConfig { Sn = "207000169627", Type = "Tycam3D", Name = "3DCam-Left#2" },
            new CameraConfig { Sn = "DA9434653", Type = "Hikvision2D", Name = "2DCam-Right#1" },
            new CameraConfig { Sn = "DA9434361", Type = "Hikvision2D", Name = "2DCam-Right#2" },
            new CameraConfig { Sn = "DA9434411", Type = "Hikvision2D", Name = "2DCam-Left#1" },
            new CameraConfig { Sn = "DA9434623", Type = "Hikvision2D", Name = "2DCam-Left#2" }
        };

        /// <summary>
        /// 核心算法配置，包含所有子业务的阈值参数。
        /// </summary>
        [Category("核心算法设置"), DisplayName("阈值参数汇总")]
        public AlgorithmsConfig Algorithms { get; set; } = new AlgorithmsConfig();

        /// <summary>
        /// 3D 相机外参标定结果列表（相机坐标系 → 基准坐标系）。
        /// </summary>
        [Browsable(false)]
        public List<CameraCalibration> Calibrations { get; set; } = new();

        /// <summary>
        /// 2D 相机（海康）内参标定结果列表（棋盘格标定）。
        /// </summary>
        [Browsable(false)]
        public List<Camera2DCalibration>? Camera2DCalibrations { get; set; } = new();
    }

    /// <summary>
    /// 系统基础设置类。
    /// </summary>
    public class SystemConfig { public string LogLevel { get; set; } = "INFO"; }

    /// <summary>
    /// Redis 通讯参数配置类。
    /// </summary>
    public class RedisConfig 
    { 
        public string Host { get; set; } = "127.0.0.1"; 
        public string Port { get; set; } = "6379"; 
        public string Password { get; set; } = "cve123456"; 
        public int TaskDb { get; set; } = 0; 
        public int ResultDb { get; set; } = 1; 
    }

    /// <summary>
    /// 单台相机的硬件连接参数。
    /// </summary>
    public class CameraConfig { public string Sn { get; set; } public string Type { get; set; } public string Name { get; set; } }

    /// <summary>
    /// 算法参数汇总容器类，支持在属性栏展开。
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))
    ]
    public class AlgorithmsConfig
    {
        /// <summary>
        /// 货位占用检测的特定配置。
        /// </summary>
        [Category("01-货位检测"), DisplayName("货位占用阈值")]
        public SlotOccupancyConfig SlotOccupancy { get; set; } = new SlotOccupancyConfig();
        
        /// <summary>
        /// 堆垛机偏移检测的特定配置。
        /// </summary>
        [Category("02-堆垛机检测"), DisplayName("堆垛机偏移判定阈值")]
        public StackerOffsetConfig StackerOffset { get; set; } = new StackerOffsetConfig();
        
        /// <summary>
        /// 钢架/横梁变形检测的特定配置。
        /// </summary>
        [Category("03-横梁检测"), DisplayName("钢架变形判定阈值")]
        public RackDeformationConfig RackDeformation { get; set; } = new RackDeformationConfig();

        /// <summary>
        /// 视觉盘库检测的特定配置 (Flag 4/5)。
        /// 使用 2D 相机进行条码扫描。
        /// </summary>
        [Category("04-盘库检测"), DisplayName("视觉盘库配置")]
        public VisualInventoryConfig VisualInventory { get; set; } = new VisualInventoryConfig();
    }

    /// <summary>
    /// A/B/C/D 四级阈值判定集合，用于区间报警。
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))
    ]
    public class ThresholdSet
    {
        /// <summary>
        /// 极小值报警界限 (Value < A -> Alarm)。
        /// </summary>
        [DisplayName("A 门限 (报警 < A)")]
        public double A { get; set; }

        /// <summary>
        /// 负向预警界限 (A <= Value < B -> Warning)。
        /// </summary>
        [DisplayName("B 门限 (严重 < B)")]
        public double B { get; set; }

        /// <summary>
        /// 正向预警界限 (C < Value <= D -> Warning)。
        /// </summary>
        [DisplayName("C 门限 (警告 > C)")]
        public double C { get; set; }

        /// <summary>
        /// 极大值报警界限 (Value > D -> Alarm)。
        /// </summary>
        [DisplayName("D 门限 (报警 > D)")]
        public double D { get; set; }

        /// <summary>
        /// 简要显示当前阈值范围。
        /// </summary>
        public override string ToString() => $"A:{A} / D:{D}";
    }

    /// <summary>
    /// 货位占用检查相关参数。
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))
    ]
    public class SlotOccupancyConfig
    {
        /// <summary>
        /// 判定为有货的最小点数阈值。
        /// </summary>
        [DisplayName("所需点云最小数量"), Description("低于此数值判定为无物")]
        public int PointThreshold { get; set; } = 10000;

        /// <summary>
        /// 左侧 3D 感兴趣区域限制（仅作 fallback，已由 CameraRoiParams 替代）。
        /// </summary>
        [Browsable(false)]
        [Obsolete("请使用 CameraRoiParams 代替全局设定", false)]
        public List<int> Roi3dLeft { get; set; } = new List<int> { -500, 500, -500, 500, 1000, 3000 };

        /// <summary>
        /// 右侧 3D 感兴趣区域限制（仅作 fallback，已由 CameraRoiParams 替代）。
        /// </summary>
        [Browsable(false)]
        [Obsolete("请使用 CameraRoiParams 代替全局设定", false)]
        public List<int> Roi3dRight { get; set; } = new List<int> { -500, 500, -500, 500, 1000, 3000 };

        /// <summary>
        /// 每台相机的独立 ROI 参数。按相机 SN 匹配。
        /// </summary>
        [Browsable(false)]
        public List<CameraRoiParam> CameraRoiParams { get; set; } = new();

        /// <summary>
        /// 该算法对应的相机 SN 映射关系。
        /// </summary>
        [Browsable(false)]
        public CameraMapping CameraMapping { get; set; } = new CameraMapping
        {
            LeftSideSns = new List<string> { "207000168918", "207000169627" },
            RightSideSns = new List<string> { "207000168577", "207000168598" }
        };

        /// <summary>
        /// 根据相机 SN 查找对应的 ROI 参数，未找到返回 null。
        /// </summary>
        public CameraRoiParam? FindCameraParam(string? sn)
        {
            if (string.IsNullOrWhiteSpace(sn)) return null;
            return CameraRoiParams?.FirstOrDefault(p =>
                string.Equals(p.CameraSn, sn, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 堆垛机偏移量检测相关参数。
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))
    ]
    public class StackerOffsetConfig
    {
        /// <summary>
        /// 点云截取的最近距离限制 (mm)。全局默认值，每相机 ROI 未配置时使用。
        /// </summary>
        [Category("1. 有效深度控制"), DisplayName("默认最近检测距离 (mm)")]
        public int DepthMin { get; set; } = 1000;

        /// <summary>
        /// 点云截取的最远距离限制 (mm)。全局默认值，每相机 ROI 未配置时使用。
        /// </summary>
        [Category("1. 有效深度控制"), DisplayName("默认最远检测距离 (mm)")]
        public int DepthMax { get; set; } = 3000;

        /// <summary>
        /// 标准位置时检测到的货位开口中心 X 坐标 (mm)。全局默认值。
        /// </summary>
        [Category("2. 标准位置"), DisplayName("默认标准开口中心X (mm)")]
        public double ReferenceGapCenterX { get; set; } = 0.0;

        /// <summary>
        /// 堆垛机左右偏移阈值组。
        /// </summary>
        [Category("3. 偏移判定门限"), DisplayName("左右偏移门限")]
        public ThresholdSet LateralThreshold { get; set; } = new ThresholdSet();

        /// <summary>
        /// 每台相机的独立 ROI 参数。按相机 SN 匹配，优先级高于全局默认值。
        /// 通过调参工具针对每台相机单独标定后保存。
        /// </summary>
        [Browsable(false)]
        public List<CameraRoiParam> CameraRoiParams { get; set; } = new();

        /// <summary>
        /// 该算法对应的相机 SN 映射关系。
        /// </summary>
        [Browsable(false)]
        public CameraMapping CameraMapping { get; set; } = new CameraMapping
        {
            LeftSideSns = new List<string> { "207000168918", "207000169627" },
            RightSideSns = new List<string> { "207000168577", "207000168598" }
        };

        /// <summary>
        /// 根据相机 SN 查找对应的 ROI 参数，未找到返回 null。
        /// </summary>
        public CameraRoiParam? FindCameraParam(string? sn)
        {
            if (string.IsNullOrWhiteSpace(sn)) return null;
            return CameraRoiParams?.FirstOrDefault(p =>
                string.Equals(p.CameraSn, sn, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 单台相机的堆垛机偏移检测 ROI 参数。
    /// 每台相机拍摄的货架区域不同，需要独立标定 X/Y 范围、深度范围和标准位置。
    /// </summary>
    public class CameraRoiParam
    {
        /// <summary>相机序列号，用于匹配</summary>
        public string CameraSn { get; set; } = "";

        /// <summary>ROI X 轴最小坐标 (mm)，导轨方向</summary>
        public double XMin { get; set; }

        /// <summary>ROI X 轴最大坐标 (mm)，导轨方向</summary>
        public double XMax { get; set; }

        /// <summary>ROI Y 轴最小坐标 (mm)，垂直地面方向（0=使用全局默认 YHalf 对称范围）</summary>
        public double YMin { get; set; }

        /// <summary>ROI Y 轴最大坐标 (mm)，垂直地面方向（0=使用全局默认 YHalf 对称范围）</summary>
        public double YMax { get; set; }

        /// <summary>ROI 最近深度 (mm)</summary>
        public int ZMin { get; set; }

        /// <summary>ROI 最远深度 (mm)</summary>
        public int ZMax { get; set; }

        /// <summary>标准位置参考值 (mm)，当前开口中心 X 与参考值的差 = 偏移量</summary>
        public double ReferenceX { get; set; }

        /// <summary>标准位置左立柱变形量 (mm)，用于变形检测差值计算</summary>
        public double RefRackDefLeft { get; set; }

        /// <summary>标准位置右立柱变形量 (mm)，用于变形检测差值计算</summary>
        public double RefRackDefRight { get; set; }

        /// <summary>标准位置左托臂下垂角度 (°)，用于变形检测差值计算</summary>
        public double RefArmAngleLeft { get; set; }

        /// <summary>标准位置右托臂下垂角度 (°)，用于变形检测差值计算</summary>
        public double RefArmAngleRight { get; set; }
    }

    /// <summary>
    /// 横梁与钢架变形检测相关参数。
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))
    ]
    public class RackDeformationConfig
    {
        /// <summary>
        /// 点云截取的最近距离限制 (mm)。全局默认值，每相机 ROI 未配置时使用。
        /// </summary>
        [Category("1. 有效深度控制"), DisplayName("默认最近检测距离 (mm)")]
        public int DepthMin { get; set; } = 1000;

        /// <summary>
        /// 点云截取的最远距离限制 (mm)。全局默认值，每相机 ROI 未配置时使用。
        /// </summary>
        [Category("1. 有效深度控制"), DisplayName("默认最远检测距离 (mm)")]
        public int DepthMax { get; set; } = 3000;

        /// <summary>
        /// 用于计算相对位移的基准面 Z 轴参考高度。
        /// </summary>
        [Category("2. 参考面基准"), DisplayName("基准面 Z 高度")]
        public int ReferenceZ { get; set; } = 2000;

        /// <summary>
        /// 横梁下挠量报警阈值。
        /// </summary>
        [Category("2. 判定门限"), DisplayName("横梁下塌门限")]
        public ThresholdSet BeamThreshold { get; set; } = new ThresholdSet();

        /// <summary>
        /// 钢架/立柱偏移量报警阈值。
        /// </summary>
        [Category("2. 判定门限"), DisplayName("钢架/立柱偏移门限")]
        public ThresholdSet RackThreshold { get; set; } = new ThresholdSet();

        /// <summary>
        /// 该算法对应的相机 SN 映射关系。
        /// </summary>
        [Browsable(false)]
        public CameraMapping CameraMapping { get; set; } = new CameraMapping
        {
            LeftSideSns = new List<string> { "207000168918", "207000169627" },
            RightSideSns = new List<string> { "207000168577", "207000168598" }
        };

        /// <summary>
        /// 每台相机的独立 ROI 参数。按相机 SN 匹配，优先级高于全局默认值。
        /// </summary>
        [Browsable(false)]
        public List<CameraRoiParam> CameraRoiParams { get; set; } = new();

        /// <summary>
        /// 根据相机 SN 查找对应的 ROI 参数，未找到返回 null。
        /// </summary>
        public CameraRoiParam? FindCameraParam(string? sn)
        {
            if (string.IsNullOrWhiteSpace(sn)) return null;
            return CameraRoiParams?.FirstOrDefault(p =>
                string.Equals(p.CameraSn, sn, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 定义不同检测方位（左/右）与实际相机硬件 SN 的映射关系。
    /// </summary>
    public class CameraMapping
    {
        /// <summary>
        /// 映射到左侧检测方位的相机序列号列表。
        /// </summary>
        public List<string> LeftSideSns { get; set; } = new List<string>();

        /// <summary>
        /// 映射到右侧检测方位的相机序列号列表。
        /// </summary>
        public List<string> RightSideSns { get; set; } = new List<string>();
    }

    /// <summary>
    /// 视觉盘库检测配置 (Flag 4/5)。
    /// 使用 2D 相机拍摄料箱条码，通过图像处理进行解码。
    /// </summary>
    public class VisualInventoryConfig
    {
        /// <summary>
        /// 该算法对应的 2D 相机 SN 映射关系。
        /// Flag=4 启动扫码，Flag=5 停止扫码。
        /// </summary>
        [Browsable(false)]
        public CameraMapping CameraMapping { get; set; } = new CameraMapping
        {
            LeftSideSns = new List<string> { "DA9434411", "DA9434623" },
            RightSideSns = new List<string> { "DA9434653", "DA9434361" }
        };
    }

    /// <summary>
    /// 2D 相机（海康）单目内参标定结果，由棋盘格标定工具计算并保存。
    /// </summary>
    public class Camera2DCalibration
    {
        /// <summary>相机序列号</summary>
        public string CameraSn { get; set; } = "";

        /// <summary>标定完成时间</summary>
        public DateTime CalibratedAt { get; set; }

        /// <summary>重投影误差 (px)</summary>
        public double RmsError { get; set; }

        /// <summary>水平焦距 (px)</summary>
        public double Fx { get; set; }

        /// <summary>垂直焦距 (px)</summary>
        public double Fy { get; set; }

        /// <summary>主点 X 坐标 (px)</summary>
        public double Cx { get; set; }

        /// <summary>主点 Y 坐标 (px)</summary>
        public double Cy { get; set; }

        /// <summary>畸变系数 [k1, k2, p1, p2, k3]</summary>
        public List<double> DistCoeffs { get; set; } = new();

        /// <summary>左侧立柱孔洞检测 ROI (8点坐标集合)</summary>
        public List<int> RoiLeft { get; set; } = new();

        /// <summary>右侧立柱孔洞检测 ROI (8点坐标集合)</summary>
        public List<int> RoiRight { get; set; } = new();

        /// <summary>盘库条码扫码检测 ROI (8点坐标集合: x1,y1,x2,y2,x3,y3,x4,y4)</summary>
        public List<int> RoiInventory { get; set; } = new();

        /// <summary>标准位置堆垛机中心线 X 坐标参考值 (像素)</summary>
        public float? ReferenceOffsetX { get; set; }
    }
}

