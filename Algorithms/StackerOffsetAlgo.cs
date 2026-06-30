using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Algorithms
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
        internal const int MinPointCount = 200;

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
        /// <param name="img1">深度帧数据 1。</param>
        /// <param name="img2">深度帧数据 2（可能为 null）。</param>
        /// <param name="res">检测结果模型，偏移量写入对应侧的值。</param>
        /// <returns>算法是否执行成功。</returns>
        public static bool Run(object img1, object img2, DetectionResult res)
        {
            try
            {
                var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
                var frame1 = img1 as DepthFrameData;
                var frame2 = img2 as DepthFrameData;

                var offsets = new List<double>();

                // 处理第一台相机
                if (frame1 != null)
                {
                    var pts1 = GetFilteredPoints(frame1, cfg, out double ref1);
                    if (pts1 != null && pts1.Count >= MinPointCount)
                    {
                        double center1 = ComputeLateralOffset(pts1, 0, -10000, 10000, null, null, null, null, cfg);
                        if (!double.IsNaN(center1))
                        {
                            double offset1 = center1 - ref1;
                            if (Math.Abs(offset1) <= MaxOffsetMm) offsets.Add(offset1);
                        }
                    }
                }

                // 处理第二台相机
                if (frame2 != null)
                {
                    var pts2 = GetFilteredPoints(frame2, cfg, out double ref2);
                    if (pts2 != null && pts2.Count >= MinPointCount)
                    {
                        double center2 = ComputeLateralOffset(pts2, 0, -10000, 10000, null, null, null, null, cfg);
                        if (!double.IsNaN(center2))
                        {
                            double offset2 = center2 - ref2;
                            if (Math.Abs(offset2) <= MaxOffsetMm) offsets.Add(offset2);
                        }
                    }
                }

                if (offsets.Count == 0)
                {
                    res.OffsetLatMmValue = 0;
                    return false;
                }

                // 偏差 = 当前单立柱中心 - 标准位置单立柱中心
                // 取有效相机的平均值
                double offset = offsets.Average();
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
        /// 用于调参工具实时预览。处理双相机的点云合并。
        /// </summary>
        /// <param name="frame1">深度帧数据1。</param>
        /// <param name="frame2">深度帧数据2。</param>
        /// <param name="zMin">全局 DepthMin (mm) 作为备用。</param>
        /// <param name="zMax">全局 DepthMax (mm) 作为备用。</param>
        /// <param name="referenceGapCenterX">标准位置开口中心 X (mm)。</param>
        /// <param name="xMinRoI">可选：X 轴 ROI 最小值。</param>
        /// <param name="xMaxRoI">可选：X 轴 ROI 最大值。</param>
        /// <param name="yMinRoI">可选：Y 轴 ROI 最小值。</param>
        /// <param name="yMaxRoI">可选：Y 轴 ROI 最大值。</param>
        /// <param name="tuneCameraSn">当前正在调参的相机SN，该相机的 ROI 参数将使用传入的这些参数。</param>
        /// <returns>包含所有中间数据的 DebugData。</returns>
        public static DebugData RunDebug(DepthFrameData? frame1, DepthFrameData? frame2, double zMin, double zMax,
            double referenceGapCenterX = 0, double? xMinRoI = null, double? xMaxRoI = null,
            double? yMinRoI = null, double? yMaxRoI = null, string tuneCameraSn = "")
        {
            var debug = new DebugData { ReferenceGapCenterX = referenceGapCenterX };

            try
            {
                var cfg = ConfigManager.Instance?.Algorithms?.StackerOffset;
                
                // 仅处理当前正在调参的相机
                var targetFrame = (frame1 != null && frame1.CameraSn == tuneCameraSn) ? frame1 : 
                                  (frame2 != null && frame2.CameraSn == tuneCameraSn) ? frame2 : 
                                  frame1 ?? frame2;

                if (targetFrame == null)
                {
                    debug.ErrorMessage = "无有效深度帧";
                    return debug;
                }

                var basePoints = GetFilteredPointsForDebug(targetFrame, cfg, tuneCameraSn, zMin, zMax, xMinRoI, xMaxRoI, yMinRoI, yMaxRoI);

                if (basePoints == null || basePoints.Count < MinPointCount)
                {
                    debug.ErrorMessage = $"有效点数不足 ({(basePoints?.Count ?? 0)} < {MinPointCount})";
                    return debug;
                }

                debug = ComputeLateralOffsetDebug(basePoints, 0, -10000, 10000, null, null, null, null, cfg);
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
                debug.ErrorMessage = $"算法异常: {ex.Message}";
                return debug;
            }
        }

        // ============================================================
        // 点云获取与坐标变换
        // ============================================================

        /// <summary>
        /// 获取应用了相机各自 ROI 过滤后的基准点云。
        /// </summary>
        public static List<Vector3>? GetFilteredPoints(DepthFrameData frame, StackerOffsetConfig? cfg, out double reference)
        {
            reference = 0;
            var pts = GetBasePointsFromFrame(frame);
            if (pts == null) return null;

            var camParam = cfg?.FindCameraParam(frame.CameraSn);
            double zMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
            double zMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
            reference = camParam?.ReferenceX ?? cfg?.ReferenceGapCenterX ?? 0.0;
            double? xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : null;
            double? xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : null;
            double yMinRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : -BeamHeightHalfRange;
            double yMaxRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax : BeamHeightHalfRange;

            var filtered = new List<Vector3>(pts.Count);
            foreach (var p in pts)
            {
                if (p.Z >= zMin && p.Z <= zMax &&
                    (!xMinRoI.HasValue || p.X >= xMinRoI.Value) && (!xMaxRoI.HasValue || p.X <= xMaxRoI.Value) &&
                    (p.Y >= yMinRoI && p.Y <= yMaxRoI))
                {
                    filtered.Add(p);
                }
            }
            return filtered;
        }

        private static List<Vector3>? GetFilteredPointsForDebug(DepthFrameData frame, StackerOffsetConfig? cfg, string tuneCameraSn, 
            double zMin, double zMax, double? xMinRoI, double? xMaxRoI, double? yMinRoI, double? yMaxRoI)
        {
            var pts = GetBasePointsFromFrame(frame);
            if (pts == null) return null;

            double applyZMin = zMin, applyZMax = zMax;
            double? applyXMin = xMinRoI, applyXMax = xMaxRoI, applyYMin = yMinRoI, applyYMax = yMaxRoI;

            // 如果这个相机不是正在调参的那个相机，就用配置文件里存的值
            if (frame.CameraSn != tuneCameraSn)
            {
                var camParam = cfg?.FindCameraParam(frame.CameraSn);
                applyZMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
                applyZMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
                applyXMin = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : null;
                applyXMax = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : null;
                applyYMin = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : -BeamHeightHalfRange;
                applyYMax = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax : BeamHeightHalfRange;
            }

            var filtered = new List<Vector3>(pts.Count);
            foreach (var p in pts)
            {
                if (p.Z >= applyZMin && p.Z <= applyZMax &&
                    (!applyXMin.HasValue || p.X >= applyXMin.Value) && (!applyXMax.HasValue || p.X <= applyXMax.Value) &&
                    (!applyYMin.HasValue || p.Y >= applyYMin.Value) && (!applyYMax.HasValue || p.Y <= applyYMax.Value))
                {
                    filtered.Add(p);
                }
            }
            return filtered;
        }

        /// <summary>
        /// 从 DepthFrameData 获取点云并变换到基准坐标系，
        /// 同时将相机深度方向（正前方）重定向到 Z 轴，保证后续算法 and 可视化的一致性。
        /// </summary>
        public static List<Vector3>? GetBasePointsFromFrame(DepthFrameData? frame)
        {
            if (frame == null)
                return null;

            return frame.GetPointCloud();
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
            double? yMinRoI = null, double? yMaxRoI = null,
            StackerOffsetConfig? cfg = null)
        {
            if (cfg != null && cfg.UseRansac)
            {
                return ComputeLateralOffsetDebugByRansac(basePoints, cfg.RansacMaxIterations, cfg.RansacDistanceThreshold, xMinRoI, xMaxRoI);
            }

            var d = new DebugData();
            double yLo = yMinRoI ?? -10000;
            double yHi = yMaxRoI ?? 10000;

            var segRes = CloudSegmentationHelper.Segment(
                basePoints, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                BinSizeMm, MinBeamBinWidth, MinPointCount, false, null, null, requireGap: false);

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
            
            d.GapCenterX = segRes.PrimaryBeamCenterX; // 用单立柱中心复用 GapCenterX，兼容下层UI
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
            double? xMinRoI = null, double? xMaxRoI = null,
            StackerOffsetConfig? cfg = null)
        {
            if (cfg != null && cfg.UseRansac)
            {
                return ComputeLateralOffsetByRansac(basePoints, cfg.RansacMaxIterations, cfg.RansacDistanceThreshold);
            }

            double yLo = yMinRoI ?? -10000;
            double yHi = yMaxRoI ?? 10000;

            var segRes = CloudSegmentationHelper.Segment(
                basePoints, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                BinSizeMm, MinBeamBinWidth, MinPointCount, false, null, null, requireGap: false);

            if (!segRes.Success)
                return double.NaN;

            return segRes.PrimaryBeamCenterX;
        }

        // ============================================================
        // RANSAC 辅助算法实现
        // ============================================================

        /// <summary>
        /// 针对立柱前表面的定制 RANSAC 平面拟合算法。
        /// 约束拟合平面的法向量必须基本朝向相机（Z 轴方向）。
        /// </summary>
        private static (double A, double B, double C, double D, List<int> Inliers)
            RansacPlaneFittingStacker(List<Vector3> points, int maxIterations, double distanceThreshold, double minNormalZ = 0.95)
        {
            if (points.Count < 3)
                return (0, 0, 0, 0, new List<int>());

            double bestA = 0, bestB = 0, bestC = 0, bestD = 0;
            int bestCount = 0;
            List<int> bestInliers = new();

            var rng = new Random(42); // 固定种子确保算法输出确定性
            int n = points.Count;

            int maxFails = maxIterations * 3;
            int fails = 0;

            for (int iter = 0; iter < maxIterations && fails < maxFails; iter++)
            {
                int i0 = rng.Next(n);
                int i1 = rng.Next(n);
                while (i1 == i0) i1 = rng.Next(n);
                int i2 = rng.Next(n);
                while (i2 == i0 || i2 == i1) i2 = rng.Next(n);

                Vector3 p0 = points[i0], p1 = points[i1], p2 = points[i2];

                Vector3 v1 = p1 - p0;
                Vector3 v2 = p2 - p0;
                Vector3 normal = Vector3.Cross(v1, v2);

                float lenSq = normal.LengthSquared();
                if (lenSq < 1e-6f)
                {
                    fails++;
                    iter--;
                    continue;
                }

                normal = Vector3.Normalize(normal);
                
                // 朝向约束：立柱前表面法向在 Z 轴上的分量应接近 1
                if (Math.Abs(normal.Z) < minNormalZ)
                {
                    fails++;
                    iter--;
                    continue;
                }

                double a = normal.X, b = normal.Y, c = normal.Z;
                double d = -(a * p0.X + b * p0.Y + c * p0.Z);

                var inliers = new List<int>();
                for (int j = 0; j < n; j++)
                {
                    Vector3 pt = points[j];
                    double dist = Math.Abs(a * pt.X + b * pt.Y + c * pt.Z + d);
                    if (dist < distanceThreshold)
                    {
                        inliers.Add(j);
                    }
                }

                if (inliers.Count > bestCount)
                {
                    bestCount = inliers.Count;
                    bestA = a; bestB = b; bestC = c; bestD = d;
                    bestInliers = inliers;
                }
            }

            // 使用所有内点重新拟合消除随机性带来的平移偏差
            if (bestInliers.Count >= 3)
            {
                Vector3 centroid = Vector3.Zero;
                foreach (var idx in bestInliers) centroid += points[idx];
                centroid /= bestInliers.Count;

                double len = Math.Sqrt(bestA * bestA + bestB * bestB + bestC * bestC);
                bestA /= len; bestB /= len; bestC /= len;
                bestD = -(bestA * centroid.X + bestB * centroid.Y + bestC * centroid.Z);

                bestInliers.Clear();
                for (int j = 0; j < n; j++)
                {
                    Vector3 pt = points[j];
                    double dist = Math.Abs(bestA * pt.X + bestB * pt.Y + bestC * pt.Z + bestD);
                    if (dist < distanceThreshold)
                    {
                        bestInliers.Add(j);
                    }
                }
            }

            return (bestA, bestB, bestC, bestD, bestInliers);
        }

        private static double ComputeLateralOffsetByRansac(List<Vector3> points, int maxIterations, double distanceThreshold)
        {
            var (a, b, c, d, inliers) = RansacPlaneFittingStacker(points, maxIterations, distanceThreshold);
            if (inliers.Count < MinPointCount)
                return double.NaN;

            var inlierXs = new List<double>(inliers.Count);
            foreach (var idx in inliers)
            {
                inlierXs.Add(points[idx].X);
            }
            inlierXs.Sort();

            // 双侧各剔除 2% 的噪点边界，获取鲁棒的宽度边缘
            int idxLeft = (int)(inlierXs.Count * 0.02);
            int idxRight = (int)(inlierXs.Count * 0.98);
            if (idxRight <= idxLeft) return double.NaN;

            double leftX = inlierXs[idxLeft];
            double rightX = inlierXs[idxRight];

            return (leftX + rightX) / 2.0;
        }

        private static DebugData ComputeLateralOffsetDebugByRansac(
            List<Vector3> points, int maxIterations, double distanceThreshold,
            double? xMinRoI = null, double? xMaxRoI = null)
        {
            var debug = new DebugData();
            debug.RoiPointCount = points.Count;

            // 1. 构建点云在 X 方向的深度投影图（与老版保持一致，供 UI 绘制）
            double xMinV = double.MaxValue, xMaxV = double.MinValue;
            foreach (var pt in points)
            {
                if (pt.X < xMinV) xMinV = pt.X;
                if (pt.X > xMaxV) xMaxV = pt.X;
            }
            
            if (xMaxV - xMinV < 50.0)
            {
                debug.ErrorMessage = $"X跨度太小({xMaxV - xMinV:F1}mm)";
                return debug;
            }

            int bc = (int)Math.Ceiling((xMaxV - xMinV) / BinSizeMm) + 1;
            if (bc < 20)
            {
                debug.ErrorMessage = $"bin数不足({bc})";
                return debug;
            }

            var dp = new double[bc];
            var bp = new int[bc];
            var bx = new double[bc];
            for (int i = 0; i < bc; i++)
            {
                dp[i] = double.MaxValue;
                bx[i] = xMinV + i * BinSizeMm + BinSizeMm / 2.0;
            }

            foreach (var pt in points)
            {
                int bin = (int)((pt.X - xMinV) / BinSizeMm);
                if (bin < 0 || bin >= bc) continue;
                if (pt.Z < dp[bin]) dp[bin] = pt.Z;
                bp[bin]++;
            }

            debug.XMin = xMinV;
            debug.XMax = xMaxV;
            debug.BinCount = bc;
            debug.BinXCenters = bx;
            debug.DepthProfile = dp;
            debug.BinPopulation = bp;

            // 2. 执行 RANSAC 平面拟合
            var (a, b, c, dVal, inliers) = RansacPlaneFittingStacker(points, maxIterations, distanceThreshold);
            if (inliers.Count < MinPointCount)
            {
                debug.ErrorMessage = $"RANSAC 拟合失败：有效内点数不足({inliers.Count} < {MinPointCount})";
                return debug;
            }

            // 3. 计算立柱表面的有效 X/Z 边界
            var inlierXs = new List<double>(inliers.Count);
            var inlierZs = new List<double>(inliers.Count);
            foreach (var idx in inliers)
            {
                inlierXs.Add(points[idx].X);
                inlierZs.Add(points[idx].Z);
            }
            inlierXs.Sort();

            int idxLeft = (int)(inlierXs.Count * 0.02);
            int idxRight = (int)(inlierXs.Count * 0.98);
            if (idxRight <= idxLeft)
            {
                debug.ErrorMessage = "内点跨度不足";
                return debug;
            }

            double leftX = inlierXs[idxLeft];
            double rightX = inlierXs[idxRight];
            double ransacCenterX = (leftX + rightX) / 2.0;

            // 4. 填充 Debug 可视化数据结构，与老算法保持无缝兼容
            debug.Success = true;
            debug.GapCenterX = ransacCenterX;
            debug.CurrentGapCenterX = ransacCenterX;
            debug.GapWidthMm = rightX - leftX;
            debug.LateralOffsetMm = ransacCenterX;

            // 梁阈值线（绘制在内点 Z 平面偏移阈值处，便于直观预览）
            double avgZ = inlierZs.Average();
            debug.BeamZThreshold = avgZ + distanceThreshold;
            
            // 背景深度（为直观起见取 ROI 最大深度）
            double maxZ = points.Max(p => (double)p.Z);
            debug.BackgroundZ = maxZ;

            // 用单个 BeamRegion 承载拟合出的立柱，以供 UI 填充橘色高亮显示
            debug.BeamRegions = new List<(double leftX, double rightX, double centerX, double width)>
            {
                (leftX, rightX, ransacCenterX, rightX - leftX)
            };

            return debug;
        }
    }
}
