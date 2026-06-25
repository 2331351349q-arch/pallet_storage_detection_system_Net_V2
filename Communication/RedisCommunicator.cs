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
                    ConnectTimeout = 5000
                };

                _redis = ConnectionMultiplexer.Connect(options);
                _taskDb = _redis.GetDatabase(taskDbIndex);
                _resultDb = _redis.GetDatabase(resultDbIndex);

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
            while (_isListening)
            {
                try
                {
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

                            var task = new TaskData
                            {
                                Flag = currentFlag,
                                Side = sideStr.HasValue ? sideStr.ToString() : "left",
                                TaskTime = timeStr.HasValue ? timeStr.ToString() : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            };

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
        /// 业务流转重置：在任务处理结束后，将 Redis 中所有相关的触发指令键位恢复初始状态。
        /// </summary>
        public async Task ClearTaskKeysAsync()
        {
            if (_taskDb == null) return;
            try
            {
                await _taskDb.StringSetAsync("vision_task_flag", "0");
                await _taskDb.StringSetAsync("vision_task_side", "");
                await _taskDb.StringSetAsync("vision_task_time", "");
                Console.WriteLine("Redis 任务触发键已成功被初始化/清空。");
            }
            catch (Exception) { /* 忽略通讯异常 */ }
        }
    }
}

