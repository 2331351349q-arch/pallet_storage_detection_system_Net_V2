using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using pallet_storage_detection_system_Net_V2.Config;

namespace pallet_storage_detection_system_Net_V2.Devices
{
    public sealed class DepthFrameData
    {
        public string CameraSn { get; }
        public int Width { get; }
        public int Height { get; }
        public ushort[] DepthRaw { get; }
        public Bitmap PreviewImage { get; }

        /// <summary>
        /// 从 SDK 帧元数据中提取的相机内参（fx, fy, cx, cy），可能为 null。
        /// </summary>
        public (double fx, double fy, double cx, double cy)? Intrinsics { get; set; }

        /// <summary>
        /// 点云数据缓存（相机坐标系，单位 mm），延迟计算。
        /// </summary>
        private List<Vector3>? _pointCloud;

        /// <summary>
        /// SDK 原生转换的点云数据（打包 XYZ，行优先排列），用于替代手动公式计算。
        /// </summary>
        private float[]? _sdkPackedXyz;
        private int _sdkPointCount;

        /// <summary>
        /// 是否已生成点云数据。
        /// </summary>
        public bool HasPointCloud => _pointCloud != null && _pointCloud.Count > 0;

        /// <summary>
        /// 是否使用 SDK 原生点云。
        /// </summary>
        public bool HasSdkPointCloud => _sdkPackedXyz != null && _sdkPackedXyz.Length > 0;

        public DepthFrameData(string cameraSn, int width, int height, ushort[] depthRaw, Bitmap previewImage)
        {
            CameraSn = cameraSn;
            Width = width;
            Height = height;
            DepthRaw = depthRaw;
            PreviewImage = previewImage;
        }

        /// <summary>
        /// 注入 SDK 原生点云数据（来自 ImageProc.DepthImageToPoint3d）。
        /// 数据格式：打包 float[]，每 3 个 float 为一个点 (X, Y, Z)，行优先排列。
        /// </summary>
        public void SetSdkPointCloud(float[] packedXyz, int pointCount)
        {
            _sdkPackedXyz = packedXyz;
            _sdkPointCount = pointCount;
        }

        /// <summary>
        /// 获取或生成点云数据（相机坐标系，mm），可选择像素 ROI 范围。
        /// 优先级：SDK 原生点云 > 提取的内参 > 默认值。
        /// </summary>
        /// <param name="roi">可选像素 ROI [x0, y0, x1, y1]，null 为全图。</param>
        public List<Vector3> GetPointCloud(int[]? roi = null)
        {
            if (_pointCloud != null && roi == null && !HasSdkPointCloud)
                return _pointCloud;

            // ★ 优先使用 SDK 原生点云
            if (HasSdkPointCloud)
                return GetPointCloudFromSdk(roi);

            // ★ 回退：使用内参手动转换
            return GetPointCloudManual(roi);
        }

        /// <summary>
        /// 从 SDK 原生点云数据中提取指定 ROI 的点。
        /// </summary>
        private List<Vector3> GetPointCloudFromSdk(int[]? roi)
        {
            int x0 = 0, y0 = 0, x1 = Width, y1 = Height;
            if (roi != null && roi.Length >= 4)
            {
                x0 = Math.Max(0, roi[0]);
                y0 = Math.Max(0, roi[1]);
                x1 = Math.Min(Width, roi[2]);
                y1 = Math.Min(Height, roi[3]);
            }

            var points = new List<Vector3>();
            var data = _sdkPackedXyz!;
            
            // 注意：标定变换统一由 StackerOffsetAlgo.GetBasePointsFromFrame() 处理，
            // 此处仅提取 SDK 原始相机坐标系点云，避免重复应用外参。
            for (int v = y0; v < y1; v++)
            {
                int rowBase = v * Width;
                for (int u = x0; u < x1; u++)
                {
                    int idx = (rowBase + u) * 3;
                    float x = data[idx];
                    float y = data[idx + 1];
                    float z = data[idx + 2];
                    if (z > 0 && z <= 10000)
                    {
                        points.Add(new Vector3(x, y, z));
                    }
                }
            }

            // 全图请求时缓存
            if (roi == null)
                _pointCloud = points;

            return points;
        }

        /// <summary>
        /// 手动公式转换（回退方案）：X=(u-cx)*Z/fx, Y=(v-cy)*Z/fy。
        /// </summary>
        private List<Vector3> GetPointCloudManual(int[]? roi)
        {
            double fx, fy, cx, cy;
            if (Intrinsics.HasValue)
            {
                var intr = Intrinsics.Value;
                fx = intr.fx; fy = intr.fy; cx = intr.cx; cy = intr.cy;
            }
            else
            {
                cx = Width / 2.0;
                cy = Height / 2.0;
                fx = 1000.0;
                fy = 1000.0;
            }

            int x0 = 0, y0 = 0, x1 = Width, y1 = Height;
            if (roi != null && roi.Length >= 4)
            {
                x0 = Math.Max(0, roi[0]);
                y0 = Math.Max(0, roi[1]);
                x1 = Math.Min(Width, roi[2]);
                y1 = Math.Min(Height, roi[3]);
            }

            var points = new List<Vector3>();
            
            // 注意：标定变换统一由 StackerOffsetAlgo.GetBasePointsFromFrame() 处理，
            // 此处仅生成内参投射的相机坐标系点云，避免重复应用外参。
            for (int v = y0; v < y1; v++)
            {
                int rowBase = v * Width;
                for (int u = x0; u < x1; u++)
                {
                    ushort z = DepthRaw[rowBase + u];
                    if (z == 0 || z > 10000) continue;

                    double zMm = z;
                    double xMm = (u - cx) * zMm / fx;
                    double yMm = (v - cy) * zMm / fy;
                    
                    float x = (float)xMm;
                    float y = (float)yMm;
                    float zz = (float)zMm;
                    
                    points.Add(new Vector3(x, y, zz));
                }
            }

            if (roi == null)
                _pointCloud = points;

            return points;
        }
    }
}
