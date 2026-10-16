using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CameraViewer;
using SciCamera.Net;

namespace CameraViewerGigE
{
    /// <summary>
    /// OPT SciCamera GigE 相机取流：发现设备 → 按序列号匹配 → 打开 → 设置曝光 → 回调取帧 → 转 Bitmap。
    /// 最新一帧由 UI 线程通过 <see cref="TakeLatest"/> 主动拉取，避免跨线程 Marshaling 与帧堆积。
    /// </summary>
    public sealed class GigECamera : IDisposable
    {
        /// <summary>UI 刷新限频（约 10fps），降低高帧率下的图像转换与重绘开销。</summary>
        private const int MinFrameIntervalMs = 100;

        private readonly string serial;
        private readonly int exposureMs;
        private readonly SciCam.fnOnPayloadDelegate payloadCallback;
        private readonly object frameGate = new object();

        private SciCam dev;
        private volatile bool running;
        private IntPtr convertBuffer = IntPtr.Zero;
        private long convertBufferSize;
        private long lastFrameTicks;
        private Bitmap latest;
        private int disposed;

        /// <summary>连接/启动失败时回调（在后台线程触发）。</summary>
        public event Action<string> Failed;

        public GigECamera(string serial, int exposureMs)
        {
            this.serial = (serial ?? "").Trim();
            this.exposureMs = exposureMs;
            payloadCallback = new SciCam.fnOnPayloadDelegate(OnPayload);
        }

        public void Start()
        {
            if (running) return;
            running = true;
            Task.Run(() => Run());
        }

        /// <summary>取走最新一帧（若 UI 未及时取走则直接丢弃），调用方负责释放返回的 Bitmap。</summary>
        public Bitmap TakeLatest()
        {
            lock (frameGate)
            {
                var bmp = latest;
                latest = null;
                return bmp;
            }
        }

        private void Run()
        {
            try
            {
                if (serial.Length == 0)
                {
                    Fail(I18n.T("gigeNoSerial"));
                    return;
                }
                SciCamNative.Configure();
                dev = GigEDeviceCatalog.CreateBySerial(serial, out var error);
                if (dev == null)
                {
                    Fail(error);
                    return;
                }

                var ret = dev.OpenDevice();
                if (ret != SciCam.SCI_CAMERA_OK)
                {
                    Fail(Format("OpenDevice", ret));
                    Cleanup();
                    return;
                }

                // 连续采集：关闭触发模式（失败不致命）
                dev.SetEnumValueByStringEx(SciCam.SciCamDeviceXmlType.SciCam_DeviceXml_Camera, "TriggerMode", "Off");
                ApplyExposure();

                ret = dev.RegisterPayloadCallBack(payloadCallback, IntPtr.Zero, true);
                if (ret != SciCam.SCI_CAMERA_OK)
                {
                    Fail(Format("RegisterPayloadCallBack", ret));
                    Cleanup();
                    return;
                }

                ret = dev.StartGrabbing();
                if (ret != SciCam.SCI_CAMERA_OK)
                {
                    Fail(Format("StartGrabbing", ret));
                    Cleanup();
                    return;
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                Cleanup();
            }
        }

        /// <summary>设置曝光时间（ms → µs）；0 表示保留相机默认值。</summary>
        private void ApplyExposure()
        {
            if (exposureMs <= 0) return;
            double microSeconds = exposureMs * 1000.0;
            // 关闭自动曝光（尽力而为，节点不存在时忽略）
            dev.SetEnumValueByStringEx(SciCam.SciCamDeviceXmlType.SciCam_DeviceXml_Camera, "ExposureAuto", "Off");
            foreach (var node in new[] { "ExposureTime", "ExposureTimeAbs" })
            {
                if (dev.SetFloatValueEx(SciCam.SciCamDeviceXmlType.SciCam_DeviceXml_Camera, node, microSeconds) == SciCam.SCI_CAMERA_OK)
                    return;
            }
            foreach (var node in new[] { "ExposureTimeRaw" })
            {
                if (dev.SetIntValueEx(SciCam.SciCamDeviceXmlType.SciCam_DeviceXml_Camera, node, (long)Math.Round(microSeconds)) == SciCam.SCI_CAMERA_OK)
                    return;
            }
        }

        private void OnPayload(IntPtr payload, IntPtr tag)
        {
            if (!running || payload == IntPtr.Zero) return;
            try
            {
                var attr = new SciCam.SCI_CAM_PAYLOAD_ATTRIBUTE();
                if (SciCam.PayloadGetAttribute(payload, ref attr) != SciCam.SCI_CAMERA_OK) return;
                if (!attr.isComplete) return;
                if (attr.payloadMode != SciCam.SciCamPayloadMode.SciCam_PayloadMode_2D) return;

                int width = (int)attr.imgAttr.width;
                int height = (int)attr.imgAttr.height;
                if (width <= 0 || height <= 0) return;

                long now = Environment.TickCount64;
                if (now - lastFrameTicks < MinFrameIntervalMs) return;
                lastFrameTicks = now;

                IntPtr srcImage = IntPtr.Zero;
                if (SciCam.PayloadGetImage(payload, ref srcImage) != SciCam.SCI_CAMERA_OK || srcImage == IntPtr.Zero) return;

                var srcType = attr.imgAttr.pixelType;
                bool color = IsColorType(srcType);
                var target = color ? SciCam.SciCamPixelType.RGB8 : SciCam.SciCamPixelType.Mono8;

                Bitmap bmp;
                if (srcType == target)
                {
                    bmp = ToBitmap(srcImage, width, height, color);
                }
                else
                {
                    long size = 0;
                    if (SciCam.PayloadConvertImageEx(ref attr.imgAttr, srcImage, target, IntPtr.Zero, ref size, true, 0) != SciCam.SCI_CAMERA_OK) return;
                    if (size <= 0) return;
                    EnsureConvertBuffer(size);
                    long dstSize = size;
                    if (SciCam.PayloadConvertImageEx(ref attr.imgAttr, srcImage, target, convertBuffer, ref dstSize, true, 0) != SciCam.SCI_CAMERA_OK) return;
                    bmp = ToBitmap(convertBuffer, width, height, color);
                }
                Publish(bmp);
            }
            catch { }
        }

        private void Publish(Bitmap bmp)
        {
            if (!running) { bmp.Dispose(); return; }
            lock (frameGate)
            {
                var old = latest;
                latest = bmp;
                old?.Dispose();
            }
        }

        private void EnsureConvertBuffer(long size)
        {
            if (convertBuffer != IntPtr.Zero && convertBufferSize >= size) return;
            if (convertBuffer != IntPtr.Zero) Marshal.FreeHGlobal(convertBuffer);
            convertBuffer = Marshal.AllocHGlobal((int)size);
            convertBufferSize = size;
        }

        /// <summary>彩色格式 → RGB8；其余（Mono 系列）→ Mono8。</summary>
        private static bool IsColorType(SciCam.SciCamPixelType type)
        {
            var name = type.ToString();
            return name.StartsWith("Bayer", StringComparison.Ordinal)
                || name.StartsWith("RGB", StringComparison.Ordinal)
                || name.StartsWith("BGR", StringComparison.Ordinal)
                || name.StartsWith("YUV", StringComparison.Ordinal)
                || name.StartsWith("YCbCr", StringComparison.Ordinal)
                || name.StartsWith("BiColor", StringComparison.Ordinal);
        }

        private static Bitmap ToBitmap(IntPtr src, int width, int height, bool color)
        {
            if (color)
            {
                var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try { CopyRows(data.Scan0, data.Stride, src, width * 3, width * 3, height); }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }

            var mono = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            var palette = mono.Palette;
            for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(255, i, i, i);
            mono.Palette = palette;
            var monoData = mono.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try { CopyRows(monoData.Scan0, monoData.Stride, src, width, width, height); }
            finally { mono.UnlockBits(monoData); }
            return mono;
        }

        /// <summary>逐行拷贝，兼容 Bitmap 的行对齐（Stride）与紧凑源数据。</summary>
        private static void CopyRows(IntPtr dst, int dstStride, IntPtr src, int srcStride, int rowBytes, int height)
        {
            for (int y = 0; y < height; y++)
                RtlMoveMemory(IntPtr.Add(dst, y * dstStride), IntPtr.Add(src, y * srcStride), new UIntPtr((uint)rowBytes));
        }

        private void Fail(string message)
        {
            running = false;
            Failed?.Invoke(string.IsNullOrEmpty(message) ? I18n.T("gigeConnecting") : message);
        }

        private static string Format(string api, uint code) =>
            string.Format(I18n.T("gigeStartFailedFmt"), api + " 0x" + code.ToString("X8"));

        private void Cleanup()
        {
            running = false;
            var d = dev;
            dev = null;
            if (d == null) return;
            try { d.RegisterPayloadCallBack(null, IntPtr.Zero, true); } catch { }
            try { d.StopGrabbing(); } catch { }
            try { d.CloseDevice(); } catch { }
            try { d.DeleteDevice(); } catch { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Cleanup();
            if (convertBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(convertBuffer);
                convertBuffer = IntPtr.Zero;
                convertBufferSize = 0;
            }
            lock (frameGate)
            {
                latest?.Dispose();
                latest = null;
            }
            Failed = null;
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void RtlMoveMemory(IntPtr destination, IntPtr source, UIntPtr length);
    }

    /// <summary>GigE 设备发现与句柄创建（枚举结果全局缓存，同一时刻仅允许一个线程操作 SDK）。</summary>
    internal static class GigEDeviceCatalog
    {
        private static readonly object Gate = new object();
        private static SciCam.SCI_DEVICE_INFO_LIST list;
        private static bool enumerated;

        /// <summary>发现设备并按序列号创建句柄；失败时返回 null 并输出错误信息。</summary>
        public static SciCam CreateBySerial(string serial, out string error)
        {
            lock (Gate)
            {
                var dev = CreateLocked(serial, out error);
                if (dev != null) return dev;
                // 未找到时重新枚举一次，兼容设备上电/网络就绪较慢的场景
                EnumerateLocked();
                return CreateLocked(serial, out error);
            }
        }

        private static SciCam CreateLocked(string serial, out string error)
        {
            error = null;
            EnsureEnumeratedLocked();
            var devices = list.pDevInfo;
            if (devices == null)
            {
                error = string.Format(I18n.T("gigeNotFoundFmt"), serial);
                return null;
            }
            int count = Math.Min((int)list.count, devices.Length);
            for (int i = 0; i < count; i++)
            {
                var gige = new SciCam.SCI_DEVICE_GIGE_INFO();
                if (SciCam.GetGigeInfo(devices[i], ref gige) != SciCam.SCI_CAMERA_OK) continue;
                if (!string.Equals((gige.serialNumber ?? "").Trim(), serial, StringComparison.OrdinalIgnoreCase)) continue;

                var dev = new SciCam();
                var ret = dev.CreateDevice(ref devices[i]);
                if (ret != SciCam.SCI_CAMERA_OK)
                {
                    error = string.Format(I18n.T("gigeStartFailedFmt"), "CreateDevice 0x" + ret.ToString("X8"));
                    return null;
                }
                return dev;
            }
            error = string.Format(I18n.T("gigeNotFoundFmt"), serial);
            return null;
        }

        private static void EnsureEnumeratedLocked()
        {
            if (!enumerated) EnumerateLocked();
        }

        private static void EnumerateLocked()
        {
            list = new SciCam.SCI_DEVICE_INFO_LIST();
            var ret = SciCam.DiscoveryDevices(ref list, (uint)SciCam.SciCamTLType.SciCam_TLType_Gige);
            enumerated = ret == SciCam.SCI_CAMERA_OK;
            if (!enumerated) list = new SciCam.SCI_DEVICE_INFO_LIST();
        }
    }
}
