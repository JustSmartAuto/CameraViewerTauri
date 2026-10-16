using System;
using System.IO;
using System.Runtime.InteropServices;
using SciCamera.Net;

namespace CameraViewerGigE
{
    /// <summary>
    /// OPT SciCamera 原生库定位：把 SDK 原生 DLL 目录加入搜索路径，
    /// 并为 SciCamera.Net 程序集注册 DllImport 解析器，保证 SciCamSDK.dll 及其同级依赖能被找到。
    /// </summary>
    internal static class SciCamNative
    {
        private static readonly object Gate = new object();
        private static bool configured;
        private static string nativeDir;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>原生库目录，未找到时为 null。</summary>
        public static string NativeDir => nativeDir;

        public static void Configure()
        {
            lock (Gate)
            {
                if (configured) return;
                configured = true;
                try
                {
                    nativeDir = FindNativeDir();
                    if (nativeDir == null) return;
                    // 1) 让 SciCamSDK.dll 的静态依赖（GenICam 等）也能被解析
                    SetDllDirectory(nativeDir);
                    // 2) 显式把 P/Invoke 目标映射到原生库目录
                    NativeLibrary.SetDllImportResolver(typeof(SciCam).Assembly, Resolve);
                }
                catch { /* 原生库缺失时仅影响 GigE 模式，HTTP 模式仍可用 */ }
            }
        }

        private static IntPtr Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (nativeDir == null) return IntPtr.Zero;
            var candidate = Path.Combine(nativeDir, libraryName);
            if (File.Exists(candidate)) return NativeLibrary.Load(candidate);
            return IntPtr.Zero;
        }

        private static string FindNativeDir()
        {
            // 部署形态：启动器释放到 <exe目录>\SciCameraSDK\Runtime\Win64_x64
            var deployed = Path.Combine(AppContext.BaseDirectory, "SciCameraSDK", "Runtime", "Win64_x64");
            if (Directory.Exists(deployed)) return deployed;

            // 开发形态：从 bin 目录逐级向上查找 opt-camera-sdk\Runtime\Win64_x64
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var dev = Path.Combine(dir.FullName, "opt-camera-sdk", "Runtime", "Win64_x64");
                if (Directory.Exists(dev)) return dev;
            }
            return null;
        }
    }
}
