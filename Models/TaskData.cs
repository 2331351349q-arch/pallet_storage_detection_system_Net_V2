using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pallet_storage_detection_system_Net_V2.Models
{
    /// <summary>
    /// 表示从 Redis 获取的任务数据实体类。
    /// </summary>
    public class TaskData
    {
        /// <summary>
        /// 任务标志位。
        /// 1: 货位检测, 2: 堆垛机偏移检测, 3: 变形检测, 4/5: 盘点(2D相机扫码)。
        /// </summary>
        public int Flag { get; set; }

        /// <summary>
        /// 检测侧位。
        /// 可选值为 "left" 或 "right"。
        /// </summary>
        public string Side { get; set; }

        /// <summary>
        /// 任务触发的时间戳字符串。
        /// </summary>
        public string TaskTime { get; set; }

        /// <summary>
        /// 货架横梁长度，区分不同尺寸货架的标定数据 (例如 2180, 1380)。默认 2180。
        /// </summary>
        public int BeamLength { get; set; } = 2180;

        /// <summary>
        /// 重写 ToString 方法，用于在日志界面清晰展示任务关键参数。
        /// 内部包含了对时间字符串乱码的清洗逻辑。
        /// </summary>
        /// <returns>格式化后的任务描述字符串。</returns>
        public override string ToString()
        {
            string cleanTime = TaskTime ?? "";
            // 清理乱码和非 ASCII 字符（处理外部环境编码不一致导致的中文字符显示乱码）
            cleanTime = System.Text.RegularExpressions.Regex.Replace(cleanTime, @"[^\x20-\x7E]", "");
            cleanTime = System.Text.RegularExpressions.Regex.Replace(cleanTime, @"\s+", " ").Trim();
            
            return $"Flag={Flag}, Side={Side}, Beam={BeamLength}, Time={cleanTime}";
        }
    }
}

