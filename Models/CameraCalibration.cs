using System;
using System.Collections.Generic;

namespace material_box_storage_detection_system_Net.Models
{
    /// <summary>
    /// 单台 3D 相机的标定参数。
    /// 包含从相机坐标系到基准（世界）坐标系的旋转矩阵与平移向量。
    /// </summary>
    public class CameraCalibration
    {
        /// <summary>目标相机的序列号。</summary>
        public string CameraSn { get; set; } = string.Empty;

        /// <summary>3x3 旋转矩阵 R（行优先：R[i,j] = 第 i 行第 j 列）。</summary>
        public List<List<double>> RotationMatrix { get; set; } = new();

        /// <summary>3x1 平移向量 T（mm）。</summary>
        public List<double> TranslationVector { get; set; } = new();

        /// <summary>参考平面在基准坐标系中的法向量（单位向量）。</summary>
        public List<double> RefPlaneNormalBase { get; set; } = new();

        /// <summary>参考平面在相机坐标系中的法向量（标定时测得，单位向量）。</summary>
        public List<double> RefPlaneNormalCam { get; set; } = new();

        /// <summary>标定时间。</summary>
        public DateTime CalibratedAt { get; set; } = DateTime.Now;

        /// <summary>相机内参 fx（像素）。</summary>
        public double Fx { get; set; } = 1000.0;

        /// <summary>相机内参 fy（像素）。</summary>
        public double Fy { get; set; } = 1000.0;

        /// <summary>相机内参 cx（像素）。</summary>
        public double Cx { get; set; } = 320.0;

        /// <summary>相机内参 cy（像素）。</summary>
        public double Cy { get; set; } = 240.0;

        /// <summary>用户手动量测的相机到参考平面的距离（mm）。</summary>
        public double MeasuredDistanceMm { get; set; }

        /// <summary>相机平面方程中的 D 值（标定时测得）。</summary>
        public double PlaneD { get; set; }

        // ---- 辅助方法 ----

        /// <summary>获取旋转矩阵 double[,] 格式。</summary>
        public double[,] GetRotationMatrix()
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = (RotationMatrix.Count > i && RotationMatrix[i].Count > j)
                        ? RotationMatrix[i][j] : (i == j ? 1.0 : 0.0);
            return r;
        }

        /// <summary>获取平移向量 double[] 格式。</summary>
        public double[] GetTranslationVector()
        {
            var t = new double[3];
            for (int i = 0; i < 3; i++)
                t[i] = TranslationVector.Count > i ? TranslationVector[i] : 0.0;
            return t;
        }

        /// <summary>判断是否已完成有效标定。</summary>
        public bool IsValid =>
            CameraSn.Length > 0 &&
            RotationMatrix.Count == 3 &&
            RotationMatrix[0].Count == 3;
    }
}
