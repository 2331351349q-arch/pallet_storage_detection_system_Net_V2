using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;
using material_box_storage_detection_system_Net.Models;

namespace material_box_storage_detection_system_Net.Algorithms
{
    /// <summary>
    /// 堆垛机偏移检测算法类 (Flag 2)。
    /// 用于确认堆垛机货叉能否正确插入货架，检测堆垛机是否处于标准位置。
    /// 只返回左右偏移量，不检测前后偏移和旋转量。
    /// 
    /// 坐标系说明（标定后的基准坐标系）：
    ///   X 轴 — 导轨方向，即堆垛机运动方向，也是本算法检测的左右偏移方向
    ///   Y 轴 — 垂直地面
    ///   Z 轴 — 垂直于货架面的深度方向
    /// 
    /// 核心思路：
    ///   利用货架竖直支撑梁的深度特征（梁面比凹槽更靠近相机），
    ///   通过 X 轴深度剖面 + 梯度边缘检测定位梁的 X 边界，
    ///   进而计算货位开口中心与堆垛机位置的左右偏差。
    /// </summary>
    public static class StackerOffsetAlgo
    {
        // ---- 可调参数 ----

        /// <summary>X 轴分 bin 分辨率 (mm)</summary>
        internal const double BinSizeMm = 5.0;

        /// <summary>梁表面判定阈值：Z 值小于背景中位数的该比例时视为梁表面</summary>
        internal const double BeamDepthRatio = 0.75;

        /// <summary>梁表面最少连续 bin 数（避免噪点误判）</summary>
        internal const int MinBeamBinWidth = 3;

        /// <summary>
        /// 托臂/螺丝剔除容差 (mm)。
        /// 扫描梁的内侧边缘时，深度超出梁平坦表面此值即认为进入了托臂区域。
        /// 合理范围: 15~40 mm，可根据现场托臂厚度适当调整。
        /// </summary>
        internal const double BracketSurfaceTolerance = 25.0;

        /// <summary>Y 方向梁高度 ROI 范围 (±mm)，以相机安装高度为中心</summary>
        internal const double BeamHeightHalfRange = 800.0;

        /// <summary>最少有效点数阈值</summary>
        internal const int MinPointCount = 500;

        /// <summary>单侧偏移量异常值上限 (mm)，超过此值忽略</summary>
        internal const double MaxOffsetMm = 100.0;

        // ============================================================

        /// <summary>
        /// 堆垛机偏移检测的调试/可视化数据。
        /// 包含算法中间结果，用于调参工具的剖面图和效果渲染。
        /// </summary>
        public class DebugData
        {
            /// <summary>计算是否成功</summary>
            public bool Success { get; set; }

            /// <summary>当前检测到的开口中心 X (mm)</summary>
            public double CurrentGapCenterX { get; set; }

            /// <summary>配置中的标准位置开口中心 X (mm)</summary>
            public double ReferenceGapCenterX { get; set; }

            /// <summary>最终偏移量 = CurrentGapCenterX - ReferenceGapCenterX (mm)</summary>
            public double LateralOffsetMm { get; set; }

            /// <summary>ROI 内总点数</summary>
            public int RoiPointCount { get; set; }

            /// <summary>X 轴起始 (mm)</summary>
            public double XMin { get; set; }

            /// <summary>X 轴结束 (mm)</summary>
            public double XMax { get; set; }

            /// <summary>Bin 数量</summary>
            public int BinCount { get; set; }

            /// <summary>每个 bin 的 X 中心坐标 (mm)</summary>
            public double[] BinXCenters { get; set; } = Array.Empty<double>();

            /// <summary>每个 bin 的最小 Z 深度剖面 (mm)，无效 bin = double.MaxValue</summary>
            public double[] DepthProfile { get; set; } = Array.Empty<double>();

            /// <summary>每个 bin 的点数</summary>
            public int[] BinPopulation { get; set; } = Array.Empty<int>();

            /// <summary>背景深度中位数 (mm)</summary>
            public double BackgroundZ { get; set; }

            /// <summary>梁判定阈值 (mm)，Z 小于此值判定为梁</summary>
            public double BeamZThreshold { get; set; }

            /// <summary>检测到的梁区域列表</summary>
            public List<(double leftX, double rightX, double centerX, double width)> BeamRegions { get; set; } = new();

            /// <summary>主货位开口中心 X (mm)</summary>
            public double GapCenterX { get; set; }

            /// <summary>主货位开口宽度 (mm)</summary>
            public double GapWidthMm { get; set; }

            /// <summary>所有开口信息</summary>
            public List<(double leftX, double rightX, double centerX, double width)> AllGaps { get; set; } = new();

            /// <summary>错误信息（如有）</summary>
            public string ErrorMessage { get; set; } = string.Empty;

            // ---- 托臂精化边缘（用于高精度开口计算）----
            /// <summary>精化后左梁内侧右边缘 X (mm)，去除托臂影响</summary>
            public double RefinedLeftBeamInnerX { get; set; } = double.NaN;
            /// <summary>精化后右梁内侧左边缘 X (mm)，去除托臂影响</summary>
            public double RefinedRightBeamInnerX { get; set; } = double.NaN;
            /// <summary>精化后的开口中心 X (mm)</summary>
            public double RefinedGapCenterX { get; set; } = double.NaN;
            /// <summary>被识别为托臂/凸起的宽度 (mm)，左右各一</summary>
            public double LeftBracketWidthMm { get; set; } = 0;
            public double RightBracketWidthMm { get; set; } = 0;
        }

        // ============================================================

        /// <summary>
        /// 执行堆垛机位置偏移计算。
        /// 检测当前帧中货位开口中心，与配置中的标准位置 ReferenceGapCenterX 比较得到偏差。
        /// 优先使用该相机独立标定的 ROI 参数（CameraRoiParams），未配置时回退全局默认值。
        /// </summary>
        /// <param name="img">深度帧数据（DepthFrameData）。</param>
        /// <param name="res">检测结果模型，偏移量写入对应侧的值。</param>
        /// <returns>算法是否执行成功。</returns>
        public static bool Run(object img, DetectionResult res)
        {
            try
            {
                var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
                var frame = img as DepthFrameData;

                // 查找该相机的独立 ROI 参数
                var camParam = cfg?.FindCameraParam(frame?.CameraSn);

                double zMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
                double zMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
                double reference = camParam?.ReferenceX ?? cfg?.ReferenceGapCenterX ?? 0.0;
                double? xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : null;
                double? xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : null;
                double? yMinRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : null;
                double? yMaxRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax : null;

                var basePoints = GetBasePointsFromFrame(frame);
                if (basePoints == null)
                {
                    res.OffsetLatMmValue = 0;
                    return false;
                }

                double cameraZ = 0;
                var calib = ConfigManager.GetCalibration(frame?.CameraSn);
                if (calib != null && calib.IsValid)
                {
                    cameraZ = calib.TranslationVector[2];
                }

                double currentGapCenter = ComputeLateralOffset(basePoints, cameraZ, zMin, zMax, yMinRoI, yMaxRoI, xMinRoI, xMaxRoI);
                if (double.IsNaN(currentGapCenter))
                {
                    res.OffsetLatMmValue = 0;
                    return false;
                }

                // 偏差 = 当前开口中心 - 标准位置开口中心
                // 正值 = 堆垛机偏右（需左移修正），负值 = 偏左（需右移修正）
                double offset = currentGapCenter - reference;
                res.OffsetLatMmValue = Math.Round(offset, 2);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StackerOffsetAlgo] 异常: {ex.Message}");
                res.OffsetLatMmValue = 0;
                return false;
            }
        }

        /// <summary>
        /// 调试模式：执行完整检测并返回中间可视化数据。
        /// 用于调参工具实时预览。优先使用该相机独立标定的 ROI 参数。
        /// </summary>
        /// <param name="frame">深度帧数据。</param>
        /// <param name="zMin">DepthMin (mm)。</param>
        /// <param name="zMax">DepthMax (mm)。</param>
        /// <param name="referenceGapCenterX">标准位置开口中心 X (mm)。</param>
        /// <param name="xMinRoI">可选：X 轴 ROI 最小值，用于限定搜索范围。</param>
        /// <param name="xMaxRoI">可选：X 轴 ROI 最大值。</param>
        /// <param name="yMinRoI">可选：Y 轴 ROI 最小值，垂直地面方向。未指定时使用 ±BeamHeightHalfRange。</param>
        /// <param name="yMaxRoI">可选：Y 轴 ROI 最大值，垂直地面方向。</param>
        /// <returns>包含所有中间数据的 DebugData。</returns>
        public static DebugData RunDebug(DepthFrameData? frame, double zMin, double zMax,
            double referenceGapCenterX = 0, double? xMinRoI = null, double? xMaxRoI = null,
            double? yMinRoI = null, double? yMaxRoI = null)
        {
            var debug = new DebugData { ReferenceGapCenterX = referenceGapCenterX };

            try
            {
                var basePoints = GetBasePointsFromFrame(frame);
                if (basePoints == null || basePoints.Count < MinPointCount)
                {
                    debug.ErrorMessage = basePoints == null
                        ? "无深度帧数据"
                        : $"有效点数不足 ({basePoints.Count} < {MinPointCount})";
                    return debug;
                }

                double cameraZ = 0;
                var calib = ConfigManager.GetCalibration(frame?.CameraSn);
                if (calib != null && calib.IsValid)
                {
                    cameraZ = calib.TranslationVector[2];
                }

                debug = ComputeLateralOffsetDebug(basePoints, cameraZ, zMin, zMax, xMinRoI, xMaxRoI, yMinRoI, yMaxRoI);
                debug.ReferenceGapCenterX = referenceGapCenterX;

                // 计算与标准位置的偏差
                if (debug.Success)
                {
                    debug.CurrentGapCenterX = debug.GapCenterX;
                    debug.LateralOffsetMm = debug.GapCenterX - referenceGapCenterX;
                }

                return debug;
            }
            catch (Exception ex)
            {
                debug.ErrorMessage = ex.Message;
                return debug;
            }
        }

        // ============================================================
        // 点云获取与坐标变换
        // ============================================================

        /// <summary>
        /// 从 DepthFrameData 获取点云并变换到基准坐标系，
        /// 同时将相机深度方向（正前方）重定向到 Z 轴，保证后续算法和可视化的一致性。
        /// </summary>
        public static List<Vector3>? GetBasePointsFromFrame(DepthFrameData? frame)
        {
            if (frame == null)
                return null;

            // 获取相机坐标系点云（优先 SDK 原生点云，回退手动公式）
            var camPoints = frame.GetPointCloud();
            if (camPoints == null || camPoints.Count < MinPointCount)
                return null;

            // 查找该相机的标定参数
            var calib = ConfigManager.GetCalibration(frame.CameraSn);
            if (calib == null || !calib.IsValid)
            {
                // 无有效标定时直接返回原始点云（相机坐标系即基准，深度=Z 天然满足）
                return camPoints;
            }

            // 变换：P_base = R * P_cam + T
            var basePoints = new List<Vector3>(camPoints.Count);
            foreach (var pt in camPoints)
            {
                basePoints.Add(CalibrationAlgo.TransformPoint(pt, calib));
            }

            return basePoints;
        }

        /// <summary>获取相机深度方向在基准坐标系中的映射描述，用于 UI 显示。</summary>
        public static string GetDepthAxisDescription(string cameraSn)
        {
            var calib = ConfigManager.GetCalibration(cameraSn);
            if (calib == null || !calib.IsValid)
                return "无标定（原始坐标系）";

            return "标定已应用（基准坐标系）";
        }

        // ============================================================
        // 核心偏移计算
        // ============================================================

        /// <summary>
        /// 调试版本：计算偏移量并填充所有中间可视化数据。
        /// </summary>
        private static DebugData ComputeLateralOffsetDebug(
            List<Vector3> basePoints, double cameraZ,
            double zMin, double zMax,
            double? xMinRoI = null, double? xMaxRoI = null,
            double? yMinRoI = null, double? yMaxRoI = null)
        {
            var d = new DebugData();
            double yLo = yMinRoI ?? -BeamHeightHalfRange;
            double yHi = yMaxRoI ?? BeamHeightHalfRange;

            var segRes = CloudSegmentationHelper.Segment(
                basePoints, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                BinSizeMm, MinBeamBinWidth, MinPointCount, false);

            d.ErrorMessage = segRes.ErrorMessage;
            d.RoiPointCount = segRes.RoiPoints?.Count ?? 0;
            d.XMin = segRes.XMin; 
            d.XMax = segRes.XMax; 
            d.BinCount = segRes.BinCount;
            d.BinXCenters = segRes.BinXCenters; 
            d.DepthProfile = segRes.DepthProfile; 
            d.BinPopulation = segRes.BinPopulation;
            d.BackgroundZ = segRes.BackgroundZ; 
            d.BeamZThreshold = segRes.BeamZThreshold;
            d.BeamRegions = segRes.BeamRegions; 
            d.AllGaps = segRes.AllGaps;

            if (!segRes.Success)
            {
                return d;
            }

            d.Success = true;
            d.GapWidthMm = segRes.GapWidthMm;
            d.RefinedLeftBeamInnerX = segRes.RefinedLeftBeamInnerX;
            d.RefinedRightBeamInnerX = segRes.RefinedRightBeamInnerX;
            d.RefinedGapCenterX = segRes.RefinedGapCenterX;
            d.LeftBracketWidthMm = segRes.LeftBracketWidthMm;
            d.RightBracketWidthMm = segRes.RightBracketWidthMm;
            
            d.GapCenterX = segRes.GapCenterX;
            
            if (Math.Abs(d.GapCenterX) > MaxOffsetMm) 
            { 
                d.Success = false;
                d.ErrorMessage = $"偏移异常({d.GapCenterX:F1}mm>{MaxOffsetMm}mm)"; 
                return d; 
            }
            
            d.LateralOffsetMm = d.GapCenterX;

            return d;
        }

        /// <summary>
        /// 从基准坐标系点云计算堆垛机左右偏移量。
        /// </summary>
        private static double ComputeLateralOffset(
            List<Vector3> basePoints, double cameraZ,
            double zMin, double zMax,
            double? yMinRoI = null, double? yMaxRoI = null,
            double? xMinRoI = null, double? xMaxRoI = null)
        {
            double yLo = yMinRoI ?? -BeamHeightHalfRange;
            double yHi = yMaxRoI ?? BeamHeightHalfRange;

            var segRes = CloudSegmentationHelper.Segment(
                basePoints, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                BinSizeMm, MinBeamBinWidth, MinPointCount, false);

            if (!segRes.Success || Math.Abs(segRes.GapCenterX) > MaxOffsetMm)
                return double.NaN;

            return segRes.GapCenterX;
        }
    }
}
