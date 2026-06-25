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
            BarcodeFormat.EAN_13,
            BarcodeFormat.ITF,
            BarcodeFormat.UPC_A,
            BarcodeFormat.CODE_93
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
            var results = new HashSet<string>();
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

                void TryDecode(System.Drawing.Bitmap bmp)
                {
                    var decodeResults = reader.DecodeMultiple(bmp);
                    if (decodeResults != null && decodeResults.Length > 0)
                    {
                        foreach (var r in decodeResults)
                        {
                            if (!string.IsNullOrWhiteSpace(r.Text))
                                results.Add(r.Text.Trim());
                        }
                    }
                    else
                    {
                        // 退管：有时候 DecodeMultiple 识别不到但 Decode 单个条码能识别到
                        var singleResult = reader.Decode(bmp);
                        if (singleResult != null && !string.IsNullOrWhiteSpace(singleResult.Text))
                        {
                            results.Add(singleResult.Text.Trim());
                        }
                    }
                }

                // 1. 原始图像解码
                TryDecode(bitmap);

                // 2. 直方图均衡化增强
                using (var enhanced = EnhanceForBarcode(bitmap))
                {
                    TryDecode(enhanced);
                }

                // 3. 对比度增强
                using (var brightened = AdjustContrast(bitmap, 1.5f))
                {
                    TryDecode(brightened);
                }

                // 4. 图像锐化增强
                using (var sharpened = SharpenImage(bitmap))
                {
                    TryDecode(sharpened);
                }

                // 5. 如果图像分辨率过大，缩小一半尝试
                if (bitmap.Width > 1500 || bitmap.Height > 1500)
                {
                    using (var resized = new System.Drawing.Bitmap(bitmap, new Size(bitmap.Width / 2, bitmap.Height / 2)))
                    {
                        TryDecode(resized);
                    }
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"⚠️ 相机#{cameraIndex} 条码解码异常: {ex.Message}");
            }
            return results.ToList();
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

        private static System.Drawing.Bitmap AdjustContrast(System.Drawing.Bitmap src, float contrast)
        {
            var result = new System.Drawing.Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            using (var attrs = new ImageAttributes())
            {
                float t = (1.0f - contrast) / 2.0f;
                float[][] ptsArray = {
                    new float[] {contrast, 0, 0, 0, 0},
                    new float[] {0, contrast, 0, 0, 0},
                    new float[] {0, 0, contrast, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {t, t, t, 0, 1}
                };
                attrs.SetColorMatrix(new ColorMatrix(ptsArray), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(src, new Rectangle(0, 0, result.Width, result.Height),
                    0, 0, src.Width, src.Height,
                    GraphicsUnit.Pixel, attrs);
            }
            return result;
        }

        private static System.Drawing.Bitmap SharpenImage(System.Drawing.Bitmap src)
        {
            var result = new System.Drawing.Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            var srcRect = new Rectangle(0, 0, src.Width, src.Height);
            var dstRect = new Rectangle(0, 0, result.Width, result.Height);
            var srcData = src.LockBits(srcRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(dstRect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                int stride = srcData.Stride;
                int width = src.Width;
                int height = src.Height;

                unsafe
                {
                    byte* srcPtr = (byte*)srcData.Scan0;
                    byte* dstPtr = (byte*)dstData.Scan0;

                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = srcPtr + y * stride;
                        byte* dstRow = dstPtr + y * stride;

                        if (y == 0 || y == height - 1)
                        {
                            for (int x = 0; x < width * 3; x++)
                                dstRow[x] = srcRow[x];
                            continue;
                        }

                        byte* srcRowPrev = srcPtr + (y - 1) * stride;
                        byte* srcRowNext = srcPtr + (y + 1) * stride;

                        for (int x = 0; x < width; x++)
                        {
                            if (x == 0 || x == width - 1)
                            {
                                dstRow[x * 3] = srcRow[x * 3];
                                dstRow[x * 3 + 1] = srcRow[x * 3 + 1];
                                dstRow[x * 3 + 2] = srcRow[x * 3 + 2];
                                continue;
                            }

                            for (int c = 0; c < 3; c++)
                            {
                                int offset = x * 3 + c;
                                int val = 5 * srcRow[offset]
                                        - srcRowPrev[offset]
                                        - srcRowNext[offset]
                                        - srcRow[offset - 3]
                                        - srcRow[offset + 3];

                                dstRow[offset] = (byte)(val > 255 ? 255 : (val < 0 ? 0 : val));
                            }
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

