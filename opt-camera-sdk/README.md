# OPT SciCamera 相机 SDK

奥普特（OPT）工业相机二次开发包（SciCamera），基于 **GenICam / GenTL** 标准实现，提供 C# 托管封装与原生运行时，支持 GigE Vision、USB3 Vision、CameraLink、CoaXPress 等接口相机的设备发现、参数配置与图像采集。

> 本目录仅为随项目携带的 SDK 分发副本，供 `CameraViewerGigE` 主程序参考/集成使用。

## 一、目录结构

| 路径 | 说明 |
|---|---|
| `SciCamera.Net.dll` | 托管 API 程序集（.NET 封装，命名空间 `SciCamera.Net`） |
| `SciCamera.Net.xml` | 中英双语 API 文档（XML Doc），含全部类型/方法/枚举说明与错误码表 |
| `Runtime/Win64_x64/` | 64 位原生运行时（部署目标为 64 位程序时使用） |
| `Runtime/Win32_x86/` | 32 位原生运行时 |
| `Samples_CSharp/` | C# 示例工程（VS2013 / 2015 / 2019 / 2022 解决方案） |

### 原生运行时关键库（`Runtime/<平台>/`）

| 类别 | 文件 | 说明 |
|---|---|---|
| SDK 主体 | `SciCamSDK.dll` | 相机采集 SDK 核心 |
| GenICam 桥接 | `OPTGeniCam.dll` | GenICam 标准对接层 |
| GenTL 采集器 | `GEVTLOPT.cti` / `U3VTLOPT.cti` / `CLTLOPT.cti` | 分别为 GigE Vision / USB3 Vision / CameraLink 传输层（Transport Layer） |
| GenICam 栈 | `GCBase_*` / `GenApi_*` / `GenCP_*` / `CLProtocol_*` / `XmlParser_*` / `NodeMapData_*` | 节点映射、XML 解析、协议栈 |
| 图像处理 | `SciCamImgConvert.dll`、`AHD/BayerToRgb_AHD.dll` | 像素格式转换、Bayer 转 RGB |
| 扩展模块 | `SciBridgeModule/`（`SciCore` / `SciImgProc` / `SciObjDetect` + OpenCV） | 图像处理/目标检测桥接 |
| 依赖库 | `libusb0/ libusbK`、`tbb12`、`msvcp140/vcruntime140`、`log4cpp` 等 | USB、并行计算与 C++ 运行时 |

## 二、运行环境与部署

- **托管程序集**：`SciCamera.Net.dll`，项目引用即可（示例为 .NET Framework，VS2013–VS2022 均可）。
- **原生运行时**：将对应平台的 `Runtime/Win64_x64`（或 `Win32_x86`）目录下的全部 DLL/CTI **随主程序一起发布**（与主程序 exe 相邻或加入搜索路径）。
- **GenTL 发现**：SDK 通过 CTI 文件发现传输层，需保证 `.cti` / GenICam 相关库可被定位；必要时设置 `GENICAM_GENTL64_PATH` 指向 `Runtime/Win64_x64`。
- **日志**：可调用 `SciCam.SetSDKLogPath("绝对路径")` 指定 SDK 日志输出目录。

## 三、API 概览

核心类 `SciCamera.Net.SciCam`，区分**静态方法**（SDK 级）与**实例方法**（设备句柄级）。

### 静态方法（SDK 级）

| 方法 | 说明 |
|---|---|
| `GetSDKVersion()` | 返回 4 字节版本号（主/次/修正/测试，各 8bit） |
| `SetSDKLogPath(string)` | 设置 SDK 日志输出目录 |
| `DiscoveryDevices(ref SCI_DEVICE_INFO_LIST, uint tlType)` | 搜索设备；`tlType=0` 搜索全部，或按 `SciCamTLType` 组合过滤 |
| `ByteToStruct(byte[], Type)` | 结构体反序列化（用于解析设备信息） |
| `PayloadGetAttribute(IntPtr, ref SCI_CAM_PAYLOAD_ATTRIBUTE)` | 获取帧属性（宽高、像素格式、帧号、时间戳等） |
| `PayloadLP3DGetMeta(IntPtr, ref SCI_CAM_LP3D_META)` | 获取 3D 轮廓帧元数据 |

### 实例方法（设备句柄级）

| 分类 | 方法 |
|---|---|
| 生命周期 | `CreateDevice(ref SCI_DEVICE_INFO)`、`DeleteDevice()`、`OpenDevice()`、`CloseDevice()`、`IsDeviceOpen()` |
| CameraLink | `CL_OpenCam()`、`CL_CloseCam()`、`CL_IsCamOpen()` |
| 采集参数 | `SetGrabTimeout/GetGrabTimeout`、`SetGrabStrategy/GetGrabStrategy`、`SetGrabBufferCount/GetGrabBufferCount` |
| 取流 | `StartGrabbing()`、`StopGrabbing()`、`Grab(ref IntPtr payload)`、`FreePayload(IntPtr)`、`RegisterPayloadCallBack(fnOnPayloadDelegate, tag, ...)` |
| 节点读写 | `Get/SetIntValue`、`...FloatValue`、`...BoolValue`、`...StringValue`、`...EnumValue`、`SetEnumValueByString`、`SetCommandValue` |
| 节点读写（指定 XML 类型） | 上述各方法的 `...Ex(SciCamDeviceXmlType, ...)` 变体、`GetNodes/GetNodesEx` |

### 主要枚举

| 枚举 | 取值 |
|---|---|
| `SciCamTLType` | Gige、Usb3、CL、CXP、IIDC、UVC、CLHS、Ethernet、PCI、CL_CAM_ONLY |
| `SciCamDeviceType` | 2D、3DLP |
| `SciCamGrabStrategy` | OneByOne、Latest、Upcoming |
| `SciCamDeviceXmlType` | Camera、Card、TL、IF、DS |
| `SciCamNodeType` | Bool / Int / Float / Enum / Cmd / String / Category / Register |
| `SciCamPayloadMode` | 2D、LP3D_Image、LP3D_Contour、LP3D_BatchContour、ACC |

### 主要结构体

`SCI_DEVICE_INFO_LIST`、`SCI_DEVICE_INFO`（及 `SCI_DEVICE_GIGE_INFO` / `USB3` / `CL` / `CXP`）、`SCI_CAM_PAYLOAD_ATTRIBUTE`（含 `imgAttr`）、`SCI_CAM_IMAGE_ATTRIBUTE`、`SCI_NODE_VAL_INT/FLOAT/STRING/ENUM`、`SCI_CAM_NODE`、`SCI_CAM_EVENT`、`SCI_CAM_CHUNK(_LIST)`、`SCI_CAM_LP3D_META`、`SCI_CAM_SL3D_*`。

## 四、典型采集流程（GigE）

```csharp
using SciCamera.Net;

var devList = new SciCam.SCI_DEVICE_INFO_LIST();
// 1. 枚举 GigE 设备
uint ret = SciCam.DiscoveryDevices(ref devList, (uint)SciCam.SciCamTLType.SciCam_TLType_Gige);

// 2. 选择设备 → 创建 / 打开
var dev = new SciCam();
dev.CreateDevice(ref devList.pDevInfo[index]);
dev.OpenDevice();

// 3. 参数配置（GenICam 节点，示例：关闭触发）
dev.SetEnumValueByStringEx(SciCam.SciCamDeviceXmlType.SciCam_DeviceXml_Camera, "TriggerMode", "Off");

// 4. 取流：轮询 Grab 或注册回调
dev.StartGrabbing();
IntPtr payload = IntPtr.Zero;
if (dev.Grab(ref payload) == SciCam.SCI_CAMERA_OK)
{
    var attr = new SciCam.SCI_CAM_PAYLOAD_ATTRIBUTE();
    SciCam.PayloadGetAttribute(payload, ref attr);   // attr.imgAttr.width / height / pixelType
    dev.FreePayload(payload);                        // 用完必须释放
}
dev.StopGrabbing();

// 5. 关闭 / 销毁
dev.CloseDevice();
dev.DeleteDevice();
```

> 回调方式：`RegisterPayloadCallBack(fnOnPayloadDelegate, IntPtr.Zero, true)` 注册，回调内同样使用 `PayloadGetAttribute` 后 `FreePayload`。

## 五、示例工程

| 工程 | 说明 |
|---|---|
| `BaseDemo` | WinForms 综合示例（枚举/开关/取流/节点） |
| `BaseDemoConsole` | 控制台全功能演示，含完整菜单、节点读写、错误码解析（见 `SDKOperate.cs`） |
| `GrabImage` | 最简取流：线程循环 `Grab` + 帧属性打印 |
| `Grab_Callback` / `BaseDemo_Grab_Callback` | 回调方式取图 |
| `GrabStrategies` | 取流策略（OneByOne / Latest / Upcoming）演示 |
| `MultipleCameras` | 多相机同时采集 |
| `Events` | 设备事件回调 |
| `ChunkData` | Chunk 数据解析 |
| `ConvertPixelType` | 像素格式转换 |
| `ParametrizeCamera_LoadAndSave` | 相机参数（Feature）加载/保存 |
| `BaseDemo_CL` | CameraLink 相机示例 |
| `BaseDemo_LP_GrabContour` / `BaseDemo_LP_GrabBatchContour` / `BaseDemo_SL_GrabContour` | 3D 轮廓相机（LP/SL）示例 |

解决方案：`Samples_CSharp/Sample_VS2022.sln`（另有 VS2013/2015/2019 版本）。

## 六、与 `CameraViewerGigE` 主程序的关系

- 主程序 `CameraViewerGigE` 目前是**网页查看器**：通过 WebView2 内嵌加载相机 HTTP/MJPG/RTSP 的 HMI 页面。
- 本 SDK 是**原生取图 SDK**：把图像帧抓到进程内存，由 `Grab` / 回调以 `Payload` 指针交出；不提供 URL/流地址。
- 若要在主程序中接入本 SDK，需要新增一种「SDK 采集」模式：枚举 GigE 相机 → `Grab` 回调 → 依据 `imgAttr` 将 `payload` 转为位图 → 绘制到 PictureBox（而非加载网页）。

## 七、备注

- 完整类型与方法说明、错误码释义请查阅 `SciCamera.Net.xml`。
- `Runtime/` 下各文件为 SDK 官方运行时，请勿单独修改或删除。
