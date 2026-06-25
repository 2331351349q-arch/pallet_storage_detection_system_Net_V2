using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;

namespace pallet_storage_detection_system_Net_V2
{
    /// <summary>
    /// 应用程序入口类，负责底层运行环境准备与库加载路径注入。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 导入 Win32 API 用于显式指定 DLL 搜索路径。
        /// 在 .NET 8 中，P/Invoke 不会自动搜索 exe 所在目录的原生 DLL。
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// 应用程序的主入口点。
        /// 包含针对海康与图漾 SDK 的原生库搜索路径注入逻辑。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 运行环境解析：定位项目内部 libs 目录下的原生驱动。
            string baseDir = AppContext.BaseDirectory;
            
            // 为了解决多厂商（海康和图漾）原生 DLL 相互调用的依赖问题，将它们的 Native 目录加入 PATH 搜索链。
            string percipioNativePath = Path.Combine(baseDir, "libs", "Percipio", "Native");
            string hikvisionNativePath = Path.Combine(baseDir, "libs", "Hikvision", "Native");
            
            // 获取并扩展系统的 PATH 变量，用于 P/Invoke 的间接加载。
            string? currentPath = Environment.GetEnvironmentVariable("PATH");
            // 将专用库路径置于首位，确保最高优先级。
            string newPath = $"{percipioNativePath};{hikvisionNativePath};{currentPath}";
            Environment.SetEnvironmentVariable("PATH", newPath);

            // 同时显式告诉 Win32 内核搜索程序根目录以获取依赖项。
            SetDllDirectory(baseDir);

            // 强行拦截程序集加载事件，专门解决 Percipio SDK 与系统 System.Text.Json 版本的冲突。
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                // 如果发现 SDK 需要加载 System.Text.Json 库。
                if (args.Name.Contains("System.Text.Json"))
                {
                    string assemblyPath = Path.Combine(baseDir, "System.Text.Json.dll");
                    if (File.Exists(assemblyPath))
                    {
                        // 强制加载项目下的物理文件，确保 SDK 引用的是 9.0.0+ 版本。
                        return Assembly.LoadFrom(assemblyPath);
                    }
                }
                return null;
            };

            // 初始化 WinForms 默认配置并运行主界面。new MainForm()先执行构造函数、Application.Run展现UI触发mainForm的MainForm_Load
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
