using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 货位占用检测算法类 (Flag 1)。
    /// </summary>
    public static class SlotOccupancyAlgo
    {
        /// <summary>
        /// 执行货位占用检测。
        /// 双帧策略：检测两台相机各自的有效点云数量或有效像素，且均满足条件。
        /// </summary>
        public static bool Run(Models.TaskData task, object img1, object img2, Models.DetectionResult res)
        {
            try
            {
                var cfg = ConfigManager.Instance?.Algorithms?.SlotOccupancy;
                string side = task.Side;
                int threshold = cfg?.PointThreshold ?? 10000;
                
                var sns = ConfigManager.GetTargetCameraSNs(1, side);
                string sn1 = sns != null && sns.Count > 0 ? sns[0] : "";
                string sn2 = sns != null && sns.Count > 1 ? sns[1] : sn1;

                var roi1 = ResolveRoi3d(cfg, sn1, task);
                var roi2 = ResolveRoi3d(cfg, sn2, task);

                var f1 = EvaluateSingleFrame(img1, threshold, roi1);
                var f2 = EvaluateSingleFrame(img2, threshold, roi2);
                
                if (!f1.Valid || !f2.Valid)
                {
                    Console.WriteLine("❌ [Flag1] 存在采集图像失败，按无货返回。");
                    res.SlotOccupied = false;
                    return true;
                }
                res.SlotOccupied = f1.Occupied && f2.Occupied;
                Console.WriteLine($"[Flag1] 双相判定: occupied={res.SlotOccupied}, c1={f1.Count}, c2={f2.Count}, threshold={threshold}");
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

        private static (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) ResolveRoi3d(SlotOccupancyConfig? cfg, string sn, Models.TaskData task)
        {
            var p = cfg?.FindCameraParam(sn, task.BeamLength);
            if (p != null)
            {
                return (p.XMin, p.XMax, p.YMin, p.YMax, p.ZMin, p.ZMax);
            }
            else
            {
                var roiList = task.Side == "right" ? cfg?.Roi3dRight : cfg?.Roi3dLeft;
                if (roiList != null && roiList.Count >= 6)
                {
                    return (roiList[0], roiList[1], roiList[2], roiList[3], roiList[4], roiList[5]);
                }
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

