using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 新增的 2D 图像特征堆垛机偏移检测算法类 (由原 3D 算法重构分离而来)。
    /// 只使用图漾 3D 相机采集的彩色 2D 图像进行特征提取，拟合立柱内侧孔洞列，计算偏差。
    /// </summary>
    public static class StackerOffset2DAlgo
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "config_2d_offset_test.json");

        /// <summary>
        /// 执行堆垛机 2D 偏移计算。
        /// </summary>
        public static bool Run(TaskData task, object img1, object img2, DetectionResult res)
        {
            try
            {
                var frame1 = img1 as DepthFrameData;
                var frame2 = img2 as DepthFrameData;

                // 载入 2D 偏移的全局配置文件
                OffsetTestConfig? globalCfg = null;
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        string json = File.ReadAllText(ConfigPath);
                        globalCfg = JsonSerializer.Deserialize<OffsetTestConfig>(json);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[StackerOffset2DAlgo] 警告: 载入配置文件失败: {ex.Message}");
                }

                globalCfg ??= new OffsetTestConfig();
                globalCfg.MigrateAll();
                var offsets = new List<double>();

                // 处理第一台相机
                if (frame1 != null)
                {
                    double? offsetMm = ProcessSingleFrame(task, frame1, globalCfg);
                    if (offsetMm.HasValue && !double.IsNaN(offsetMm.Value))
                    {
                        offsets.Add(offsetMm.Value);
                    }
                }

                // 处理第二台相机
                if (frame2 != null)
                {
                    double? offsetMm = ProcessSingleFrame(task, frame2, globalCfg);
                    if (offsetMm.HasValue && !double.IsNaN(offsetMm.Value))
                    {
                        offsets.Add(offsetMm.Value);
                    }
                }

                if (offsets.Count == 0)
                {
                    Console.WriteLine("[StackerOffset2DAlgo] 错误: 双侧相机均未能提取到有效拟合线段进行偏移计算。");
                    res.OffsetLatMmValue = 0;
                    return false;
                }

                // 均值作为最终偏差
                double finalOffset = offsets.Average();

                // 遵循最终偏移输出正负号的物理对应关系（均以往里侧走为正值）：
                // 
                // 1. 对于 right 一侧（面向右侧货架）：
                //    - 最终计算结果为正值（+），表示堆垛机偏右侧，需要往左侧移动（即相机的 X 轴负方向）。
                //    - 堆垛机偏右侧时，静止柱子在相机画面中偏左（actualCenterX < refX，即原始偏差 dx < 0）。
                //    - 既然原始偏差 dx < 0（负值）对应“需要向左移动（正值）”，因此 right 侧必须进行取反。
                //
                if (string.Equals(res.Side, "right", StringComparison.OrdinalIgnoreCase))
                {
                    finalOffset = -finalOffset;
                }
                //
                // 2. 对于 left 一侧（面向左侧货架）：
                //    - 相机 X+ 方向（偏右）为向里，对应的计算偏移量为正值，也是堆垛机需要移动的正方向。
                //    - 堆垛机偏左/偏外侧时，静止柱子在相机画面中偏右（actualCenterX > refX，即原始偏差 dx > 0）。
                //    - 既然原始偏差 dx > 0（正值）对应“向里移动（正值）”，因此 left 侧保持同号，无需取反。
                //


                if (finalOffset < -3.0) finalOffset = -3.0;
                if (finalOffset > 3.0) finalOffset = 3.0;

                res.OffsetLatMmValue = Math.Round(finalOffset, 1);
                Console.WriteLine($"[StackerOffset2DAlgo] 计算完成：最终水平偏移 = {res.OffsetLatMmValue} mm (由 {offsets.Count} 台相机均值计算得到)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 异常崩溃: {ex.Message}");
                res.OffsetLatMmValue = 0;
                return false;
            }
        }

        private static double? ProcessSingleFrame(TaskData task, DepthFrameData frame, OffsetTestConfig config)
        {
            string sn = frame.CameraSn;
            if (string.IsNullOrEmpty(sn)) return null;

            // 获取该相机的参数，若不存在则跳过该相机
            if (!config.Cameras.TryGetValue(sn, out var param))
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 相机 {sn} 未配置参数，跳过偏移计算。");
                return null;
            }

            // 根据 BeamLength 选取 ROI 区域与标准参考值
            var roiPointsToUse = (task.BeamLength == 1380) ? param.RoiPoints1380 : param.RoiPoints2180;
            var referenceOffsetXToUse = (task.BeamLength == 1380) ? param.ReferenceOffsetX1380 : param.ReferenceOffsetX2180;

            if (roiPointsToUse == null || roiPointsToUse.Count != 4)
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 相机 {sn} 未配置货架规格 {task.BeamLength} 的多边形 ROI 区域，跳过偏移计算。");
                return null;
            }

            // 获取 2D 彩色图像（优先使用真实 RGB，无则降级使用 pseudo-depth）
            Bitmap srcBmp = frame.ColorImage ?? frame.PreviewImage;
            if (srcBmp == null) return null;

            // 克隆一份用来进行图像处理并最终回绘 PreviewImage
            var processBmp = (Bitmap)srcBmp.Clone();
            using var mat = processBmp.ToMat();
            using var gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            // 1. 高斯平滑
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

            // 2. 自适应阈值化
            using var binary = new Mat();
            int blockSize = param.BlockSize;
            if (blockSize % 2 == 0) blockSize++;
            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, blockSize, param.CValue);

            // 3. 开运算去噪
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);

            // 4. 轮廓提取与几何筛选
            Cv2.FindContours(binary, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var detectedRounds = new List<RotatedRect>();
            var detectedSlots = new List<RotatedRect>();
            var allCandidates = new List<RotatedRect>();

            var roiPts = roiPointsToUse.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();

            foreach (var contour in contours)
            {
                if (contour.Length < 5) continue;

                double area = Cv2.ContourArea(contour);
                if (area < param.MinArea || area > param.MaxArea) continue;

                var ellipse = Cv2.FitEllipse(contour);

                // 多边形 ROI 过滤
                var pt = new OpenCvSharp.Point2f(ellipse.Center.X, ellipse.Center.Y);
                if (Cv2.PointPolygonTest(roiPts, pt, false) < 0) continue;

                double width = Math.Min(ellipse.Size.Width, ellipse.Size.Height);
                double height = Math.Max(ellipse.Size.Width, ellipse.Size.Height);
                double aspectRatio = width / height;

                var hull = Cv2.ConvexHull(contour);
                double hullArea = Cv2.ContourArea(hull);
                double solidity = hullArea > 0 ? area / hullArea : 0;

                bool isRound = (aspectRatio >= param.RoundAspectMin && aspectRatio <= 1.0 && solidity >= param.RoundSolidMin);
                bool isSlot = (aspectRatio >= param.SlotAspectMin && aspectRatio <= param.SlotAspectMax && solidity >= param.SlotSolidMin);

                if (isRound) detectedRounds.Add(ellipse);
                else if (isSlot) detectedSlots.Add(ellipse);
            }

            allCandidates.AddRange(detectedRounds);
            allCandidates.AddRange(detectedSlots);

            if (allCandidates.Count < 3)
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 相机 {sn} 在 ROI 区域内提取出的候选点数过少 ({allCandidates.Count} 个)，无法进行 RANSAC 直线拟合。");
                processBmp.Dispose();
                return null;
            }

            // 5. RANSAC 寻找线段
            var remainingCandidates = new List<RotatedRect>(allCandidates);
            var detectedLines = new List<Line2D>();
            var lineInliersList = new List<List<RotatedRect>>();
            
            // 如果设定了标准位置参考值，由于立柱可能存在双列孔洞，我们寻找最多 2 条线，然后选择最接近标准值的一条。
            // 如果未设定标准参考值，则默认沿用原有逻辑寻找最契合的 1 条直线。
            int maxLinesToFind = referenceOffsetXToUse.HasValue ? 2 : 1;
            int minInliers = 3;
            double distanceThresh = 15.0;
            var rng = new Random(42);

            for (int lineIdx = 0; lineIdx < maxLinesToFind; lineIdx++)
            {
                if (remainingCandidates.Count < minInliers) break;

                var tempBestInliers = new List<RotatedRect>();
                for (int iter = 0; iter < 300; iter++)
                {
                    int i1 = rng.Next(remainingCandidates.Count);
                    int i2 = rng.Next(remainingCandidates.Count);
                    if (i1 == i2) continue;

                    var p1 = remainingCandidates[i1].Center;
                    var p2 = remainingCandidates[i2].Center;

                    double dy = p2.Y - p1.Y;
                    if (Math.Abs(dy) < 10.0) continue;

                    double k = (p2.X - p1.X) / dy;
                    if (Math.Abs(k) > 0.35) continue; // 偏角过大

                    double c = p1.X - k * p1.Y;

                    var inliers = new List<RotatedRect>();
                    foreach (var e in remainingCandidates)
                    {
                        double expectedX = k * e.Center.Y + c;
                        if (Math.Abs(e.Center.X - expectedX) < distanceThresh)
                        {
                            inliers.Add(e);
                        }
                    }

                    if (inliers.Count > tempBestInliers.Count)
                    {
                        tempBestInliers = inliers;
                    }
                }

                if (tempBestInliers.Count >= minInliers)
                {
                    var linePts = tempBestInliers.Select(e => new Point2f(e.Center.X, e.Center.Y)).ToArray();
                    var fittedLine = Cv2.FitLine(linePts, DistanceTypes.L2, 0, 0.01, 0.01);
                    detectedLines.Add(fittedLine);
                    lineInliersList.Add(tempBestInliers);

                    foreach (var inlier in tempBestInliers)
                    {
                        remainingCandidates.Remove(inlier);
                    }
                }
                else
                {
                    break;
                }
            }

            if (detectedLines.Count == 0)
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 相机 {sn} 未能通过 RANSAC 提取出符合对齐条件的直线。");
                processBmp.Dispose();
                return null;
            }

            // 选择最契合标准值（或最多内点）的一条拟合直线
            float midY = mat.Height / 2f;
            Line2D selectedLine;
            List<RotatedRect> selectedInliers;
            float actualCenterX;

            if (referenceOffsetXToUse.HasValue)
            {
                float refX = referenceOffsetXToUse.Value;
                var lineInfos = new List<(Line2D line, double xAtMid, List<RotatedRect> inliers)>();
                for (int i = 0; i < detectedLines.Count; i++)
                {
                    var line = detectedLines[i];
                    double k = line.Vx / line.Vy;
                    double c = line.X1 - k * line.Y1;
                    double xAtMid = k * midY + c;
                    lineInfos.Add((line, xAtMid, lineInliersList[i]));
                }
                // 按与参考值的偏差绝对值升序排序，选择最接近参考值的那一条
                lineInfos = lineInfos.OrderBy(item => Math.Abs(item.xAtMid - refX)).ToList();

                selectedLine = lineInfos[0].line;
                actualCenterX = (float)lineInfos[0].xAtMid;
                selectedInliers = lineInfos[0].inliers;

                // 若最接近的直线的偏差依然过大（例如超过 70 像素），则输出警告日志
                double dev = Math.Abs(actualCenterX - refX);
                if (dev > 70.0)
                {
                    Console.WriteLine($"[StackerOffset2DAlgo] 警告: 相机 {sn} 检测到的最佳孔洞列中心 {actualCenterX:F1} px 距离标准参考值 {refX:F1} px 偏差过大 ({dev:F1} px)，可能是由于错列检测或堆垛机严重偏离。");
                }
            }
            else
            {
                selectedLine = detectedLines[0];
                selectedInliers = lineInliersList[0];
                double k = selectedLine.Vx / selectedLine.Vy;
                double c = selectedLine.X1 - k * selectedLine.Y1;
                actualCenterX = (float)(k * midY + c);
            }

            double finalK = selectedLine.Vx / selectedLine.Vy;
            double finalC = selectedLine.X1 - finalK * selectedLine.Y1;
            var bestInliers = selectedInliers;

            // 6. 计算偏差（像素与物理毫米）
            // 标准位置参考值
            float refXVal = referenceOffsetXToUse ?? (mat.Width / 2f);

            double offsetPixel = actualCenterX - refXVal;
            double ratio = param.PixelToMmRatio > 0 ? param.PixelToMmRatio : 0.125;
            double offsetMm = offsetPixel * ratio;

            // 统一最终显示与物理输出方向规则：对于 right 一侧进行取反
            double displayOffsetMm = offsetMm;
            if (string.Equals(task.Side, "right", StringComparison.OrdinalIgnoreCase))
            {
                displayOffsetMm = -offsetMm;
            }

            // 7. 可视化回绘到 PreviewImage
            try
            {
                // 绘制背景噪点（灰色圈）
                foreach (var e in allCandidates)
                {
                    if (!bestInliers.Contains(e))
                    {
                        Cv2.Ellipse(mat, e, Scalar.Gray, 1);
                    }
                }

                // 绘制拟合内点（红色圈）
                foreach (var e in bestInliers)
                {
                    Cv2.Ellipse(mat, e, Scalar.Red, 3);
                }

                // 绘制局部红线 (限制在内点高度范围内)
                double minPointsY = bestInliers.Min(e => (double)e.Center.Y);
                double maxPointsY = bestInliers.Max(e => (double)e.Center.Y);
                minPointsY = Math.Max(0, minPointsY - 15);
                maxPointsY = Math.Min(mat.Height, maxPointsY + 15);

                Point2f pt1 = new Point2f((float)(finalK * minPointsY + finalC), (float)minPointsY);
                Point2f pt2 = new Point2f((float)(finalK * maxPointsY + finalC), (float)maxPointsY);
                Cv2.Line(mat, new OpenCvSharp.Point((int)pt1.X, (int)pt1.Y), new OpenCvSharp.Point((int)pt2.X, (int)pt2.Y), Scalar.Red, 3, LineTypes.AntiAlias);

                // 转换为 Bitmap 绘制中文字符和多边形 ROI 区域
                using var drawBmp = mat.ToBitmap();
                using (var g = Graphics.FromImage(drawBmp))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    // 绘制多边形 ROI
                    using (var roiPen = new Pen(Color.FromArgb(0, 255, 255), 2f))
                    using (var roiBrush = new SolidBrush(Color.FromArgb(30, 0, 255, 255)))
                    {
                        var screenPts = roiPointsToUse.ToArray();
                        g.DrawPolygon(roiPen, screenPts);
                        g.FillPolygon(roiBrush, screenPts);
                    }

                    // 绘制文字遮罩和信息
                    using (var titleFont = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold))
                    using (var descFont = new Font("Microsoft YaHei UI", 12F))
                      using (var bgBrush = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                    {
                        string refText = referenceOffsetXToUse.HasValue ? $"标准:{refXVal:F1}px" : "基于画面中心";
                        string infoText = $"偏差: {displayOffsetMm:F2} mm ({refText})";
                        string descText = $"[2D算法] 相机 SN: {sn} | 找到孔洞数: {bestInliers.Count}个 | 线中心: {actualCenterX:F1}px (比例: {ratio:F4} mm/px)";

                        g.FillRectangle(bgBrush, 30, 20, 1100, 95);
                        g.DrawString(infoText, titleFont, Brushes.Cyan, 40, 23);
                        g.DrawString(descText, descFont, Brushes.White, 40, 72);
                    }
                }

                // 替换 PreviewImage
                frame.PreviewImage?.Dispose();
                frame.PreviewImage = (Bitmap)drawBmp.Clone();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StackerOffset2DAlgo] 绘图发生异常: {ex.Message}");
            }
            finally
            {
                processBmp.Dispose();
            }

            return offsetMm;
        }
    }
}
