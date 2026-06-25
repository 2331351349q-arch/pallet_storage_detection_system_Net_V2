using System.Threading.Tasks;

namespace pallet_storage_detection_system_Net_V2.Devices
{
    /// <summary>
    /// 通用相机设备接口，抽象了不同厂商（海康、图漾等）的底层实现差异。
    /// </summary>
    public interface ICameraDevice
    {
        /// <summary>
        /// 相机的唯一序列号。
        /// </summary>
        string SerialNumber { get; }

        /// <summary>
        /// 当前是否已成功连接并初始化。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 当前是否处于持续抓图/预览状态。
        /// </summary>
        bool IsCapturing { get; }

        /// <summary>
        /// 打开设备连接。
        /// </summary>
        /// <returns>连接结果。</returns>
        bool Connect();

        /// <summary>
        /// 断开设备连接并释放资源。
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 启动预览抓取流。
        /// </summary>
        /// <returns>启动结果。</returns>
        bool StartGrabbing();

        /// <summary>
        /// 停止提交抓取请求。
        /// </summary>
        void StopGrabbing();
        
        /// <summary>
        /// 异步主动触发一次抓拍，并返回图像对象。
        /// </summary>
        /// <returns>抓取的图像实体（通常为 Bitmap 或 Mat 对象）。</returns>
        Task<object> GrabFrameAsync();
    }
}

