using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 横梁与立柱弯曲度检测算法类 (Flag 3)。
    /// 分相机独立分割策略：
    ///   frame1(LeftSideSns[0]) → 左立柱点云 + 左半横梁点云
    ///   frame2(LeftSideSns[1]) → 右立柱点云 + 右半横梁点云
    /// 横梁下挠在合并两侧 BeamPoints 后统一计算，从根本上消除两台相机
    /// 外参标定对齐误差对分割结果的影响。
    /// 立柱弯曲 = X-Y投影直线拟合的最大X偏差(mm)；横梁弯曲 = X-Y投影直线拟合的最大Y挠度(mm)。
    /// </summary>
    public static class RackDeformationAlgo
    {
        /// <summary>
        /// 执行货架本体安全性评价 (立柱弯曲量、横梁下挠量)。
        /// 采用分相机独立分割策略：各相机各自完成 ROI 过滤 + 场景分割，
        /// 再按角色汇总（frame1→左立柱，frame2→右立柱，两侧 BeamPoints 合并→横梁）。
        /// 任一相机分割失败时对应结果置 0 并降级处理，不阻断整体检测。
        /// </summary>
        public static bool Run(object img1, object img2, Models.DetectionResult res)
        {
            try
            {
                var cfg = Config.ConfigManager.Instance?.Algorithms?.RackDeformation;
                var frame1 = img1 as Devices.DepthFrameData;
                var frame2 = img2 as Devices.DepthFrameData;

                // 分相机独立分割，互不干扰
                var seg1 = SegmentSingleCamera(frame1, cfg);
                var seg2 = SegmentSingleCamera(frame2, cfg);

                if (seg1 == null && seg2 == null)
                {
                    res.RackDefMmLeftValue = 0; res.RackDefMmRightValue = 0;
                    res.BeamDefMmValue = 0;
                    return false;
                }

                // 立柱弯曲：frame1(LeftSideSns[0])→左立柱，frame2(LeftSideSns[1])→右立柱
                double rawRackL = ComputeColumnDeformation(seg1?.LeftColumnPoints);
                double rawRackR = ComputeColumnDeformation(seg2?.RightColumnPoints);

                // 横梁：合并两侧 BeamPoints 后统一计算下挠量
                var beamPts = new List<Vector3>();
                if (seg1?.BeamPoints != null) beamPts.AddRange(seg1.BeamPoints);
                if (seg2?.BeamPoints != null) beamPts.AddRange(seg2.BeamPoints);
                double rawBeam = ComputeBeamDeformation(beamPts.Count >= 50 ? beamPts : null);

                // 减去标准基准值（按 frame1 SN 查配置，兼容原有存储方式）
                var camParam = cfg?.FindCameraParam(frame1?.CameraSn ?? frame2?.CameraSn);
                double refRackL = camParam?.RefRackDefLeft ?? 0.0;
                double refRackR = camParam?.RefRackDefRight ?? 0.0;
                double refBeam  = camParam?.RefBeamDef ?? 0.0;

                res.RackDefMmLeftValue  = Math.Round(rawRackL - refRackL, 2);
                res.RackDefMmRightValue = Math.Round(rawRackR - refRackR, 2);
                res.BeamDefMmValue      = Math.Round(rawBeam  - refBeam,  2);

                return true;
            }
            catch (Exception)
            {
                res.RackDefMmLeftValue = 0; res.RackDefMmRightValue = 0;
                res.BeamDefMmValue = 0;
                return false;
            }
        }

        /// <summary>
        /// 对单台相机点云执行完整的 ROI 过滤 + 场景分割，返回分割结果（含立柱/横梁点云）。
        /// ROI 参数从该相机的独立配置 (<see cref="Config.CameraRoiParam"/>) 读取，
        /// 与另一台相机完全解耦，互不影响。失败或点云不足时返回 null。
        /// </summary>
        public static SegmentationResult? SegmentSingleCamera(
            Devices.DepthFrameData? frame, Config.RackDeformationConfig? cfg)
        {
            if (frame == null) return null;

            var pts = StackerOffsetAlgo.GetBasePointsFromFrame(frame);
            if (pts == null || pts.Count < 500) return null;

            var camParam = cfg?.FindCameraParam(frame.CameraSn);

            // 如果配置了双 ROI (立柱或横梁 ROI)，直接按范围提取点云，无需做 X 轴深度剖面自适应分割
            if (camParam != null && (camParam.ColXMax > camParam.ColXMin || camParam.BeamXMax > camParam.BeamXMin))
            {
                var colPts = new List<Vector3>();
                var beamPts = new List<Vector3>();

                // 立柱 ROI 提取范围及 Fallback
                double cxMin = camParam.ColXMax > camParam.ColXMin ? camParam.ColXMin : (camParam.XMax > camParam.XMin ? camParam.XMin : -1000);
                double cxMax = camParam.ColXMax > camParam.ColXMin ? camParam.ColXMax : (camParam.XMax > camParam.XMin ? camParam.XMax : 1000);
                double cyMin = camParam.ColXMax > camParam.ColXMin ? camParam.ColYMin : (camParam.YMax > camParam.YMin ? camParam.YMin : -800);
                double cyMax = camParam.ColXMax > camParam.ColXMin ? camParam.ColYMax : (camParam.YMax > camParam.YMin ? camParam.YMax : 800);
                double czMin = camParam.ColXMax > camParam.ColXMin ? camParam.ColZMin : (camParam.ZMax > camParam.ZMin ? camParam.ZMin : 1000);
                double czMax = camParam.ColXMax > camParam.ColXMin ? camParam.ColZMax : (camParam.ZMax > camParam.ZMin ? camParam.ZMax : 3000);

                // 横梁 ROI 提取范围及 Fallback
                double bxMin = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamXMin : (camParam.XMax > camParam.XMin ? camParam.XMin : -1000);
                double bxMax = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamXMax : (camParam.XMax > camParam.XMin ? camParam.XMax : 1000);
                double byMin = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamYMin : (camParam.YMax > camParam.YMin ? camParam.YMin : -800);
                double byMax = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamYMax : (camParam.YMax > camParam.YMin ? camParam.YMax : 800);
                double bzMin = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamZMin : (camParam.ZMax > camParam.ZMin ? camParam.ZMin : 1000);
                double bzMax = camParam.BeamXMax > camParam.BeamXMin ? camParam.BeamZMax : (camParam.ZMax > camParam.ZMin ? camParam.ZMax : 3000);

                foreach (var pt in pts)
                {
                    if (pt.X >= cxMin && pt.X <= cxMax && pt.Y >= cyMin && pt.Y <= cyMax && pt.Z >= czMin && pt.Z <= czMax)
                    {
                        colPts.Add(pt);
                    }
                    if (pt.X >= bxMin && pt.X <= bxMax && pt.Y >= byMin && pt.Y <= byMax && pt.Z >= bzMin && pt.Z <= bzMax)
                    {
                        beamPts.Add(pt);
                    }
                }

                return new SegmentationResult
                {
                    Success = true,
                    LeftColumnPoints = colPts,
                    RightColumnPoints = colPts,
                    BeamPoints = beamPts,
                    RoiPoints = pts,
                    XMin = Math.Min(cxMin, bxMin),
                    XMax = Math.Max(cxMax, bxMax)
                };
            }

            // Fallback：原单 ROI + 自适应边缘分割算法
            double zMin  = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
            double zMax  = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
            double? xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : (double?)null;
            double? xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : (double?)null;
            double yLo = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : -800.0;
            double yHi = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax :  800.0;

            var seg = CloudSegmentationHelper.Segment(
                pts, zMin, zMax, yLo, yHi, xMinRoI, xMaxRoI,
                5.0, 3, 500, extractComponentClouds: true);

            return seg.Success ? seg : null;
        }

        public static List<Vector3>? GetFilteredPoints(Devices.DepthFrameData frame, Config.RackDeformationConfig? cfg)
        {
            var pts = StackerOffsetAlgo.GetBasePointsFromFrame(frame);
            if (pts == null) return null;

            var camParam = cfg?.FindCameraParam(frame.CameraSn);
            double zMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
            double zMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
            double? xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : null;
            double? xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : null;
            double? yMinRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : null;
            double? yMaxRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax : null;

            double yLo = yMinRoI ?? -800;
            double yHi = yMaxRoI ?? 800;

            var filtered = new List<Vector3>(pts.Count);
            foreach (var p in pts)
            {
                if (p.Z >= zMin && p.Z <= zMax && p.Y >= yLo && p.Y <= yHi &&
                    (!xMinRoI.HasValue || p.X >= xMinRoI.Value) && (!xMaxRoI.HasValue || p.X <= xMaxRoI.Value))
                {
                    filtered.Add(p);
                }
            }
            return filtered;
        }

        /// <summary>
        /// 计算立柱弯曲量：投影到 X-Y 平面，直线拟合后找最大 X 偏差 (mm)。
        /// </summary>
        public static double ComputeColumnDeformation(List<Vector3>? pts)
        {
            if (pts == null || pts.Count < 50) return 0.0;

            // X = k * Y + b
            double sumY = 0, sumY2 = 0, sumX = 0, sumXY = 0;
            foreach (var p in pts)
            {
                sumY += p.Y;
                sumY2 += p.Y * p.Y;
                sumX += p.X;
                sumXY += p.X * p.Y;
            }

            int n = pts.Count;
            double denominator = n * sumY2 - sumY * sumY;
            if (Math.Abs(denominator) < 1e-6) return 0.0;

            double k = (n * sumXY - sumX * sumY) / denominator;
            double b = (sumX * sumY2 - sumY * sumXY) / denominator;

            double maxDev = 0;
            foreach (var p in pts)
            {
                double idealX = k * p.Y + b;
                double dev = Math.Abs(p.X - idealX);
                if (dev > maxDev) maxDev = dev;
            }

            return Math.Round(maxDev, 2);
        }

        /// <summary>
        /// 计算横梁下挠量：对横梁点云沿X轴分bin，取每bin的Y下表面（10th百分位），
        /// 鲁棒拟合直线 Y=kX+b，返回各采样点与拟合直线的最大 Y 偏差 (mm)。
        /// </summary>
        /// <param name="pts">横梁点云（两立柱内侧边缘之间、特定Y高度的前景点）。</param>
        /// <param name="usedPoints">可选：输出实际用于计算的过滤后的剖面采样点，供可视化。</param>
        public static double ComputeBeamDeformation(List<Vector3>? pts,
            List<Vector3>? usedPoints = null)
        {
            if (pts == null || pts.Count < 50) return 0.0;

            // ---- 第一步：沿 X 轴分 bin，每 bin 取 Y 下表面（10th 百分位，更稳定）----
            double xMin = double.MaxValue, xMax = double.MinValue;
            foreach (var p in pts) { if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X; }
            double xSpan = xMax - xMin;
            if (xSpan < 30.0) return 0.0;

            double xStep = 10.0;
            int bins = (int)Math.Ceiling(xSpan / xStep) + 1;
            var binYVals = new List<double>[bins];
            for (int i = 0; i < bins; i++) binYVals[i] = new List<double>();

            foreach (var p in pts)
            {
                int bin = (int)((p.X - xMin) / xStep);
                if (bin >= 0 && bin < bins)
                    binYVals[bin].Add(p.Y);
            }

            // 每 bin 取 10th 百分位 Y（横梁下表面）
            int minBinPts = Math.Max(3, pts.Count / (bins * 2));
            var rawProfile = new List<(double x, double y)>();
            for (int i = 0; i < bins; i++)
            {
                if (binYVals[i].Count >= minBinPts)
                {
                    binYVals[i].Sort();
                    int p10Idx = (int)(binYVals[i].Count * 0.10);
                    p10Idx = Math.Clamp(p10Idx, 0, binYVals[i].Count - 1);
                    rawProfile.Add((xMin + i * xStep + xStep / 2, binYVals[i][p10Idx]));
                }
            }
            if (rawProfile.Count < 5) return 0.0;

            // 3点滑动平均平滑
            var profile = new List<(double x, double y)>();
            for (int i = 0; i < rawProfile.Count; i++)
            {
                int lo = Math.Max(0, i - 1), hi = Math.Min(rawProfile.Count - 1, i + 1);
                double avgY = 0; int cnt = 0;
                for (int j = lo; j <= hi; j++) { avgY += rawProfile[j].y; cnt++; }
                profile.Add((rawProfile[i].x, avgY / cnt));
            }
            if (profile.Count < 5) return 0.0;

            // ---- 第二步：鲁棒拟合 Y = k * X + b ----
            double k0 = FitSlope(profile);
            double b0 = profile.Average(p => p.y) - k0 * profile.Average(p => p.x);
            var residuals = profile.Select(p => Math.Abs(p.y - (k0 * p.x + b0))).ToList();
            double meanRes = residuals.Average();
            double stdRes = Math.Sqrt(residuals.Average(r => (r - meanRes) * (r - meanRes)));
            double resThresh = Math.Max(meanRes + 2.0 * stdRes, 3.0);

            var inliers = new List<(double x, double y)>();
            for (int i = 0; i < profile.Count; i++)
            {
                if (residuals[i] <= resThresh) inliers.Add(profile[i]);
            }
            if (inliers.Count < 5) inliers = profile;

            double kFinal = FitSlope(inliers);
            double bFinal = inliers.Average(p => p.y) - kFinal * inliers.Average(p => p.x);

            // 输出用于计算的剖面点（供可视化）
            if (usedPoints != null)
            {
                usedPoints.Clear();
                foreach (var (x, y) in inliers)
                    usedPoints.Add(new Vector3((float)x, (float)y, 0));
            }

            // ---- 第三步：计算最大 Y 偏差（挠度 mm）----
            double maxDev = 0;
            foreach (var (x, y) in inliers)
            {
                double idealY = kFinal * x + bFinal;
                double dev = Math.Abs(y - idealY);
                if (dev > maxDev) maxDev = dev;
            }

            return Math.Round(maxDev, 2);
        }

        /// <summary>
        /// 最小二乘法拟合 Y = k * X + b 并返回斜率 k。
        /// </summary>
        private static double FitSlope(List<(double x, double y)> pts)
        {
            double sumX = 0, sumX2 = 0, sumY = 0, sumXY = 0;
            foreach (var p in pts)
            {
                sumX += p.x;
                sumX2 += p.x * p.x;
                sumY += p.y;
                sumXY += p.x * p.y;
            }

            int n = pts.Count;
            double denominator = n * sumX2 - sumX * sumX;
            if (Math.Abs(denominator) < 1e-6) return 0.0;

            return (n * sumXY - sumX * sumY) / denominator;
        }
    }
}
