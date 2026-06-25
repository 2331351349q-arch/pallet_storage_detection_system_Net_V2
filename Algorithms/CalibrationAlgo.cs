using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pallet_storage_detection_system_Net_V2.Devices;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 3D 相机标定算法：RANSAC 平面拟合 + Rodrigues 旋转 + 平移求解。
    /// 目标：将相机坐标系变换至基准坐标系（X-Z 平行地面，Y 垂直地面）。
    /// </summary>
    public static class CalibrationAlgo
    {
        private static readonly Random _rng = new();

        // ============ 深度转点云 ============

        /// <summary>
        /// 将深度图 (ushort[], mm) 转为相机坐标系下的 3D 点列表。
        /// 使用小孔模型：X = (u - cx) * Z / fx, Y = (v - cy) * Z / fy, Z = depth。
        /// </summary>
        /// <param name="depthRaw">深度原始数据 (mm)，长度 = width * height。</param>
        /// <param name="width">图像宽度。</param>
        /// <param name="height">图像高度。</param>
        /// <param name="fx">x 方向焦距（像素）。</param>
        /// <param name="fy">y 方向焦距（像素）。</param>
        /// <param name="cx">主点 x 坐标（像素）。</param>
        /// <param name="cy">主点 y 坐标（像素）。</param>
        /// <param name="roi">可选 ROI（像素范围），为 null 则用全图。</param>
        /// <returns>相机坐标系下的 3D 点（mm）。</returns>
        public static List<Vector3> DepthToPointCloud(
            ushort[] depthRaw, int width, int height,
            double fx, double fy, double cx, double cy,
            int[]? roi = null)
        {
            int x0 = 0, y0 = 0, x1 = width, y1 = height;
            if (roi != null && roi.Length >= 4)
            {
                x0 = Math.Max(0, roi[0]);
                y0 = Math.Max(0, roi[1]);
                x1 = Math.Min(width, roi[2]);
                y1 = Math.Min(height, roi[3]);
            }

            var points = new List<Vector3>();
            for (int v = y0; v < y1; v++)
            {
                for (int u = x0; u < x1; u++)
                {
                    int idx = v * width + u;
                    if (idx >= depthRaw.Length) continue;

                    double z = depthRaw[idx];
                    if (z == 0 || z > 10000) continue; // 过滤无效 / 过远点

                    double x = (u - cx) * z / fx;
                    double y = (v - cy) * z / fy;
                    points.Add(new Vector3((float)x, (float)y, (float)z));
                }
            }
            return points;
        }

        // ============ RANSAC 平面拟合 ============

        /// <summary>
        /// 对点云进行 RANSAC 平面拟合。
        /// </summary>
        /// <param name="points">3D 点云。</param>
        /// <param name="maxIterations">最大迭代次数。</param>
        /// <param name="distanceThreshold">内点距离阈值 (mm)。</param>
        /// <returns>(A, B, C, D) 平面方程参数及内点索引列表。</returns>
        public static (double A, double B, double C, double D, List<int> Inliers)
            RansacPlaneFitting(List<Vector3> points, int maxIterations = 500, double distanceThreshold = 5.0)
        {
            if (points.Count < 3)
                return (0, 0, 0, 0, new List<int>());

            double bestA = 0, bestB = 0, bestC = 0, bestD = 0;
            double bestScore = double.MinValue;
            List<int> bestInliers = new();

            int sampleSize = Math.Min(1000, points.Count);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // 随机选 3 个点
                int i0 = _rng.Next(points.Count);
                int i1, i2;
                do { i1 = _rng.Next(points.Count); } while (i1 == i0);
                do { i2 = _rng.Next(points.Count); } while (i2 == i0 || i2 == i1);

                Vector3 p0 = points[i0], p1 = points[i1], p2 = points[i2];

                // 平面法向量 = (p1-p0) × (p2-p0)
                Vector3 v1 = p1 - p0;
                Vector3 v2 = p2 - p0;
                Vector3 normal = Vector3.Cross(v1, v2);

                if (normal.LengthSquared() < 1e-9f) continue;

                normal = Vector3.Normalize(normal);
                double a = normal.X, b = normal.Y, c = normal.Z;
                double d = -(a * p0.X + b * p0.Y + c * p0.Z);

                // 验证：用采样点评估模型得分
                var inliers = new List<int>();
                double score = 0;
                for (int j = 0; j < sampleSize; j++)
                {
                    int idx = _rng.Next(points.Count);
                    Vector3 pt = points[idx];
                    double dist = Math.Abs(a * pt.X + b * pt.Y + c * pt.Z + d);
                    if (dist < distanceThreshold)
                    {
                        inliers.Add(idx);
                        score += 1.0 / (1.0 + dist); // 距离越小权重越大
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestA = a; bestB = b; bestC = c; bestD = d;
                    bestInliers = inliers;
                }
            }

            // 用最佳内点集重新拟合
            if (bestInliers.Count >= 3)
            {
                // 内点均值（质心）
                Vector3 centroid = Vector3.Zero;
                foreach (var idx in bestInliers) centroid += points[idx];
                centroid /= bestInliers.Count;

                // 直接保留 RANSAC 选出的最优法向量 (bestA, bestB, bestC)
                // 仅通过质心重新计算 bestD，从而消除随机三点带来的平移误差
                double len = Math.Sqrt(bestA * bestA + bestB * bestB + bestC * bestC);
                bestA /= len; bestB /= len; bestC /= len;
                bestD = -(bestA * centroid.X + bestB * centroid.Y + bestC * centroid.Z);
            }

            return (bestA, bestB, bestC, bestD, bestInliers);
        }

        // ============ Rodrigues 旋转公式 ============

        /// <summary>
        /// 使用 Rodrigues 旋转公式计算将源单位向量旋转到目标单位向量的 3x3 旋转矩阵。
        /// </summary>
        /// <param name="srcNormal">源向量（相机坐标系下的法向量，单位向量）。</param>
        /// <param name="dstNormal">目标向量（基准坐标系下的法向量，单位向量）。</param>
        /// <returns>3x3 旋转矩阵 R。</returns>
        public static double[,] ComputeRodriguesRotation(double[] srcNormal, double[] dstNormal)
        {
            var r = new double[3, 3];
            double ax = srcNormal[0], ay = srcNormal[1], az = srcNormal[2];
            double bx = dstNormal[0], by = dstNormal[1], bz = dstNormal[2];

            // cos = a·b
            double cosTheta = ax * bx + ay * by + az * bz;
            cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);

            // 叉积 v = a × b (旋转轴)
            double vx = ay * bz - az * by;
            double vy = az * bx - ax * bz;
            double vz = ax * by - ay * bx;
            double sinTheta = Math.Sqrt(vx * vx + vy * vy + vz * vz);

            const double eps = 1e-8;
            if (sinTheta < eps)
            {
                if (cosTheta > 0) // 同向，单位矩阵
                {
                    r[0, 0] = 1; r[1, 1] = 1; r[2, 2] = 1;
                }
                else // 反向，绕任意垂直轴旋转 180°
                {
                    // 找与 a 正交的轴
                    double ux, uy, uz;
                    if (Math.Abs(ax) < 0.9)
                    {
                        double len = Math.Sqrt(ay * ay + az * az);
                        ux = 0; uy = -az / len; uz = ay / len;
                    }
                    else
                    {
                        double len = Math.Sqrt(ax * ax + az * az);
                        ux = -az / len; uy = 0; uz = ax / len;
                    }
                    r[0, 0] = 2 * ux * ux - 1; r[0, 1] = 2 * ux * uy;     r[0, 2] = 2 * ux * uz;
                    r[1, 0] = 2 * ux * uy;     r[1, 1] = 2 * uy * uy - 1; r[1, 2] = 2 * uy * uz;
                    r[2, 0] = 2 * ux * uz;     r[2, 1] = 2 * uy * uz;     r[2, 2] = 2 * uz * uz - 1;
                }
                return r;
            }

            // 归一化旋转轴
            vx /= sinTheta; vy /= sinTheta; vz /= sinTheta;

            // Rodrigues 公式: R = I + sin(θ)·[v]× + (1-cos(θ))·[v]×²
            // [v]× 为 v 的反对称矩阵
            double oneMc = 1.0 - cosTheta;

            r[0, 0] = cosTheta + vx * vx * oneMc;
            r[0, 1] = -vz * sinTheta + vx * vy * oneMc;
            r[0, 2] = vy * sinTheta + vx * vz * oneMc;

            r[1, 0] = vz * sinTheta + vx * vy * oneMc;
            r[1, 1] = cosTheta + vy * vy * oneMc;
            r[1, 2] = -vx * sinTheta + vy * vz * oneMc;

            r[2, 0] = -vy * sinTheta + vx * vz * oneMc;
            r[2, 1] = vx * sinTheta + vy * vz * oneMc;
            r[2, 2] = cosTheta + vz * vz * oneMc;

            return r;
        }

        // ============ 完整标定 ============

        /// <summary>
        /// 执行完整标定流程，返回 CameraCalibration 结果。
        /// </summary>
        /// <param name="cameraSn">相机 SN。</param>
        /// <param name="depthRaw">深度图 (ushort[], mm)。</param>
        /// <param name="width">图像宽度。</param>
        /// <param name="height">图像高度。</param>
        /// <param name="fx">相机 fx。</param>
        /// <param name="fy">相机 fy。</param>
        /// <param name="cx">相机 cx。</param>
        /// <param name="cy">相机 cy。</param>
        /// <param name="roi">ROI 像素范围 [x0, y0, x1, y1]。</param>
        /// <param name="targetNormalBase">基准坐标系中参考平面的法向量（单位向量）。</param>
        /// <param name="measuredDistanceMm">手动量测的相机到参考平面的距离 (mm)。</param>
        /// <returns>标定结果。</returns>
        public static CameraCalibration Calibrate(
            string cameraSn,
            ushort[] depthRaw, int width, int height,
            double fx, double fy, double cx, double cy,
            int[] roi,
            double[] targetNormalBase,
            double measuredDistanceMm)
        {
            // 1. 提取 ROI 内的点云
            var points = DepthToPointCloud(depthRaw, width, height, fx, fy, cx, cy, roi);

            if (points.Count < 100)
                throw new InvalidOperationException($"ROI 范围内有效点不足 ({points.Count} 个)，请调整 ROI 或确保相机稳定。");

            // 2. RANSAC 平面拟合
            var (a, b, c, d, inliers) = RansacPlaneFitting(points);

            if (inliers.Count < 10)
                throw new InvalidOperationException($"RANSAC 内点不足 ({inliers.Count})，请重新选择参考平面区域。");

            // 归一化法向量（确保指向相机方向）
            double len = Math.Sqrt(a * a + b * b + c * c);
            double nx = a / len, ny = b / len, nz = c / len;
            double planeD = d / len;

            // 确保法向量指向相机（即 D < 0 表示原点在平面后方）
            // 在相机坐标系中，平面应在相机前方 (Z>0)
            if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; planeD = -planeD; }

            // 3. Rodrigues 旋转
            double[] srcNormal = { nx, ny, nz };
            double[,] rotation = ComputeRodriguesRotation(srcNormal, targetNormalBase);

            // 4. 计算平移
            // 相机到平面的有符号距离：|D| 表示原点到平面的垂直距离
            // 根据目标法向量方向自动分配平移分量，兼容地面标定和货架面标定
            double[] translation = new double[3];
            translation[0] = -measuredDistanceMm * targetNormalBase[0];
            translation[1] = -measuredDistanceMm * targetNormalBase[1];
            translation[2] = -measuredDistanceMm * targetNormalBase[2];

            return new CameraCalibration
            {
                CameraSn = cameraSn,
                RotationMatrix = new List<List<double>>
                {
                    new() { rotation[0, 0], rotation[0, 1], rotation[0, 2] },
                    new() { rotation[1, 0], rotation[1, 1], rotation[1, 2] },
                    new() { rotation[2, 0], rotation[2, 1], rotation[2, 2] }
                },
                TranslationVector = new List<double> { translation[0], translation[1], translation[2] },
                RefPlaneNormalBase = new List<double> { targetNormalBase[0], targetNormalBase[1], targetNormalBase[2] },
                RefPlaneNormalCam = new List<double> { nx, ny, nz },
                Fx = fx, Fy = fy, Cx = cx, Cy = cy,
                MeasuredDistanceMm = measuredDistanceMm,
                PlaneD = planeD,
                CalibratedAt = DateTime.Now
            };
        }

        // ============ 方案A：直接使用点云数据的标定方法（不依赖手动的 fx/fy/cx/cy）============

        /// <summary>
        /// 使用 DepthFrameData 自动生成点云并执行标定（推荐）。
        /// 优先使用 DepthFrameData 中已缓存的 SDK 内参，回退到默认值。
        /// </summary>
        public static CameraCalibration Calibrate(
            string cameraSn,
            DepthFrameData depthFrame,
            int[] roi,
            double[] targetNormalBase,
            double measuredDistanceMm)
        {
            // 直接从 DepthFrameData 获取点云（内部已处理内参）
            var points = depthFrame.GetPointCloud(roi);

            // 使用获取到的内参（或默认值）记录到标定结果中
            double fx, fy, cx, cy;
            if (depthFrame.Intrinsics.HasValue)
            {
                var intr = depthFrame.Intrinsics.Value;
                fx = intr.fx; fy = intr.fy; cx = intr.cx; cy = intr.cy;
            }
            else
            {
                fx = 1000.0; fy = 1000.0;
                cx = depthFrame.Width / 2.0; cy = depthFrame.Height / 2.0;
            }

            return CalibrateCore(cameraSn, points, fx, fy, cx, cy, targetNormalBase, measuredDistanceMm);
        }

        /// <summary>
        /// 使用已有的点云列表直接执行标定。
        /// </summary>
        public static CameraCalibration CalibrateFromPointCloud(
            string cameraSn,
            List<Vector3> points,
            double fx, double fy, double cx, double cy,
            double[] targetNormalBase,
            double measuredDistanceMm)
        {
            return CalibrateCore(cameraSn, points, fx, fy, cx, cy, targetNormalBase, measuredDistanceMm);
        }

        /// <summary>
        /// 标定核心逻辑（纯点云输入，不再依赖深度图）。
        /// </summary>
        private static CameraCalibration CalibrateCore(
            string cameraSn,
            List<Vector3> points,
            double fx, double fy, double cx, double cy,
            double[] targetNormalBase,
            double measuredDistanceMm)
        {
            if (points.Count < 100)
                throw new InvalidOperationException($"ROI 范围内有效点不足 ({points.Count} 个)，请调整 ROI 或确保相机稳定。");

            // 1. RANSAC 平面拟合
            var (a, b, c, d, inliers) = RansacPlaneFitting(points);

            if (inliers.Count < 10)
                throw new InvalidOperationException($"RANSAC 内点不足 ({inliers.Count})，请重新选择参考平面区域。");

            // 2. 归一化法向量
            double len = Math.Sqrt(a * a + b * b + c * c);
            double nx = a / len, ny = b / len, nz = c / len;
            double planeD = d / len;

            // 确保法向量指向相机
            if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; planeD = -planeD; }

            // 3. Rodrigues 旋转
            double[] srcNormal = { nx, ny, nz };
            double[,] rotation = ComputeRodriguesRotation(srcNormal, targetNormalBase);

            // 4. 计算平移（按目标法向量方向自动分配）
            double[] translation = new double[3];
            translation[0] = -measuredDistanceMm * targetNormalBase[0];
            translation[1] = -measuredDistanceMm * targetNormalBase[1];
            translation[2] = -measuredDistanceMm * targetNormalBase[2];

            return new CameraCalibration
            {
                CameraSn = cameraSn,
                RotationMatrix = new List<List<double>>
                {
                    new() { rotation[0, 0], rotation[0, 1], rotation[0, 2] },
                    new() { rotation[1, 0], rotation[1, 1], rotation[1, 2] },
                    new() { rotation[2, 0], rotation[2, 1], rotation[2, 2] }
                },
                TranslationVector = new List<double> { translation[0], translation[1], translation[2] },
                RefPlaneNormalBase = new List<double> { targetNormalBase[0], targetNormalBase[1], targetNormalBase[2] },
                RefPlaneNormalCam = new List<double> { nx, ny, nz },
                Fx = fx, Fy = fy, Cx = cx, Cy = cy,
                MeasuredDistanceMm = measuredDistanceMm,
                PlaneD = planeD,
                CalibratedAt = DateTime.Now
            };
        }

        /// <summary>
        /// 应用标定变换：将相机坐标系下的点转换到基准坐标系。
        /// P_base = R * P_cam + T
        /// </summary>
        public static Vector3 TransformPoint(Vector3 pointCam, CameraCalibration calib)
        {
            var r = calib.GetRotationMatrix();
            var t = calib.GetTranslationVector();
            return new Vector3(
                (float)(r[0, 0] * pointCam.X + r[0, 1] * pointCam.Y + r[0, 2] * pointCam.Z + t[0]),
                (float)(r[1, 0] * pointCam.X + r[1, 1] * pointCam.Y + r[1, 2] * pointCam.Z + t[1]),
                (float)(r[2, 0] * pointCam.X + r[2, 1] * pointCam.Y + r[2, 2] * pointCam.Z + t[2])
            );
        }
    }
}
