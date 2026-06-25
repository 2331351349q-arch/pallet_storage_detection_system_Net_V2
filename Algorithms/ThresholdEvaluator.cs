using System;
using pallet_storage_detection_system_Net_V2.Config;

namespace pallet_storage_detection_system_Net_V2.Algorithms
{
    /// <summary>
    /// 表示单项指标的评价结果实体。
    /// </summary>
    public class EvaluationResult
    {
        /// <summary>
        /// 是否处于预警区间。
        /// </summary>
        public bool Warning { get; set; }

        /// <summary>
        /// 是否处于报警（严重超限）区间。
        /// </summary>
        public bool Alarm { get; set; }

        /// <summary>
        /// 获取当前状态对应的状态图标。
        /// </summary>
        public string StatusIcon => Alarm ? "❌" : (Warning ? "⚠️" : "✅");

        /// <summary>
        /// 获取当前状态对应的中文名称。
        /// </summary>
        public string StatusName => Alarm ? "报警" : (Warning ? "预警" : "正常");
    }

    /// <summary>
    /// 阈值判定引擎，基于 A/B/C/D 工业门限模型。
    /// </summary>
    public static class ThresholdEvaluator
    {
        /// <summary>
        /// 核心评价逻辑：基于 A/B/C/D 门限判定报警档位 (Python 算法镜像)。
        /// 判定区间说明: A < B < 0 < C < D。
        /// 1. Value < A 或 Value > D -> Alarm (报警)。
        /// 2. A <= Value < B 或 C < Value <= D -> Warning (预警)。
        /// 3. B <= Value <= C -> Normal (正常)。
        /// </summary>
        /// <param name="value">当前测得的实际数值。</param>
        /// <param name="ts">该项指标对应的阈值配置集合。</param>
        /// <returns>评价结果对象。</returns>
        public static EvaluationResult Evaluate(double value, ThresholdSet ts)
        {
            var result = new EvaluationResult();
            
            if (ts == null) return result;

            if (value < ts.A || value > ts.D)
            {
                result.Alarm = true;
            }
            else if ((value >= ts.A && value < ts.B) || (value > ts.C && value <= ts.D))
            {
                result.Warning = true;
            }

            return result;
        }
    }
}

