using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using ZXing;
using ZXing.Common;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 视觉盘库算法类 (Flag 4 和 5)。
    /// 使用 2D 相机拍摄料箱图像进行条码扫描识别。
    /// </summary>
    public static class VisualInventoryAlgo
    {
        private static readonly BarcodeFormat[] SupportedFormats = new[]
        {
            BarcodeFormat.CODE_128,
            BarcodeFormat.CODE_39,
            BarcodeFormat.QR_CODE,
            BarcodeFormat.DATA_MATRIX,
            BarcodeFormat.EAN_13
        };

        public static bool Run(int flag, object img1, object img2, DetectionResult res, Action<string>? onLog = null)
        {
            var startTime = DateTime.Now;
            var bitmaps = new List<System.Drawing.Bitmap>();

            if (img1 is System.Drawing.Bitmap bmp1) bitmaps.Add(bmp1);
            if (img2 is System.Drawing.Bitmap bmp2) bitmaps.Add(bmp2);

            if (bitmaps.Count == 0)
            {
                onLog?.Invoke($"📷 [盘库] 未收到有效的 2D 图像");
                return false;
            }

            int totalDecodeCount = 0;
            var allBarcodes = new HashSet<string>();

            for (int i = 0; i < bitmaps.Count; i++)
            {
                var decodeStart = DateTime.Now;
                var barcodes = DecodeBarcodes(bitmaps[i], onLog, i + 1);
                var decodeMs = (DateTime.Now - decodeStart).TotalMilliseconds;

                foreach (var b in barcodes)
                {
                    if (allBarcodes.Add(b))
                    {
                        onLog?.Invoke($"  - 相机#{i + 1} 扫到: {b} ({decodeMs:F0}ms)");
                    }
                }
                totalDecodeCount += barcodes.Count;

                if (barcodes.Count == 0)
                {
                    onLog?.Invoke($"  - 相机#{i + 1} 未识别 ({decodeMs:F0}ms)");
                }
            }

            // 去重
            var sortedBarcodes = allBarcodes.OrderBy(b => b).ToArray();
            res.ResultBarcodes = System.Text.Json.JsonSerializer.Serialize(sortedBarcodes);

            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            if (flag == 4)
            {
                onLog?.Invoke($"⏱️  [盘库启动(Flag4)] 双路 2D 扫码完成: 共识别 {sortedBarcodes.Length} 个唯一条码（原始解码 {totalDecodeCount} 次），耗时: {elapsed:F2}ms");
            }
            else if (flag == 5)
            {
                if (onLog != null)
                {
                    onLog($"📋 [读码结果] 共扫描到 {sortedBarcodes.Length} 个唯一条码。");
                    if (sortedBarcodes.Length > 0)
                    {
                        onLog($"🧾 [扫码明细] {string.Join(", ", sortedBarcodes)}");
                    }
                    onLog($"⏱️  [盘库结束(Flag5)] 双路 2D 扫码耗时: {elapsed:F2}ms");
                }
            }

            return true;
        }

        private static List<string> DecodeBarcodes(System.Drawing.Bitmap bitmap, Action<string>? onLog, int cameraIndex)
        {
            var results = new List<string>();
            try
            {
                var reader = new ZXing.Windows.Compatibility.BarcodeReader()
                {
                    AutoRotate = true,
                    TryInverted = true,
                    Options = new DecodingOptions
                    {
                        PossibleFormats = new List<BarcodeFormat>(SupportedFormats),
                        TryHarder = true,
                        ReturnCodabarStartEnd = false,
                        PureBarcode = false,
                        CharacterSet = "UTF-8"
                    }
                };

                var decodeResults = reader.DecodeMultiple(bitmap);
                if (decodeResults != null && decodeResults.Length > 0)
                {
                    foreach (var r in decodeResults)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Text))
                            results.Add(r.Text.Trim());
                    }
                    return results;
                }

                using (var enhanced = EnhanceForBarcode(bitmap))
                {
                    decodeResults = reader.DecodeMultiple(enhanced);
                    if (decodeResults != null && decodeResults.Length > 0)
                    {
                        foreach (var r in decodeResults)
                        {
                            if (!string.IsNullOrWhiteSpace(r.Text))
                                results.Add(r.Text.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"⚠️ 相机#{cameraIndex} 条码解码异常: {ex.Message}");
            }
            return results;
        }

        private static System.Drawing.Bitmap EnhanceForBarcode(System.Drawing.Bitmap src)
        {
            var result = new System.Drawing.Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            var srcRect = new Rectangle(0, 0, src.Width, src.Height);
            var dstRect = new Rectangle(0, 0, result.Width, result.Height);
            var srcData = src.LockBits(srcRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(dstRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                int bytesPerPixel = 3;
                int stride = srcData.Stride;
                int totalPixels = src.Width * src.Height;

                var histogram = new int[256];
                unsafe
                {
                    byte* srcPtr = (byte*)srcData.Scan0;
                    for (int y = 0; y < src.Height; y++)
                    {
                        byte* row = srcPtr + y * stride;
                        for (int x = 0; x < src.Width; x++)
                        {
                            int offset = x * bytesPerPixel;
                            byte gray = (byte)((row[offset + 2] * 77 + row[offset + 1] * 150 + row[offset] * 29) >> 8);
                            histogram[gray]++;
                        }
                    }
                }

                var lut = new byte[256];
                int cumulativeSum = 0;
                int minCdf = 0;
                for (int i = 0; i < 256; i++)
                {
                    cumulativeSum += histogram[i];
                    if (cumulativeSum > 0 && minCdf == 0 && histogram[i] > 0)
                        minCdf = cumulativeSum;
                }

                if (totalPixels > minCdf)
                {
                    float scale = 255.0f / (totalPixels - minCdf);
                    cumulativeSum = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        cumulativeSum += histogram[i];
                        int value = (int)((cumulativeSum - minCdf) * scale);
                        lut[i] = (byte)Math.Clamp(value, 0, 255);
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++) lut[i] = (byte)i;
                }

                unsafe
                {
                    byte* srcPtr = (byte*)srcData.Scan0;
                    byte* dstPtr = (byte*)dstData.Scan0;
                    for (int y = 0; y < src.Height; y++)
                    {
                        byte* srcRow = srcPtr + y * stride;
                        byte* dstRow = dstPtr + y * dstData.Stride;
                        for (int x = 0; x < src.Width; x++)
                        {
                            int srcOffset = x * bytesPerPixel;
                            int dstOffset = x * bytesPerPixel;
                            byte gray = (byte)((srcRow[srcOffset + 2] * 77 + srcRow[srcOffset + 1] * 150 + srcRow[srcOffset] * 29) >> 8);
                            byte enhanced = lut[gray];
                            dstRow[dstOffset] = enhanced;
                            dstRow[dstOffset + 1] = enhanced;
                            dstRow[dstOffset + 2] = enhanced;
                        }
                    }
                }
            }
            finally
            {
                src.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }
            return result;
        }
    }
}

