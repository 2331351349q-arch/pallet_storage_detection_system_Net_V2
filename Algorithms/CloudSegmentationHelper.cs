using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace material_box_storage_detection_system_Net.Algorithms
{
    /// <summary>
    /// 点云分割工具结果模型。
    /// 包含中间分割步骤的所有数据（供可视化）及最终提取的局部点云。
    /// </summary>
    public class SegmentationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public List<Vector3> RoiPoints { get; set; } = new();

        public double XMin { get; set; }
        public double XMax { get; set; }
        public int BinCount { get; set; }
        public double[] BinXCenters { get; set; } = Array.Empty<double>();
        public double[] DepthProfile { get; set; } = Array.Empty<double>();
        public int[] BinPopulation { get; set; } = Array.Empty<int>();

        public double BackgroundZ { get; set; }
        public double BeamZThreshold { get; set; }

        public List<(double leftX, double rightX, double centerX, double width)> BeamRegions { get; set; } = new();
        public List<(double leftX, double rightX, double centerX, double width)> AllGaps { get; set; } = new();

        public double GapCenterX { get; set; } = double.NaN;
        public double GapWidthMm { get; set; }

        public double RefinedLeftBeamInnerX { get; set; } = double.NaN;
        public double RefinedRightBeamInnerX { get; set; } = double.NaN;
        public double RefinedGapCenterX { get; set; } = double.NaN;

        public double LeftBracketWidthMm { get; set; } = 0;
        public double RightBracketWidthMm { get; set; } = 0;

        // 变形检测专属点云（仅在 extractComponentClouds = true 时填充）
        public List<Vector3> LeftColumnPoints { get; set; } = new();
        public List<Vector3> RightColumnPoints { get; set; } = new();
        public List<Vector3> LeftArmPoints { get; set; } = new();
        public List<Vector3> RightArmPoints { get; set; } = new();
    }

    /// <summary>
    /// 将点云进行场景分割（寻找立柱、托臂等）的公共算法逻辑，供 Flag 2 和 Flag 3 复用。
    /// </summary>
    public static class CloudSegmentationHelper
    {
        public static SegmentationResult Segment(
            List<Vector3> basePoints,
            double zMin, double zMax,
            double yMinRoI, double yMaxRoI,
            double? xMinRoI = null, double? xMaxRoI = null,
            double binSizeMm = 5.0,
            int minBeamBinWidth = 3,
            int minPointCount = 500,
            bool extractComponentClouds = false)
        {
            var res = new SegmentationResult();

            // 1. ROI 滤波
            var roi = new List<Vector3>();
            foreach (var pt in basePoints)
            {
                if (pt.Y >= yMinRoI && pt.Y <= yMaxRoI && pt.Z >= zMin && pt.Z <= zMax)
                {
                    if (xMinRoI.HasValue && pt.X < xMinRoI.Value) continue;
                    if (xMaxRoI.HasValue && pt.X > xMaxRoI.Value) continue;
                    roi.Add(pt);
                }
            }
            res.RoiPoints = roi;
            if (roi.Count < minPointCount) { res.ErrorMessage = $"ROI点数不足({roi.Count}<{minPointCount})"; return res; }

            // 2. X轴深度投影
            double xMinV = double.MaxValue, xMaxV = double.MinValue;
            foreach (var pt in roi) { if (pt.X < xMinV) xMinV = pt.X; if (pt.X > xMaxV) xMaxV = pt.X; }
            if (xMaxV - xMinV < 50.0) { res.ErrorMessage = $"X跨度太小({xMaxV - xMinV:F1}mm)"; return res; }

            int bc = (int)Math.Ceiling((xMaxV - xMinV) / binSizeMm) + 1;
            if (bc < 20) { res.ErrorMessage = $"bin数不足({bc})"; return res; }

            var dp = new double[bc]; var bp = new int[bc]; var bx = new double[bc];
            for (int i = 0; i < bc; i++) { dp[i] = double.MaxValue; bx[i] = xMinV + i * binSizeMm + binSizeMm / 2.0; }
            foreach (var pt in roi)
            {
                int bin = (int)((pt.X - xMinV) / binSizeMm);
                if (bin < 0 || bin >= bc) continue;
                if (pt.Z < dp[bin]) dp[bin] = pt.Z;
                bp[bin]++;
            }
            res.XMin = xMinV; res.XMax = xMaxV; res.BinCount = bc;
            res.BinXCenters = bx; res.DepthProfile = dp; res.BinPopulation = bp;

            // 3. 梁/背景分割 (自适应阈值)
            var vz = new List<double>();
            for (int i = 0; i < bc; i++) if (dp[i] < double.MaxValue * 0.5 && bp[i] > 0) vz.Add(dp[i]);
            if (vz.Count < 20) { res.ErrorMessage = $"有效Z bin不足({vz.Count})"; return res; }
            vz.Sort();
            
            double bgZ = vz[(int)(vz.Count * 0.95)];
            double frontZ = vz[(int)(vz.Count * 0.05)];
            if (bgZ - frontZ < 15.0) { res.ErrorMessage = $"深度落差太小({bgZ - frontZ:F1}mm<15mm)，未找到明显开口"; return res; }
            
            double bThresh = frontZ + (bgZ - frontZ) * 0.6;
            res.BackgroundZ = bgZ; res.BeamZThreshold = bThresh;

            // 4. 梁边缘检测
            var br = new List<(double l, double r, double c, double w)>();
            int? bs = null; double xEnd = xMinV + bc * binSizeMm;
            for (int i = 0; i < bc; i++)
            {
                bool isB = dp[i] < bThresh && bp[i] > 0;
                if (isB && bs == null) { bs = i; }
                else if (!isB && bs != null)
                {
                    int s = bs.Value, e = i - 1;
                    if (e - s + 1 >= minBeamBinWidth)
                    { double lx = xMinV + s * binSizeMm, rx = xMinV + e * binSizeMm; br.Add((lx, rx, (lx + rx) / 2, rx - lx)); }
                    bs = null;
                }
            }
            if (bs != null && (bc - 1 - bs.Value + 1) >= minBeamBinWidth)
            { int s = bs.Value; double lx = xMinV + s * binSizeMm; br.Add((lx, xEnd, (lx + xEnd) / 2, xEnd - lx)); }
            res.BeamRegions = br;
            if (br.Count < 2) { res.ErrorMessage = $"梁不足({br.Count}<2)"; return res; }

            // 5. 识别开口
            double bestC = double.NaN, bestW = 0;
            int bestLeftBeamIdx = -1, bestRightBeamIdx = -1;
            var gaps = new List<(double l, double r, double c, double w)>();
            for (int i = 0; i < br.Count - 1; i++)
            {
                double gl = br[i].r, gr = br[i + 1].l, gw = gr - gl, gc = (gl + gr) / 2;
                gaps.Add((gl, gr, gc, gw));
                if (gw > bestW) { bestW = gw; bestC = gc; bestLeftBeamIdx = i; bestRightBeamIdx = i + 1; }
            }
            res.AllGaps = gaps;
            if (double.IsNaN(bestC)) { res.ErrorMessage = "未找到有效开口"; return res; }
            res.GapWidthMm = bestW;

            // 5b. 托臂内侧边缘精化
            if (bestLeftBeamIdx >= 0 && bestRightBeamIdx >= 0)
            {
                double lRawRight = br[bestLeftBeamIdx].r;
                double rRawLeft  = br[bestRightBeamIdx].l;

                var (refinedLX, refinedRX) = ComputeRobustColumnInnerEdges(roi, lRawRight, rRawLeft, bThresh);

                res.RefinedLeftBeamInnerX  = refinedLX;
                res.RefinedRightBeamInnerX = refinedRX;
                res.RefinedGapCenterX      = (refinedLX + refinedRX) / 2.0;
                res.LeftBracketWidthMm     = Math.Max(0, lRawRight - refinedLX);
                res.RightBracketWidthMm    = Math.Max(0, refinedRX - rRawLeft);

                res.GapCenterX = res.RefinedGapCenterX;

                // 6. 分离独立点云 (仅在需要时)
                if (extractComponentClouds)
                {
                    double lRawLeft = br[bestLeftBeamIdx].l;
                    double rRawRight = br[bestRightBeamIdx].r;

                    foreach (var pt in roi)
                    {
                        if (pt.Z >= bThresh) continue; // 仅处理前景(梁)部分

                        // 属于左梁区域
                        if (pt.X >= lRawLeft && pt.X <= lRawRight)
                        {
                            // 判断是否超出纯立柱的内侧边缘 (即属于托臂突出部分)
                            // 注意左侧立柱内侧边缘在右边，所以 pt.X > refinedLX 即为突出部分
                            if (pt.X > refinedLX) res.LeftArmPoints.Add(pt);
                            else res.LeftColumnPoints.Add(pt);
                        }
                        // 属于右梁区域
                        else if (pt.X >= rRawLeft && pt.X <= rRawRight)
                        {
                            // 右侧立柱内侧边缘在左边，所以 pt.X < refinedRX 即为突出部分
                            if (pt.X < refinedRX) res.RightArmPoints.Add(pt);
                            else res.RightColumnPoints.Add(pt);
                        }
                    }
                }
            }
            else
            {
                res.GapCenterX = bestC;
            }

            res.Success = true;
            return res;
        }

        private static (double leftInnerX, double rightInnerX) ComputeRobustColumnInnerEdges(
            List<Vector3> roi, double approxLeftBeamRight, double approxRightBeamLeft, double beamZThreshold)
        {
            const double YSliceMm = 15.0;
            const double SearchMm = 150.0;
            const double EdgePercentile = 0.50;

            double leftXMin  = approxLeftBeamRight  - SearchMm;
            double leftXMax  = approxLeftBeamRight  + SearchMm * 0.4;
            double rightXMin = approxRightBeamLeft  - SearchMm * 0.4;
            double rightXMax = approxRightBeamLeft  + SearchMm;

            var leftEdgeByY  = new Dictionary<int, double>();
            var rightEdgeByY = new Dictionary<int, double>();

            foreach (var pt in roi)
            {
                if (pt.Z >= beamZThreshold) continue;
                int yBin = (int)Math.Floor(pt.Y / YSliceMm);

                if (pt.X >= leftXMin && pt.X <= leftXMax)
                {
                    if (!leftEdgeByY.TryGetValue(yBin, out double cur) || pt.X > cur)
                        leftEdgeByY[yBin] = pt.X;
                }

                if (pt.X >= rightXMin && pt.X <= rightXMax)
                {
                    if (!rightEdgeByY.TryGetValue(yBin, out double cur) || pt.X < cur)
                        rightEdgeByY[yBin] = pt.X;
                }
            }

            double leftX  = approxLeftBeamRight;
            double rightX = approxRightBeamLeft;

            if (leftEdgeByY.Count >= 3)
            {
                var sortedL = leftEdgeByY.Values.OrderBy(x => x).ToList();
                int idxL = Math.Clamp((int)Math.Floor(sortedL.Count * EdgePercentile), 0, sortedL.Count - 1);
                int winL = Math.Max(1, sortedL.Count / 10);
                leftX = sortedL.Skip(Math.Max(0, idxL - winL)).Take(winL * 2 + 1).Average();
            }

            if (rightEdgeByY.Count >= 3)
            {
                var sortedR = rightEdgeByY.Values.OrderByDescending(x => x).ToList();
                int idxR = Math.Clamp((int)Math.Floor(sortedR.Count * EdgePercentile), 0, sortedR.Count - 1);
                int winR = Math.Max(1, sortedR.Count / 10);
                rightX = sortedR.Skip(Math.Max(0, idxR - winR)).Take(winR * 2 + 1).Average();
            }

            return (leftX, rightX);
        }
    }
}
