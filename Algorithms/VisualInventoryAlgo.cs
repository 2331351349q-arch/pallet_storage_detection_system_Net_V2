using System;

namespace material_box_storage_detection_system_Net.Algorithms
{
    /// <summary>
    /// 视觉盘库算法类 (Flag 4 和 5)。
    /// </summary>
    public static class VisualInventoryAlgo
    {
        public static event Action<System.Drawing.Bitmap>? OnCodeReaderImage;

        /// <summary>
        /// 执行盘库任务。
        /// </summary>
        /// <param name="flag">4 为开始，5 为停止。</param>
        /// <param name="img1">预留参数（读码器流程可为空）。</param>
        /// <param name="img2">预留参数（读码器流程可为空）。</param>
        /// <param name="res">结果载体。</param>
        /// <returns>算法是否执行成功。</returns>
        public static bool Run(int flag, object img1, object img2, Models.DetectionResult res, Action<string>? onLog = null)
        {
            if (flag == 4)
            {
                var startTime = DateTime.Now;
                if (!Devices.CodeReaderService.StartScan(bmp => OnCodeReaderImage?.Invoke(bmp), onLog))
                {
                    return false;
                }
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                onLog?.Invoke($"⏱️  [盘库启动(Flag4)] 总耗时: {elapsed:F2}ms");
                res.ResultBarcodes = "[]";
            }
            else if (flag == 5)
            {
                var startTime = DateTime.Now;
                var codes = Devices.CodeReaderService.StopScan();
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                
                res.ResultBarcodes = System.Text.Json.JsonSerializer.Serialize(codes);

                if (onLog != null)
                {
                    if (codes.Count == 0)
                    {
                        onLog("📭 [读码结果] 未扫描到条码。");
                    }
                    else
                    {
                        onLog($"📋 [读码结果] 共扫描到 {codes.Count} 个唯一条码。");
                        onLog($"🧾 [扫码明细] {string.Join(", ", codes)}");
                    }
                    onLog($"⏱️  [盘库结束(Flag5)] 耗时: {elapsed:F2}ms");
                }
            }
            
            return true;
        }
    }
}

