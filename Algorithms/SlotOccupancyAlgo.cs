using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using material_box_storage_detection_system_Net.Config;
using material_box_storage_detection_system_Net.Devices;

namespace material_box_storage_detection_system_Net.Algorithms
{
    /// <summary>
    /// 货位占用检测算法类 (Flag 1)。
    /// </summary>
    public static class SlotOccupancyAlgo
    {
        /// <summary>
        /// 执行货位占用检测。
        /// 单帧策略：检测单帧图像的有效点云数量或有效像素。
        /// </summary>
        public static bool Run(string side, object img1, Models.DetectionResult res)
        {
            try
            {
                var cfg = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
                int threshold = cfg?.PointThreshold ?? 10000;
                
                var roiSource = (side?.ToLower() == "right") ? cfg?.Roi3dRight : cfg?.Roi3dLeft;
                var roi3d = ResolveRoi3d(roiSource);

                var f1 = EvaluateSingleFrame(img1, threshold, roi3d);
                
                if (!f1.Valid)
                {
                    Console.WriteLine("❌ [Flag1] 单帧采集图像失败，按无货返回。");
                    res.SlotOccupied = false;
                    return true;
                }
                res.SlotOccupied = f1.Occupied;
                Console.WriteLine($"[Flag1] 单帧判定: occupied={f1.Occupied}, count={f1.Count}, threshold={threshold}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SlotOccupancyAlgo 执行失败: {ex.Message}");
                res.SlotOccupied = false;
                return false;
            }
        }

        private static (bool Valid, bool Occupied, int Count) EvaluateSingleFrame(object frame, int threshold, (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) roi)
        {
            if (frame is DepthFrameData depthFrame && depthFrame.DepthRaw != null && depthFrame.DepthRaw.Length > 0)
            {
                // ★ 方案A：使用 DepthFrameData.GetPointCloud()，自动使用 SDK 内参或默认值
                int count = CountPointsInRoi(depthFrame, roi);
                return (true, count > threshold, count);
            }

            if (frame is Bitmap bmp && IsValidBitmapFallback(bmp))
            {
                int count = CountEffectivePixelsFallback(bmp);
                return (true, count > threshold, count);
            }

            return (false, false, 0);
        }

        private static int CountPointsInRoi(DepthFrameData frame, (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) roi)
        {
            if (frame.Width <= 0 || frame.Height <= 0) return 0;

            // ★ 使用统一接口获取点云（自动使用已缓存的内参，无内参时回退默认值）
            var points = frame.GetPointCloud();
            int count = 0;

            foreach (var pt in points)
            {
                if (pt.X >= roi.MinX && pt.X <= roi.MaxX &&
                    pt.Y >= roi.MinY && pt.Y <= roi.MaxY &&
                    pt.Z >= roi.MinZ && pt.Z <= roi.MaxZ)
                {
                    count++;
                }
            }

            return count;
        }

        private static (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) ResolveRoi3d(List<int>? roi)
        {
            if (roi != null && roi.Count >= 6)
            {
                return (roi[0], roi[1], roi[2], roi[3], roi[4], roi[5]);
            }

            return (-500, 500, -500, 500, 1000, 3000);
        }

        private static bool IsValidBitmapFallback(Bitmap bmp)
        {
            if (bmp.Width == 640 && bmp.Height == 480)
            {
                var c = bmp.GetPixel(5, 5);
                if (c.R < 100 && c.G < 60 && c.B < 60)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountEffectivePixelsFallback(Bitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if ((c.R + c.G + c.B) > 45)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}

