using System;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 横梁与立柱变形检测算法类 (Flag 3)。
    /// </summary>
    public static class RackDeformationAlgo
    {
        /// <summary>
        /// 执行货架本体安全性评价 (立柱变形量、托臂下垂角度)。
        /// 单相机视角，提取左右立柱及托臂点云进行拟合计算。
        /// </summary>
        /// <param name="img">深度帧数据。</param>
        /// <param name="res">结果载体。</param>
        /// <returns>算法是否执行成功。</returns>
        public static bool Run(object img, Models.DetectionResult res)
        {
            try
            {
                var frame = img as Devices.DepthFrameData;
                var basePoints = StackerOffsetAlgo.GetBasePointsFromFrame(frame);
                if (basePoints == null || basePoints.Count < 500)
                {
                    res.RackDefMmLeftValue = 0; res.RackDefMmRightValue = 0;
                    res.ArmDefAngleLeftValue = 0; res.ArmDefAngleRightValue = 0;
                    return false;
                }

                var cfg = Config.ConfigManager.Instance?.Algorithms?.RackDeformation;
                var camParam = cfg?.FindCameraParam(frame?.CameraSn);

                double zMin = camParam?.ZMin ?? cfg?.DepthMin ?? 1000;
                double zMax = camParam?.ZMax ?? cfg?.DepthMax ?? 3000;
                double? xMinRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMin : null;
                double? xMaxRoI = (camParam != null && camParam.XMax > camParam.XMin) ? camParam.XMax : null;
                double? yMinRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMin : null;
                double? yMaxRoI = (camParam != null && camParam.YMax > camParam.YMin) ? camParam.YMax : null;

                double yMin = yMinRoI ?? -800;
                double yMax = yMaxRoI ?? 800;

                var segRes = CloudSegmentationHelper.Segment(
                    basePoints, zMin, zMax, yMin, yMax, xMinRoI, xMaxRoI,
                    5.0, 3, 500, extractComponentClouds: true);

                if (!segRes.Success)
                {
                    res.RackDefMmLeftValue = 0; res.RackDefMmRightValue = 0;
                    res.ArmDefAngleLeftValue = 0; res.ArmDefAngleRightValue = 0;
                    return false;
                }

                double rawRackL = ComputeColumnDeformation(segRes.LeftColumnPoints);
                double rawRackR = ComputeColumnDeformation(segRes.RightColumnPoints);
                double rawArmL  = ComputeArmAngle(segRes.LeftArmPoints);
                double rawArmR  = ComputeArmAngle(segRes.RightArmPoints);

                // 减去标准基准值（与堆垛机偏移的 ReferenceX 机制一致）
                double refRackL = camParam?.RefRackDefLeft  ?? 0.0;
                double refRackR = camParam?.RefRackDefRight ?? 0.0;
                double refArmL  = camParam?.RefArmAngleLeft ?? 0.0;
                double refArmR  = camParam?.RefArmAngleRight ?? 0.0;

                res.RackDefMmLeftValue    = Math.Round(rawRackL - refRackL, 2);
                res.RackDefMmRightValue   = Math.Round(rawRackR - refRackR, 2);
                res.ArmDefAngleLeftValue  = Math.Round(rawArmL  - refArmL, 2);
                res.ArmDefAngleRightValue = Math.Round(rawArmR  - refArmR, 2);

                return true;
            }
            catch (Exception)
            {
                res.RackDefMmLeftValue = 0; res.RackDefMmRightValue = 0;
                res.ArmDefAngleLeftValue = 0; res.ArmDefAngleRightValue = 0;
                return false;
            }
        }

        /// <summary>
        /// 计算立柱变形量：投影到 X-Y 平面，直线拟合后找最大 X 偏差。
        /// </summary>
        public static double ComputeColumnDeformation(System.Collections.Generic.List<System.Numerics.Vector3> pts)
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
        /// 计算托臂下垂角度：提取下表面（更平滑），Y-X 平面拟合。
        /// </summary>
        /// <param name="pts">托臂点云。</param>
        /// <param name="usedPoints">可选：输出实际用于计算的过滤后点云，供可视化。</param>
        public static double ComputeArmAngle(System.Collections.Generic.List<System.Numerics.Vector3> pts,
            System.Collections.Generic.List<System.Numerics.Vector3>? usedPoints = null)
        {
            if (pts == null || pts.Count < 50) return 0.0;

            // ---- 第一步：过滤螺栓（按 X 方向连续性）----
            // 托臂沿 X 轴延伸，长度远大于螺栓。
            // 按 X 分 bin，找到点密度最高的连续区段作为托臂主体。
            double xMin = double.MaxValue, xMax = double.MinValue;
            foreach (var p in pts)
            {
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
            }
            double xRange = xMax - xMin;
            if (xRange < 20) return 0.0; // X 跨度不足

            double xBinSize = 5.0;
            int xBinCount = (int)Math.Ceiling(xRange / xBinSize) + 1;
            var xBinPop = new int[xBinCount];
            foreach (var p in pts)
            {
                int b = (int)((p.X - xMin) / xBinSize);
                if (b >= 0 && b < xBinCount) xBinPop[b]++;
            }

            // 找最长的连续密集 X 区间（点数 > 中位数 * 0.3 的连续 bin 段）
            int medianPop = xBinPop.Where(c => c > 0).OrderBy(c => c).ElementAtOrDefault(xBinPop.Count(c => c > 0) / 2);
            int densityThresh = Math.Max(2, (int)(medianPop * 0.3));

            int bestStart = 0, bestLen = 0, curStart = 0, curLen = 0;
            for (int i = 0; i < xBinCount; i++)
            {
                if (xBinPop[i] >= densityThresh)
                {
                    if (curLen == 0) curStart = i;
                    curLen++;
                    if (curLen > bestLen) { bestStart = curStart; bestLen = curLen; }
                }
                else { curLen = 0; }
            }

            if (bestLen < 3) return 0.0; // 没有足够的连续段

            // 只保留主体区间内的点
            double armXMin = xMin + bestStart * xBinSize;
            double armXMax = xMin + (bestStart + bestLen) * xBinSize;
            var filtered = new System.Collections.Generic.List<System.Numerics.Vector3>();
            foreach (var p in pts)
            {
                if (p.X >= armXMin && p.X <= armXMax) filtered.Add(p);
            }
            if (filtered.Count < 30) return 0.0;

            // ---- 第二步：Y 方向 IQR 过滤（去除上下方螺栓残留）----
            var yValues = new double[filtered.Count];
            for (int i = 0; i < filtered.Count; i++) yValues[i] = filtered[i].Y;
            Array.Sort(yValues);
            double yMedian = yValues[yValues.Length / 2];
            double yQ1 = yValues[yValues.Length / 4];
            double yQ3 = yValues[yValues.Length * 3 / 4];
            double yIqr = yQ3 - yQ1;
            double boltMargin = Math.Max(yIqr * 2.5, 15.0);

            var clean = new System.Collections.Generic.List<System.Numerics.Vector3>();
            foreach (var p in filtered)
            {
                if (p.Y >= yMedian - boltMargin && p.Y <= yMedian + boltMargin) clean.Add(p);
            }
            if (clean.Count < 30) clean = filtered;

            // 输出用于计算的过滤后点云（供可视化标记）
            if (usedPoints != null)
            {
                usedPoints.Clear();
                usedPoints.AddRange(clean);
            }

            // ---- 第三步：沿 X 轴切片提取下表面（每个 X 区间的 10th 百分位 Y）----
            // 下表面比上表面平滑得多，噪声和螺栓影响小
            double cXMin = double.MaxValue, cXMax = double.MinValue;
            foreach (var p in clean)
            {
                if (p.X < cXMin) cXMin = p.X;
                if (p.X > cXMax) cXMax = p.X;
            }

            // 最小 X 跨度检查：跨度太小无法可靠拟合
            if (cXMax - cXMin < 30.0) return 0.0;

            double xStep = 10.0;  // 较大的 bin 减少噪声
            int bins = (int)Math.Ceiling((cXMax - cXMin) / xStep) + 1;
            var binYValues = new System.Collections.Generic.List<double>[bins];
            for (int i = 0; i < bins; i++) binYValues[i] = new System.Collections.Generic.List<double>();

            foreach (var p in clean)
            {
                int bin = (int)((p.X - cXMin) / xStep);
                if (bin >= 0 && bin < bins)
                    binYValues[bin].Add(p.Y);
            }

            // 对每个 bin 取 10th 百分位 Y（下表面，比上表面稳定得多）
            int minBinPts = Math.Max(3, clean.Count / (bins * 2));
            var rawSurface = new System.Collections.Generic.List<(double x, double y)>();
            for (int i = 0; i < bins; i++)
            {
                if (binYValues[i].Count >= minBinPts)
                {
                    binYValues[i].Sort();
                    int p10Idx = (int)(binYValues[i].Count * 0.10);
                    p10Idx = Math.Clamp(p10Idx, 0, binYValues[i].Count - 1);
                    rawSurface.Add((cXMin + i * xStep + xStep / 2, binYValues[i][p10Idx]));
                }
            }
            if (rawSurface.Count < 3) return 0.0;

            // 3 点滑动平均平滑（消除逐 bin 噪声）
            var surfacePts = new System.Collections.Generic.List<(double x, double y)>();
            for (int i = 0; i < rawSurface.Count; i++)
            {
                int lo = Math.Max(0, i - 1), hi = Math.Min(rawSurface.Count - 1, i + 1);
                double avgY = 0; int cnt = 0;
                for (int j = lo; j <= hi; j++) { avgY += rawSurface[j].y; cnt++; }
                surfacePts.Add((rawSurface[i].x, avgY / cnt));
            }
            if (surfacePts.Count < 3) return 0.0;

            // ---- 第四步：鲁棒拟合 Y = k * X + b ----
            double k0 = FitSlopeYX(surfacePts);
            double b0 = surfacePts.Average(p => p.y) - k0 * surfacePts.Average(p => p.x);
            var residuals = surfacePts.Select(p => Math.Abs(p.y - (k0 * p.x + b0))).ToList();
            double meanRes = residuals.Average();
            double stdRes = Math.Sqrt(residuals.Average(r => (r - meanRes) * (r - meanRes)));
            double resThresh = Math.Max(meanRes + 2.0 * stdRes, 3.0);

            var inliers = new System.Collections.Generic.List<(double x, double y)>();
            for (int i = 0; i < surfacePts.Count; i++)
            {
                if (residuals[i] <= resThresh) inliers.Add(surfacePts[i]);
            }
            if (inliers.Count < 5) inliers = surfacePts;

            double kFinal = FitSlopeYX(inliers);

            // angle in degrees
            double angle = Math.Atan(kFinal) * 180.0 / Math.PI;
            return Math.Round(Math.Abs(angle), 2);
        }

        /// <summary>
        /// 最小二乘法拟合 Y = k * X + b 并返回斜率 k。
        /// </summary>
        private static double FitSlopeYX(System.Collections.Generic.List<(double x, double y)> pts)
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

