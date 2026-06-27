using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using pallet_storage_detection_system_Net_V2.Models;
using pallet_storage_detection_system_Net_V2.Communication;
using pallet_storage_detection_system_Net_V2.Config;
using pallet_storage_detection_system_Net_V2.Devices;

namespace pallet_storage_detection_system_Net_V2.Control
{
    /// <summary>
    /// 任务管理器，作为全系统的核心调度中枢。
    /// 维护一个线程安全的任务队列，并开启独立的后台消费者线程按序处理检测请求。
    /// </summary>
    public class TaskManager
    {
        private static readonly string ImageRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");

        private BlockingCollection<TaskData> _taskQueue;//线程安全集合
        private RedisCommunicator _redisComm;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 当产生新的日志消息时触发。用于 UI 实时展现系统状态。
        /// </summary>
        public event Action<string> OnLogMessage;

        /// <summary>
        /// 当相机抓取到新图像并准备好显示时触发。
        /// 参数 1 为相机索引，参数 2 为图像位图对象。
        /// </summary>
        public event Action<int, object> OnImageUpdated;

        /// <summary>
        /// 构造函数，建立任务队列并订阅 Redis 通信器的触发事件。
        /// </summary>
        /// <param name="redisComm">已实例化的 Redis 通信组件。</param>
        public TaskManager(RedisCommunicator redisComm)
        {
            _taskQueue = new BlockingCollection<TaskData>();
            _redisComm = redisComm;

            // 订阅通信类的事件：一旦 Redis 监听到合法信号，立即将任务塞进本地生产消费队列。
            _redisComm.OnTaskTriggered += (taskData) => EnqueueTask(taskData);
        }

        /// <summary>
        /// 内部日志记录辅助方法，同时支持时间戳和阶段耗时记录。
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine(message);
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        /// <summary>
        /// 记录阶段耗时的日志方法。
        /// </summary>
        private void LogPhaseTime(string phaseName, DateTime startTime, DateTime? endTime = null)
        {
            endTime = endTime ?? DateTime.Now;
            var elapsed = (endTime.Value - startTime).TotalMilliseconds;
            Log($"⏱️  [{phaseName}] 耗时: {elapsed:F2}ms");
        }

        /// <summary>
        /// 将外部任务压入待处理队列。
        /// </summary>
        /// <param name="task">任务实体。</param>
        public void EnqueueTask(TaskData task)
        {
            var queueStartTime = DateTime.Now;
            _taskQueue.Add(task);
            var queueTime = DateTime.Now;
            
            // 计算从Redis检测到入队的耗时
            var fromDetectTime = (queueTime - _redisComm.TaskDetectedTime).TotalMilliseconds;
            Log($"Task Added To Queue: {task.Flag} - {task.Side}");
            Log($"⏱️  [Redis检测→队列入队] 耗时: {fromDetectTime:F2}ms");
        }

        /// <summary>
        /// 开启任务处理器后台线程。
        /// </summary>
        public void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // 启动独立消费者线程提取任务，保证 UI 线程绝对不卡顿。
            Task.Factory.StartNew(ExecutionLoop, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// 停止任务处理器，并优雅关闭阻塞队列。
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _taskQueue?.CompleteAdding(); 
        }

        /// <summary>
        /// 核心消费者循环逻辑。
        /// 负责：查找映射、并发抓图、算法路由、评价计算、结果上报。
        /// </summary>
        private async Task ExecutionLoop()
        {
            foreach (var task in _taskQueue.GetConsumingEnumerable(_cancellationTokenSource.Token))
            {
                try
                {
                    var totalStartTime = DateTime.Now;
                    Log($"\n>>> 开始处理新任务: {task}");

                    // 1. 准备结果容器。
                    DetectionResult result = new DetectionResult
                    {
                        ResultType = task.Flag,
                        Side = task.Side,
                        LastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    bool algoSuccess;
                    var imagesToSave = new List<(String Name, Image Img)>();

                    // 2. 统一抓图流程：所有 Flag 类型均通过相机抓图。
                    {
                        // Flag=1,2,3,4,5 统一：按配置的 SN 数量抓取图像
                        List<string> targetSNs = ConfigManager.GetTargetCameraSNs(task.Flag, task.Side);
                        int requiredCameraCount = targetSNs?.Count ?? 0;

                        if (targetSNs == null || targetSNs.Count < requiredCameraCount)
                        {
                            Log($"配置文件中相应映射 SN 数量不足（需 {requiredCameraCount} 台），任务中止");
                            await _redisComm.ClearTaskKeysAsync();
                            continue;
                        }

                        List<Task<object?>> grabTasks = new List<Task<object?>>();
                        for (int i = 0; i < requiredCameraCount; i++)
                        {
                            string sn = targetSNs[i];
                            var camera = DeviceManager.GetCamera(sn);
                            if (camera != null)
                            {
                                grabTasks.Add(GrabFrameSafeAsync(camera, task.Flag, i + 1));
                            }
                            else
                            {
                                Log($"警告: 系统硬件池中未找到 SN 为 {sn} 的在线相机");
                            }
                        }

                        if (grabTasks.Count != requiredCameraCount)
                        {
                            Log($"有效抓取设备数目不满足要求（需 {requiredCameraCount} 台），任务失败！");
                            await _redisComm.ClearTaskKeysAsync();
                            continue;
                        }

                        var grabStartTime = DateTime.Now;
                        var images = await Task.WhenAll(grabTasks);
                        LogPhaseTime($"相机抓图(Flag{task.Flag})", grabStartTime);
                        
                        var image1 = images.Length > 0 ? images[0] : null;
                        var image2 = images.Length > 1 ? images[1] : image1;
                        Log($"{images.Length} 帧图像抓取完毕，进入算法周期。");

                        // 同步推送至 UI 渲染界面（最多更新 4 个窗口，索引从 1 起）。
                        for (int i = 0; i < images.Length && i < 4; i++)
                        {
                            if (images[i] != null)
                            {
                                int uiIndex = (task.Side?.ToLower() == "left") ? (i + 3) : (i + 1);
                                OnImageUpdated?.Invoke(uiIndex, images[i]!);
                            }
                        }

                        for (int i = 0; i < images.Length; i++)
                        {
                            int uiIndex = (task.Side?.ToLower() == "left") ? (i + 3) : (i + 1);
                            string name = $"camera_{uiIndex}";
                            if (images[i] is Image img)
                            {
                                imagesToSave.Add((name, (Image)img.Clone()));
                            }
                            else if (images[i] is DepthFrameData depthFrame)
                            {
                                imagesToSave.Add((name, (Image)depthFrame.PreviewImage.Clone()));
                            }
                        }

                        var algoStartTime = DateTime.Now;
                        algoSuccess = await ExecuteAlgorithmAsync(task.Flag, task.Side, image1, image2, result);
                        LogPhaseTime($"算法执行(Flag{task.Flag})", algoStartTime);
                    }

                    result.Success = algoSuccess;

                    // 5. 评价阶段：盘库任务(Flag 4/5)不参与偏移阈值判定。
                    if (task.Flag != 4 && task.Flag != 5)
                    {
                        result.ApplyThresholds(ConfigManager.Instance);
                    }

                    // 6. 上报阶段：组装结果推回 Redis，并清理触发标志位。
                    string statusInfo = GetStatusSummary(result);
                    if (task.Flag == 4 || task.Flag == 5)
                    {
                        Log(statusInfo);
                    }
                    else
                    {
                        Log($"{statusInfo} 计算流程结束. 确认结果已同步至 Redis 缓存。");
                    }

                    Log(GetDetailedResultSummary(result));

                    // 记录Redis写入耗时
                    var redisWriteStartTime = DateTime.Now;
                    await _redisComm.WriteResultAsync(result);
                    LogPhaseTime($"Redis写入结果", redisWriteStartTime);

                    var redisClearStartTime = DateTime.Now;
                    await _redisComm.ClearTaskKeysAsync();
                    LogPhaseTime($"Redis清理任务键", redisClearStartTime);

                    // 记录总耗时
                    LogPhaseTime($"总耗时(Redis检测→写入完成)", _redisComm.TaskDetectedTime);

                    _ = Task.Run(() => SaveTaskImages(task, imagesToSave));
                }
                catch (OperationCanceledException)
                {
                    Log("任务执行线程已取消。");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"任务执行流程发生未捕获异常: {ex.Message}");
                    await _redisComm.ClearTaskKeysAsync(); // 异常时也必须重置 Redis 触发，防止外部逻辑挂起。
                }
            }
        }

        /// <summary>
        /// 根据检测结果生成带图标的简要文字状态汇总。
        /// </summary>
        private string GetStatusSummary(DetectionResult res)
        {
            if (!res.Success) return "❌ [处理失败]";

            if (res.ResultType == 4) return "ℹ️ [盘库进行中]";
            if (res.ResultType == 5) return "✅ [盘库结束]";
            
            // 使用堆垛机左右偏移作为代表性指标进行状态等级初步展示。
            var eval = Algorithms.ThresholdEvaluator.Evaluate(res.OffsetLatMmValue, ConfigManager.Instance.Algorithms.StackerOffset.LateralThreshold);
            return $"{eval.StatusIcon} [{eval.StatusName}]";
        }

        private string GetDetailedResultSummary(DetectionResult res)
        {
            if (!res.Success)
            {
                return $"📌 结果明细(Flag{res.ResultType}): 处理失败";
            }

            switch (res.ResultType)
            {
                case 1:
                    return $"📌 结果明细(Flag1): slot_occupied={(res.SlotOccupied ? "true" : "false")}";

                case 2:
                    return $"📌 结果明细(Flag2): offset_{res.Side}={res.OffsetLatMmValue:F1}mm {res.OffsetLatMmWarningAlarm}";

                case 3:
                    return $"📌 结果明细(Flag3): rackL={res.RackDefMmLeftValue:F1}mm {res.RackDefMmLeftWarningAlarm}, rackR={res.RackDefMmRightValue:F1}mm {res.RackDefMmRightWarningAlarm}, armL={res.ArmDefAngleLeftValue:F1}° {res.ArmDefAngleLeftWarningAlarm}, armR={res.ArmDefAngleRightValue:F1}° {res.ArmDefAngleRightWarningAlarm}";

                case 4:
                case 5:
                    return $"📌 结果明细(Flag{res.ResultType}): result_barcodes={res.ResultBarcodes}";

                default:
                    return $"📌 结果明细(Flag{res.ResultType}): 无可用字段";
            }
        }

        /// <summary>
        /// 算法执行路由入口。
        /// 负责根据任务 Flag 将采集到的底层图像数据分发给不同的视觉处理模型类。
        /// </summary>
        /// <param name="flag">任务功能号 (1-5)。</param>
        /// <param name="side">测点侧位 ("left"/"right")。</param>
        /// <param name="img1">左侧原始采集到的图像对象（通常为 Bitmap）。</param>
        /// <param name="img2">右侧原始采集到的图像对象。</param>
        /// <param name="res">外部传入的任务结果记录实体，算法将在此基础上填充计算后的数值。</param>
        /// <returns>算法处理是否成功。若为 false，系统将记录日志并中止该次上报。</returns>
        private async Task<bool> ExecuteAlgorithmAsync(int flag, string side, object img1, object img2, DetectionResult res)
        {
            // 在此模拟一个 500ms 的图像处理延迟，代表真实的 Halcon/OpenCV 计算开销。
            await Task.Delay(500);

            try
            {
                // 根据 Flag 核心指令分流至具体的静态算法类进行高性能计算。
                switch (flag)
                {
                    case 1:
                        // 货位占用检查 (Flag 1) - 双相机输入
                        return Algorithms.SlotOccupancyAlgo.Run(side, img1, img2, res);

                    case 2:
                        // 堆垛机偏移检测 (Flag 2) — 双相机融合输入
                        return Algorithms.StackerOffsetAlgo.Run(img1, img2, res);

                    case 3:
                        // 货架立柱托臂变形检测 (Flag 3) — 双相机融合输入
                        return Algorithms.RackDeformationAlgo.Run(img1, img2, res);

                    case 4:
                    case 5:
                        // 视觉盘库逻辑：4 为启动任务，5 为停止任务 (Flag 4/5)
                        return Algorithms.VisualInventoryAlgo.Run(flag, img1, img2, res, Log);

                    default:
                        Log($"无法识别的任务指令 Flag={flag}，取消算法路由。");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log($"算法模块内部逻辑崩溃 (Flag={flag}): {ex.Message}");
                return false;
            }
        }

        private void SaveTaskImages(TaskData task, List<(string Name, Image Img)> images)
        {
            if (images == null || images.Count == 0)
            {
                Log($"⚠️ 图像保存跳过: imagesToSave 为空 (Flag={task?.Flag})");
                return;
            }

            try
            {
                var triggerTime = ParseTaskTime(task?.TaskTime);
                string flagDir = Path.Combine(ImageRootPath, $"flag{task.Flag}");
                string dateDir = Path.Combine(flagDir, triggerTime.ToString("yyyy_MM_dd"));
                string triggerDir = Path.Combine(dateDir, triggerTime.ToString("HH_mm_ss_fff"));

                Directory.CreateDirectory(triggerDir);

                for (int i = 0; i < images.Count; i++)
                {
                    var (name, img) = images[i];
                    string filePath = Path.Combine(triggerDir, $"{name}_{i + 1:D2}.png");
                    img.Save(filePath, ImageFormat.Png);
                }

                Log($"💾 图像已保存: {triggerDir} ({images.Count} 张)");
            }
            catch (Exception ex)
            {
                Log($"❌ 图像存储失败(Flag={task?.Flag}): {ex.Message}");
            }
            finally
            {
                foreach (var item in images)
                {
                    item.Img.Dispose();
                }
            }
        }

        private DateTime ParseTaskTime(string? taskTime)
        {
            if (string.IsNullOrWhiteSpace(taskTime)) return DateTime.Now;

            string[] formats = new[]
            {
                "yyyy/MM/dd HH:mm:ss.fff",
                "yyyy/MM/dd HH:mm:ss.ff",
                "yyyy/MM/dd HH:mm:ss.f",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "yyyyMMddHHmmssfff",
                "yyyyMMddHHmmss"
            };

            if (DateTime.TryParseExact(taskTime.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(taskTime, out parsed))
            {
                return parsed;
            }

            return DateTime.Now;
        }

        private async Task<object?> GrabFrameSafeAsync(ICameraDevice camera, int flag, int captureIndex)
        {
            try
            {
                return await camera.GrabFrameAsync();
            }
            catch (Exception ex)
            {
                Log($"⚠️ [Flag{flag}] 第 {captureIndex} 帧采集图像失败: {ex.Message}");
                return null;
            }
        }
    }

    // ----------- 占位壳对象，以便使以上代码通过编译 -----------

}

