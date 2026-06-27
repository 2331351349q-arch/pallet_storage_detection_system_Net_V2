using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using HalconDotNet;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 视觉盘库算法类 (Flag 4 和 5)。
    /// 升级版：使用 MVTec HALCON 引擎进行工业级超强二维码/条码识别。
    /// </summary>
    public static class VisualInventoryAlgo
    {
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
                var barcodes = DecodeBarcodesHalcon(bitmaps[i], onLog, i + 1);
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
                onLog?.Invoke($"⏱️  [盘库启动(Flag4)] 扫码完成: 共识别 {sortedBarcodes.Length} 个唯一条码，耗时: {elapsed:F2}ms");
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
                    onLog($"⏱️  [盘库结束(Flag5)] 扫码耗时: {elapsed:F2}ms");
                }
            }

            return true;
        }

        private static List<string> DecodeBarcodesHalcon(System.Drawing.Bitmap bitmap, Action<string>? onLog, int cameraIndex)
        {
            var results = new HashSet<string>();
            HObject ho_Image = null;
            HTuple hv_DataCodeHandle_ECC200 = null;
            HTuple hv_DataCodeHandle_QR = null;
            HTuple hv_BarCodeHandle = null;

            try
            {
                // 1. 将 Bitmap 转换为 HALCON HObject (格式要求对应)
                HOperatorSet.GenEmptyObj(out ho_Image);
                var bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    // 使用 GenImageInterleaved 直接将 BGR 字节流转化为 HALCON 对象
                    HOperatorSet.GenImageInterleaved(out ho_Image, bmpData.Scan0, "bgr", bitmap.Width, bitmap.Height, -1, "byte", bitmap.Width, bitmap.Height, 0, 0, -1, 0);
                }
                finally
                {
                    bitmap.UnlockBits(bmpData);
                }

                // 2. 初始化识别模型 (开启 enhanced_recognition 应对严重畸变/反光/模糊)
                HOperatorSet.CreateDataCode2dModel("Data Matrix ECC 200", "default_parameters", "enhanced_recognition", out hv_DataCodeHandle_ECC200);
                HOperatorSet.CreateDataCode2dModel("QR Code", "default_parameters", "enhanced_recognition", out hv_DataCodeHandle_QR);
                HOperatorSet.CreateBarCodeModel(new HTuple(), new HTuple(), out hv_BarCodeHandle);

                // 3. 寻找 DataMatrix
                HObject ho_SymbolXLDs_ECC200 = null;
                HTuple hv_ResultHandles_ECC200, hv_DecodedDataStrings_ECC200;
                HOperatorSet.FindDataCode2d(ho_Image, out ho_SymbolXLDs_ECC200, hv_DataCodeHandle_ECC200, "train", "all", out hv_ResultHandles_ECC200, out hv_DecodedDataStrings_ECC200);
                if (ho_SymbolXLDs_ECC200 != null) ho_SymbolXLDs_ECC200.Dispose();
                if (hv_DecodedDataStrings_ECC200 != null && hv_DecodedDataStrings_ECC200.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_ECC200.SArr;
                    foreach (var s in strings)
                        if (!string.IsNullOrWhiteSpace(s)) results.Add(s.Trim());
                }

                // 4. 寻找 QR Code
                HObject ho_SymbolXLDs_QR = null;
                HTuple hv_ResultHandles_QR, hv_DecodedDataStrings_QR;
                HOperatorSet.FindDataCode2d(ho_Image, out ho_SymbolXLDs_QR, hv_DataCodeHandle_QR, "train", "all", out hv_ResultHandles_QR, out hv_DecodedDataStrings_QR);
                if (ho_SymbolXLDs_QR != null) ho_SymbolXLDs_QR.Dispose();
                if (hv_DecodedDataStrings_QR != null && hv_DecodedDataStrings_QR.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_QR.SArr;
                    foreach (var s in strings)
                        if (!string.IsNullOrWhiteSpace(s)) results.Add(s.Trim());
                }

                // 5. 寻找 1D Barcode (一维码兜底)
                HObject ho_SymbolRegions_1D = null;
                HTuple hv_DecodedDataStrings_1D;
                HOperatorSet.FindBarCode(ho_Image, out ho_SymbolRegions_1D, hv_BarCodeHandle, "auto", out hv_DecodedDataStrings_1D);
                if (ho_SymbolRegions_1D != null) ho_SymbolRegions_1D.Dispose();
                if (hv_DecodedDataStrings_1D != null && hv_DecodedDataStrings_1D.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_1D.SArr;
                    foreach (var s in strings)
                        if (!string.IsNullOrWhiteSpace(s)) results.Add(s.Trim());
                }
            }
            catch (HalconException hex)
            {
                onLog?.Invoke($"⚠️ HALCON 引擎相机#{cameraIndex} 异常: {hex.Message}");
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"⚠️ 相机#{cameraIndex} 解码异常: {ex.Message}");
            }
            finally
            {
                // 必须释放 HALCON 非托管资源，防止内存泄漏
                if (ho_Image != null) ho_Image.Dispose();
                if (hv_DataCodeHandle_ECC200 != null) HOperatorSet.ClearDataCode2dModel(hv_DataCodeHandle_ECC200);
                if (hv_DataCodeHandle_QR != null) HOperatorSet.ClearDataCode2dModel(hv_DataCodeHandle_QR);
                if (hv_BarCodeHandle != null) HOperatorSet.ClearBarCodeModel(hv_BarCodeHandle);
            }

            return results.ToList();
        }
    }
}
