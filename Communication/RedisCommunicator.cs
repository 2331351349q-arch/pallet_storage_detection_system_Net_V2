using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;
using pallet_storage_detection_system_Net_V2.Models;

namespace pallet_storage_detection_system_Net_V2.Communication
{
    /// <summary>
    /// Redis 通信器，负责监听外部触发信号、读写检测任务状态及其结果。
    /// </summary>
    public class RedisCommunicator
    {
        private ConnectionMultiplexer _redis;
        private IDatabase _taskDb;
        private IDatabase _resultDb;

        private bool _isListening = false;
        private int _lastFlag = 0;
        private DateTime _taskDetectedTime = DateTime.MinValue;

        /// <summary>
        /// 当监听到 Redis 任务状态发生合法跳变（由 0 变为非 0）时触发。
        /// </summary>
        public event Action<TaskData> OnTaskTriggered;

        /// <summary>
        /// Redis 通信日志回调事件。
        /// </summary>
        public event Action<string>? OnLogMessage;

        /// <summary>
        /// 任务被检测到的时间（用于性能追踪）
        /// </summary>
        public DateTime TaskDetectedTime => _taskDetectedTime;

        /// <summary>
        /// 连接到指定的 Redis 服务器。
        /// </summary>
        /// <param name="host">服务器地址。</param>
        /// <param name="port">端口号。</param>
        /// <param name="password">连接密码。</param>
        /// <param name="taskDbIndex">任务监听所在的数据库索引（通常为 0）。</param>
        /// <param name="resultDbIndex">结果写入所在的数据库索引（通常为 1）。</param>
        /// <returns>连接是否成功。</returns>
        public bool Connect(string host, int port, string password, int taskDbIndex, int resultDbIndex)
        {
            try
            {
                var options = new ConfigurationOptions
                {
                    EndPoints = { $"{host}:{port}" },
                    Password = string.IsNullOrEmpty(password) ? null : password,
                    AbortOnConnectFail = false,
                    ConnectTimeout = 5000,
                    SyncTimeout = 5000
                };

                _redis = ConnectionMultiplexer.Connect(options);
                _taskDb = _redis.GetDatabase(taskDbIndex);
                _resultDb = _redis.GetDatabase(resultDbIndex);

                // 注册连接恢复事件：当网络断开后重新连上时，自动确保 DB1 必备键
                _redis.ConnectionRestored += (sender, args) =>
                {
                    OnLogMessage?.Invoke("✔ [Redis] 连接已恢复，正在校验 DB1 必备键...");
                    _ = EnsureResultKeysAsync();
                };

                Console.WriteLine("Redis Connected.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis Connect Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 开启后台任务监听循环。
        /// </summary>
        public void StartListening()
        {
            if (_isListening) return;
            _isListening = true;

            // 采用长效后台任务跑无限轮询，防止阻塞 UI 线程
            Task.Factory.StartNew(ListenLoop, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// 停止后台监听循环。
        /// </summary>
        public void StopListening() => _isListening = false;

        /// <summary>
        /// 后台轮询逻辑：检测 vision_task_flag 的边缘触发跳变。
        /// 优化：采用更短的轮询间隔以减少响应延迟
        /// </summary>
        private async Task ListenLoop()
        {
            bool hasEnsuredResultKeys = false;

            while (_isListening)
            {
                try
                {
                    if (!hasEnsuredResultKeys && _redis != null && _redis.IsConnected)
                    {
                        hasEnsuredResultKeys = await EnsureResultKeysAsync();
                    }

                    var flagStr = await _taskDb.StringGetAsync("vision_task_flag");
                    int currentFlag = 0;

                    if (flagStr.HasValue && int.TryParse(flagStr, out currentFlag))
                    {
                        // 边缘触发逻辑：只有在值发生变化且变为正数时，才视为新任务开始。
                        if (currentFlag != _lastFlag && currentFlag > 0)
                        {
                            _taskDetectedTime = DateTime.Now;
                            var sideStr = await _taskDb.StringGetAsync("vision_task_side");
                            var timeStr = await _taskDb.StringGetAsync("vision_task_time");
                            var beamLenStr = await _taskDb.StringGetAsync("vision_task_beam_length");
                            var boxIdStr = await _taskDb.StringGetAsync("vision_box_id");

                            int beamLength = 2180;
                            if (beamLenStr.HasValue && int.TryParse(beamLenStr, out int parsedLen))
                            {
                                beamLength = parsedLen;
                            }

                            string? boxId = (!boxIdStr.IsNullOrEmpty && !string.IsNullOrWhiteSpace(boxIdStr.ToString())) ? boxIdStr.ToString().Trim() : null;

                            var task = new TaskData
                            {
                                Flag = currentFlag,
                                Side = (!sideStr.IsNullOrEmpty && !string.IsNullOrWhiteSpace(sideStr.ToString())) ? sideStr.ToString() : "left",
                                TaskTime = (!timeStr.IsNullOrEmpty && !string.IsNullOrWhiteSpace(timeStr.ToString())) ? timeStr.ToString() : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                BeamLength = beamLength,
                                BoxId = boxId
                            };

                            // 将任务处理状态置为 1 (DB0)
                            await _taskDb.StringSetAsync("vision_task_processing", "1");
                            Console.WriteLine($"[Redis] vision_task_processing -> 1 (Task {currentFlag} detected)");

                            // 广播任务到达事件。
                            OnTaskTriggered?.Invoke(task);
                            _lastFlag = currentFlag;
                        }
                        else if (currentFlag == 0 && _lastFlag != 0)
                        {
                            // 收到归零信号，重置状态位以待下一次触发。
                            _lastFlag = 0;
                        }
                    }
                    else if (!flagStr.HasValue && _lastFlag != 0)
                    {
                        // 键被外部意外清空时，同步重置内存状态。
                        _lastFlag = 0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Redis Listen Loop Error: {ex.Message}");
                }

                // 优化：减少轮询间隔从100ms到50ms，以降低检测延迟
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// 并发地将检测结果对象的各个字段写入 Redis 排队数据库。
        /// 优化：使用批量管道操作而不是逐个异步写入
        /// </summary>
        /// <param name="result">包含所有指标的结果实体。</param>
        public async Task WriteResultAsync(DetectionResult result)
        {
            if (_resultDb == null || result == null) return;
            try
            {
                var entries = result.ToHashEntries();
                
                // 优化方案：使用管道批量写入，减少网络往返
                var batch = _resultDb.CreateBatch();
                var tasks = new System.Collections.Generic.List<Task>();
                
                foreach (var entry in entries)
                {
                    tasks.Add(batch.StringSetAsync((string)entry.Name, (string)entry.Value));
                }
                
                batch.Execute();
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Write Result Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 业务流转重置：在任务处理结束后，将 Redis 中触发标志位归零。
        /// 注意：vision_task_beam_length 和 vision_task_side 等配置参数保留原值，不进行清空。
        /// </summary>
        public async Task ClearTaskKeysAsync()
        {
            if (_taskDb == null || _redis == null) return;
            try
            {
                // 等待连接完全就绪（最多等待 3 秒）
                int waitCount = 0;
                while (!_redis.IsConnected && waitCount < 30)
                {
                    await Task.Delay(100);
                    waitCount++;
                }

                if (!_redis.IsConnected) return;

                await _taskDb.StringSetAsync("vision_task_flag", "0");
                await _taskDb.StringSetAsync("vision_task_processing", "0");
                Console.WriteLine("[Redis] vision_task_processing -> 0 (Task completed)");
                Console.WriteLine("Redis 任务触发键已成功归零。");
            }
            catch (Exception) { /* 忽略通讯异常 */ }
        }

        /// <summary>
        /// 重置结果数据库 (DB1) 中的执行状态：
        /// 将 result_status 与 scan_completed 置为空字符串，表示当前任务处于未执行/未完成扫描状态。
        /// </summary>
        public async Task ResetExecutionStatusAsync()
        {
            if (_resultDb == null || _redis == null || !_redis.IsConnected) return;
            try
            {
                var batch = _resultDb.CreateBatch();
                var t1 = batch.StringSetAsync("result_status", "");
                var t2 = batch.StringSetAsync("scan_completed", "");
                batch.Execute();
                await Task.WhenAll(t1, t2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Redis DB1] 重置执行状态异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 校验并确保结果数据库 (DB1) 中必备的键已初始化（若不存在则自动创建为空）。
        /// 例如：result_status 与 scan_completed 在未执行/未扫描时为空。
        /// </summary>
        public async Task<bool> EnsureResultKeysAsync(Action<string>? logAction = null)
        {
            var log = logAction ?? ((msg) => OnLogMessage?.Invoke(msg));

            if (_resultDb == null || _redis == null)
            {
                log("⚠️ [Redis DB1] Redis 数据库未初始化，无法检测结果状态键。");
                return false;
            }

            try
            {
                // 等待连接就绪（最多等待 5 秒）
                int waitCount = 0;
                while (!_redis.IsConnected && waitCount < 50)
                {
                    await Task.Delay(100);
                    waitCount++;
                }

                if (!_redis.IsConnected)
                {
                    log("⚠️ [Redis DB1] Redis 尚未连通，暂时无法校验 DB1 必备键。");
                    return false;
                }

                // 1. scan_completed 校验（未触发扫描时为空）
                bool scanExists = await _resultDb.KeyExistsAsync("scan_completed");
                if (!scanExists)
                {
                    await _resultDb.StringSetAsync("scan_completed", "");
                    log("✔ [Redis DB1] 检测到 scan_completed 字段不存在，已自动创建并初始化为空。");
                    Console.WriteLine("[Redis DB1] 检测到 scan_completed 字段不存在，已自动创建并初始化为空。");
                }
                else
                {
                    var val = await _resultDb.StringGetAsync("scan_completed");
                    log($"✔ [Redis DB1] 检测到 scan_completed 字段已存在 (当前值: '{val}')。");
                    Console.WriteLine($"[Redis DB1] 检测到 scan_completed 字段已存在 (当前值: '{val}')。");
                }

                // 2. result_status 校验（未执行时为空）
                bool statusExists = await _resultDb.KeyExistsAsync("result_status");
                if (!statusExists)
                {
                    await _resultDb.StringSetAsync("result_status", "");
                    log("✔ [Redis DB1] 检测到 result_status 字段不存在，已自动创建并初始化为空。");
                    Console.WriteLine("[Redis DB1] 检测到 result_status 字段不存在，已自动创建并初始化为空。");
                }
                else
                {
                    var val = await _resultDb.StringGetAsync("result_status");
                    log($"✔ [Redis DB1] 检测到 result_status 字段已存在 (当前值: '{val}')。");
                    Console.WriteLine($"[Redis DB1] 检测到 result_status 字段已存在 (当前值: '{val}')。");
                }

                return true;
            }
            catch (Exception ex)
            {
                log($"❌ [Redis DB1] 检测/创建结果状态键异常: {ex.Message}");
                Console.WriteLine($"[Redis DB1] EnsureResultKeysAsync 异常: {ex.Message}");
                return false;
            }
        }
    }
}

