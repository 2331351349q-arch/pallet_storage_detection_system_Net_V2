using System;

namespace material_box_storage_detection_system_Net.Algorithms
{
    /// <summary>
    /// 视觉盘库算法类 (Flag 4 和 5)。
    /// 使用 2D 相机拍摄料箱图像进行条码扫描识别。
    /// 当前版本使用模拟条码数据进行流程验证。
    /// </summary>
    public static class VisualInventoryAlgo
    {
        /// <summary>
        /// 模拟盘库条码列表，用于在真实 2D 相机解码算法就绪前进行流程验证。
        /// </summary>
        private static readonly string[] MockBarcodes = new[]
        {
            "BOX-A001", "BOX-A002", "BOX-A003",
            "BOX-B001", "BOX-B002", "BOX-C001"
        };

        /// <summary>
        /// 执行盘库任务。每侧使用 2 台 2D 相机同时拍摄料箱图像。
        /// </summary>
        /// <param name="flag">4 为启动扫码，5 为停止扫码。</param>
        /// <param name="img1">第 1 路 2D 相机抓取的图像（Bitmap）。</param>
        /// <param name="img2">第 2 路 2D 相机抓取的图像（Bitmap）。</param>
        /// <param name="res">结果载体。</param>
        /// <param name="onLog">可选日志回调。</param>
        /// <returns>算法是否执行成功。</returns>
        public static bool Run(int flag, object img1, object img2, Models.DetectionResult res, Action<string>? onLog = null)
        {
            var startTime = DateTime.Now;

            int imageCount = 0;
            if (img1 is System.Drawing.Bitmap bmp1)
            {
                onLog?.Invoke($"📷 [盘库] 2D 相机#1 图像 ({bmp1.Width}x{bmp1.Height})");
                imageCount++;
            }
            if (img2 is System.Drawing.Bitmap bmp2)
            {
                onLog?.Invoke($"📷 [盘库] 2D 相机#2 图像 ({bmp2.Width}x{bmp2.Height})");
                imageCount++;
            }
            onLog?.Invoke($"📷 [盘库] 共收到 {imageCount} 路 2D 图像，执行模拟扫码...");

            if (flag == 4)
            {
                // 启动扫码：返回模拟条码（表示正在扫描中）
                res.ResultBarcodes = "[]";
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                onLog?.Invoke($"⏱️  [盘库启动(Flag4)] 双路 2D 模拟扫码已就绪，耗时: {elapsed:F2}ms");
            }
            else if (flag == 5)
            {
                // 停止扫码：返回模拟条码结果
                res.ResultBarcodes = System.Text.Json.JsonSerializer.Serialize(MockBarcodes);
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

                if (onLog != null)
                {
                    onLog($"📋 [读码结果] 共扫描到 {MockBarcodes.Length} 个模拟条码。");
                    onLog($"🧾 [扫码明细] {string.Join(", ", MockBarcodes)}");
                    onLog($"⏱️  [盘库结束(Flag5)] 双路 2D 扫码耗时: {elapsed:F2}ms");
                }
            }

            return true;
        }
    }
}
