using StackExchange.Redis;
using System;
using System.Collections.Generic;
using pallet_storage_detection_system_Net_V2.Config;

namespace pallet_storage_detection_system_Net_V2.Models
{
    /// <summary>
    /// 表示视觉检测结果实体类，包含所有可能的检测项数据。
    /// 能够根据配置自动进行阈值判定，并支持转换为 Redis Hash 格式。
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// 任务整体逻辑是否成功。
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// 结果类型标识，与 TaskData.Flag 一致。
        /// </summary>
        public int ResultType { get; set; } = 0;

        /// <summary>
        /// 结果更新的时间。
        /// </summary>
        public string LastUpdateTime { get; set; } = "";

        /// <summary>
        /// 当前任务的测点侧位 ("left"/"right")。
        /// </summary>
        public string Side { get; set; } = "";

        // Flag 1 相关字段
        /// <summary>
        /// 货位是否被占用（仅 Flag=1 有效）。
        /// </summary>
        public bool SlotOccupied { get; set; } = true;

        // Flag 2: 堆垛机偏移量相关字段
        /// <summary>
        /// 堆垛机左右偏移值 (mm)。正值表示偏右，负值表示偏左。
        /// </summary>
        public double OffsetLatMmValue { get; set; } = 0.0;
        /// <summary>
        /// 堆垛机左右偏移的报警状态 JSON 字符串。
        /// </summary>
        public string OffsetLatMmWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        // Flag 3: 立柱与横梁变形字段
        /// <summary>
        /// 左侧立柱弯曲值 (mm) — X-Y投影直线拟合的最大X偏差。
        /// </summary>
        public double RackDefMmLeftValue { get; set; } = 4.0;
        /// <summary>
        /// 左侧立柱弯曲的报警状态 JSON 字符串。
        /// </summary>
        public string RackDefMmLeftWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        /// <summary>
        /// 右侧立柱弯曲值 (mm) — X-Y投影直线拟合的最大X偏差。
        /// </summary>
        public double RackDefMmRightValue { get; set; } = 9.2;
        /// <summary>
        /// 右侧立柱弯曲的报警状态 JSON 字符串。
        /// </summary>
        public string RackDefMmRightWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        /// <summary>
        /// 横梁下挠量 (mm) — 合并后的完整横梁点云在X-Y投影的最大Y偏差。
        /// </summary>
        public double BeamDefMmValue { get; set; } = 2.0;
        /// <summary>
        /// 横梁下挠的报警状态 JSON 字符串。
        /// </summary>
        public string BeamDefMmWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        /// <summary>
        /// 托盘左插孔变形值 (mm)。
        /// </summary>
        public double PalletHoleDefMmLeftValue { get; set; } = 0.0;
        /// <summary>
        /// 托盘左插孔变形的报警状态 JSON 字符串。
        /// </summary>
        public string PalletHoleDefMmLeftWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        /// <summary>
        /// 托盘右插孔变形值 (mm)。
        /// </summary>
        public double PalletHoleDefMmRightValue { get; set; } = 0.0;
        /// <summary>
        /// 托盘右插孔变形的报警状态 JSON 字符串。
        /// </summary>
        public string PalletHoleDefMmRightWarningAlarm { get; set; } = "{\"warning\": false, \"alarm\": false}";

        // Flag 4/5: 视觉盘库字段
        /// <summary>
        /// 条码或者库位汇总信息的 JSON 字符串。
        /// </summary>
        public string ResultBarcodes { get; set; } = "[\"BOX111\", \"BOX112\"]";

        /// <summary>
        /// 核心业务方法：根据应用配置的阈值对象，自动对当前测得的数值进行等级评定（正常/预警/报警）。
        /// </summary>
        /// <param name="config">全局配置实例。</param>
        public void ApplyThresholds(AppConfig config)
        {
            if (config == null || config.Algorithms == null) return;

            switch (ResultType)
            {
                case 1:
                    // 货位检测通常只看点云数量，目前为模拟逻辑
                    break;
                case 2:
                    var sCfg = config.Algorithms.StackerOffset;
                    OffsetLatMmWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(OffsetLatMmValue, sCfg.LateralThreshold));
                    break;
                case 3:
                    var bCfg = config.Algorithms.RackDeformation;
                    RackDefMmLeftWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(RackDefMmLeftValue, bCfg.RackThreshold));
                    RackDefMmRightWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(RackDefMmRightValue, bCfg.RackThreshold));
                    BeamDefMmWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(BeamDefMmValue, bCfg.BeamThreshold));
                    PalletHoleDefMmLeftWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(PalletHoleDefMmLeftValue, bCfg.PalletHoleThreshold));
                    PalletHoleDefMmRightWarningAlarm = FormatAlarm(Algorithms.ThresholdEvaluator.Evaluate(PalletHoleDefMmRightValue, bCfg.PalletHoleThreshold));
                    break;
            }
        }

        /// <summary>
        /// 将评价结果格式化为标准 JSON 字符串。
        /// </summary>
        private string FormatAlarm(Algorithms.EvaluationResult res)
        {
            return $"{{\"warning\": {res.Warning.ToString().ToLower()}, \"alarm\": {res.Alarm.ToString().ToLower()}}}";
        }

        /// <summary>
        /// 将阈值对象格式化为标准 JSON 字符串。
        /// </summary>
        private string FormatThreshold(ThresholdSet? ts)
        {
            if (ts == null)
            {
                return "{\"A\": 0.0, \"B\": 0.0, \"C\": 0.0, \"D\": 0.0}";
            }

            return $"{{\"A\": {ts.A:F1}, \"B\": {ts.B:F1}, \"C\": {ts.C:F1}, \"D\": {ts.D:F1}}}";
        }

        /// <summary>
        /// 将对象转化为 Redis HashEntry 数组，用于写入 Redis。
        /// 只会转化与当前 ResultType 相关的字段以节省带宽。
        /// </summary>
        /// <returns>HashEntry 数组。</returns>
        public HashEntry[] ToHashEntries()
        {
            var entries = new List<HashEntry>
            {
                new HashEntry("result_status", Success ? "success" : "fail"),
                new HashEntry("result_type", ResultType.ToString()),
                new HashEntry("last_update_time", LastUpdateTime)
            };

            switch (ResultType)
            {
                case 1:
                    entries.Add(new HashEntry("slot_occupied", SlotOccupied ? "true" : "false"));
                    break;
                case 2:
                    if (string.Equals(Side, "left", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new HashEntry("offset_lat_mm_left_value", OffsetLatMmValue.ToString("F1")));
                        entries.Add(new HashEntry("offset_lat_mm_left_warning_alarm", OffsetLatMmWarningAlarm));
                    }
                    else if (string.Equals(Side, "right", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new HashEntry("offset_lat_mm_right_value", OffsetLatMmValue.ToString("F1")));
                        entries.Add(new HashEntry("offset_lat_mm_right_warning_alarm", OffsetLatMmWarningAlarm));
                    }
                    break;
                case 3:
                    entries.Add(new HashEntry("rack_def_mm_left_value", RackDefMmLeftValue.ToString("F1")));
                    entries.Add(new HashEntry("rack_def_mm_left_warning_alarm", RackDefMmLeftWarningAlarm));
                    entries.Add(new HashEntry("rack_def_mm_right_value", RackDefMmRightValue.ToString("F1")));
                    entries.Add(new HashEntry("rack_def_mm_right_warning_alarm", RackDefMmRightWarningAlarm));
                    entries.Add(new HashEntry("beam_def_mm_value", BeamDefMmValue.ToString("F1")));
                    entries.Add(new HashEntry("beam_def_mm_warning_alarm", BeamDefMmWarningAlarm));
                    entries.Add(new HashEntry("pallet_hole_def_mm_left_value", PalletHoleDefMmLeftValue.ToString("F1")));
                    entries.Add(new HashEntry("pallet_hole_def_mm_left_warning_alarm", PalletHoleDefMmLeftWarningAlarm));
                    entries.Add(new HashEntry("pallet_hole_def_mm_right_value", PalletHoleDefMmRightValue.ToString("F1")));
                    entries.Add(new HashEntry("pallet_hole_def_mm_right_warning_alarm", PalletHoleDefMmRightWarningAlarm));
                    break;
                case 4:
                case 5:
                    entries.Add(new HashEntry("result_barcodes", ResultBarcodes));
                    break;
            }

            var cfg = ConfigManager.Instance;
            if (cfg?.Algorithms != null)
            {
                entries.Add(new HashEntry("pallet_offset_lat_threshold", FormatThreshold(cfg.Algorithms.StackerOffset?.LateralThreshold)));
                entries.Add(new HashEntry("beam_deflection_beam_threshold", FormatThreshold(cfg.Algorithms.RackDeformation?.BeamThreshold)));
                entries.Add(new HashEntry("beam_deflection_rack_threshold", FormatThreshold(cfg.Algorithms.RackDeformation?.RackThreshold)));
                entries.Add(new HashEntry("pallet_hole_deflection_threshold", FormatThreshold(cfg.Algorithms.RackDeformation?.PalletHoleThreshold)));
            }

            return entries.ToArray();
        }
    }
}

