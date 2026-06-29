using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using HalconDotNet;
using pallet_storage_detection_system_Net_V2.Models;
using pallet_storage_detection_system_Net_V2.Config;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 视觉盘库算法类 (Flag 4 和 5)。
    /// 升级版：使用 MVTec HALCON 引擎进行工业级超强二维码/条码识别。
    /// </summary>
    public static class VisualInventoryAlgo
    {
        public static bool Run(int flag, object img1, object img2, List<string> targetSNs, DetectionResult res, Action<string>? onLog = null)
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
                string sn = targetSNs != null && i < targetSNs.Count ? targetSNs[i] : "";
                var barcodes = DecodeBarcodesHalcon(bitmaps[i], sn, onLog, i + 1);
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

        private static List<string> DecodeBarcodesHalcon(System.Drawing.Bitmap bitmap, string sn, Action<string>? onLog, int cameraIndex)
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

                // 2. ROI 区域裁剪加速 (极其关键：减少 90% 运算量)
                if (!string.IsNullOrEmpty(sn) && ConfigManager.Instance?.Camera2DCalibrations != null)
                {
                    var calib = ConfigManager.Instance.Camera2DCalibrations.FirstOrDefault(c => c.CameraSn == sn);
                    if (calib != null && calib.RoiInventory != null && calib.RoiInventory.Count == 8)
                    {
                        try
                        {
                            HTuple rows = new HTuple(new double[] { calib.RoiInventory[1], calib.RoiInventory[3], calib.RoiInventory[5], calib.RoiInventory[7] });
                            HTuple cols = new HTuple(new double[] { calib.RoiInventory[0], calib.RoiInventory[2], calib.RoiInventory[4], calib.RoiInventory[6] });
                            HOperatorSet.GenRegionPolygonFilled(out HObject ho_Region, rows, cols);
                            HOperatorSet.ReduceDomain(ho_Image, ho_Region, out HObject ho_ImageReduced);
                            ho_Image.Dispose();
                            ho_Image = ho_ImageReduced;
                            ho_Region.Dispose();
                        }
                        catch (Exception ex)
                        {
                            onLog?.Invoke($"⚠️ [{sn}] ROI 裁剪异常，回退至全局搜索: {ex.Message}");
                        }
                    }
                }

                // 3. 创建 DataCode/BarCode 句柄
                HOperatorSet.CreateDataCode2dModel("Data Matrix ECC 200", "default_parameters", "maximum_recognition", out hv_DataCodeHandle_ECC200);
                HOperatorSet.CreateDataCode2dModel("QR Code", "default_parameters", "maximum_recognition", out hv_DataCodeHandle_QR);
                HOperatorSet.CreateBarCodeModel(new HTuple(), new HTuple(), out hv_BarCodeHandle);

                // 强制要求适应任何极性（黑底白码、白底黑码）、镜像反转等极端情况
                HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle_ECC200, "polarity", "any");
                HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle_ECC200, "mirrored", "any");
                
                HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle_QR, "polarity", "any");
                HOperatorSet.SetDataCode2dParam(hv_DataCodeHandle_QR, "mirrored", "any");

                // 3. 寻找 DataMatrix
                HObject ho_SymbolXLDs_ECC200 = null;
                HTuple hv_ResultHandles_ECC200, hv_DecodedDataStrings_ECC200;
                HOperatorSet.FindDataCode2d(ho_Image, out ho_SymbolXLDs_ECC200, hv_DataCodeHandle_ECC200, "stop_after_result_num", 1, out hv_ResultHandles_ECC200, out hv_DecodedDataStrings_ECC200);
                if (ho_SymbolXLDs_ECC200 != null) ho_SymbolXLDs_ECC200.Dispose();
                if (hv_DecodedDataStrings_ECC200 != null && hv_DecodedDataStrings_ECC200.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_ECC200.SArr;
                    foreach (var s in strings)
                    {
                        var t = s?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(t) && t.Length > 4) 
                            results.Add(t);
                    }
                }

                // 4. 寻找 QR Code
                HObject ho_SymbolXLDs_QR = null;
                HTuple hv_ResultHandles_QR, hv_DecodedDataStrings_QR;
                HOperatorSet.FindDataCode2d(ho_Image, out ho_SymbolXLDs_QR, hv_DataCodeHandle_QR, "stop_after_result_num", 1, out hv_ResultHandles_QR, out hv_DecodedDataStrings_QR);
                if (ho_SymbolXLDs_QR != null) ho_SymbolXLDs_QR.Dispose();
                if (hv_DecodedDataStrings_QR != null && hv_DecodedDataStrings_QR.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_QR.SArr;
                    foreach (var s in strings)
                    {
                        var t = s?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(t) && t.Length > 4) 
                            results.Add(t);
                    }
                }

                // 5. 寻找 1D 条形码 ("auto" 支持识别大多数常见条码如 Code 128, Code 39, EAN 等)
                HObject ho_SymbolRegions_BarCode = null;
                HTuple hv_DecodedDataStrings_BarCode;
                HOperatorSet.FindBarCode(ho_Image, out ho_SymbolRegions_BarCode, hv_BarCodeHandle, "auto", out hv_DecodedDataStrings_BarCode);
                if (ho_SymbolRegions_BarCode != null) ho_SymbolRegions_BarCode.Dispose();
                if (hv_DecodedDataStrings_BarCode != null && hv_DecodedDataStrings_BarCode.Length > 0)
                {
                    var strings = hv_DecodedDataStrings_BarCode.SArr;
                    foreach (var s in strings)
                    {
                        var t = s?.Trim() ?? "";
                        // 过滤掉长度小于等于4的短字符串，以防 Halcon 将背景噪声误识别为无效短码（例如 3798, 3538）
                        if (!string.IsNullOrWhiteSpace(t) && t.Length > 4) 
                            results.Add(t);
                    }
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
