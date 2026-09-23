using System;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using Script.Methods;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

/// <summary>
/// VisionMaster 脚本：将图像与测量数据通过 MQTT 发送到相机显示软件。
///
/// 脚本输入（请在 VM 脚本模块中按此顺序绑定）：
///   in0  IMAGE       图像
///   in1  INT         配方号 ProductNumber
///   in2  INT         面号 SurfaceNumber
///   in3  POINT       测量点（取第一个点的 PointX/PointY）
///   in4  DOUBLE      对称度 Symmetry
///   in5  DOUBLE      高度 ProductHeight
///   in6  DOUBLE      宽度 ProductWidth
///
/// 依赖引用（需在 VM 脚本模块的 DLL 引用中添加）：
///   - OpenCvSharp.dll
///   - M2Mqtt.Net.dll
///
/// 发布话题（与 CameraViewerAutoWeld 软件默认配置一致）：
///   /autoweld/measure/image/raw
///   /autoweld/measure/data/raw
///   /autoweld/measure/logs/raw
/// </summary>
public partial class UserScript : ScriptMethods, IProcessMethods
{
    // 执行次数计数
    int processCount;

    /// <summary>MQTT broker 地址</summary>
    const string MqttHost = "127.0.0.1";

    /// <summary>MQTT broker 端口</summary>
    const int MqttPort = 8907;

    /// <summary>图像话题</summary>
    const string TopicImage = "/autoweld/measure/image/raw";

    /// <summary>测量数据话题</summary>
    const string TopicData = "/autoweld/measure/data/raw";

    /// <summary>日志话题</summary>
    const string TopicLog = "/autoweld/measure/logs/raw";

    /// <summary>
    /// 预编译时变量初始化
    /// </summary>
    public void Init()
    {
        processCount = 0;
    }

    /// <summary>
    /// 流程执行一次进入 Process 函数
    /// </summary>
    /// <returns>推送成功返回 true，否则返回 false</returns>
    public bool Process()
    {
        processCount++;

        // 读取并校验图像输入
        ImageData img = in0;
        if (img == null || img.Buffer == null || img.Buffer.Length == 0)
        {
            ConsoleWrite("图像输入 in0 为空");
            return false;
        }

        // 读取配方号、面号
        int productNumber = in1;
        int surfaceNumber = in2;

        // 读取测量点（POINT 类型在 VM 中为 PointData[]，取第一个点）
        PointData[] pts = in3;
        if (pts == null || pts.Length == 0)
        {
            ConsoleWrite("点输入 in3 为空");
            return false;
        }
        float pointX = pts[0].PointX;
        float pointY = pts[0].PointY;

        // 读取对称度、高度、宽度
        double symmetry = in4;
        double productHeight = in5;
        double productWidth = in6;

        try
        {
            using (Mat mat = ImageDataToMat(img))
            {
                if (mat == null || mat.Empty())
                {
                    ConsoleWrite("图像转换 Mat 失败");
                    return false;
                }

                // 编码为 JPEG 并转 base64
                byte[] jpeg = null;
                if (!Cv2.ImEncode(".jpg", InputArray.Create(mat), out jpeg, new int[0])
                    || jpeg == null || jpeg.Length == 0)
                {
                    ConsoleWrite("JPEG 编码失败");
                    return false;
                }

                string imageBase64 = Convert.ToBase64String(jpeg);
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

                // 连接 MQTT 并发布三类消息
                MqttClient client = new MqttClient(MqttHost, MqttPort, false, null, null, MqttSslProtocols.None);
                string clientId = string.Format("vm-pub-{0}-{1}", processCount, Guid.NewGuid().ToString("N").Substring(0, 8));
                client.Connect(clientId);

                Publish(client, TopicImage, BuildImageXml(timestamp, img.Width, img.Height, productNumber, surfaceNumber, imageBase64));
                Publish(client, TopicData, BuildDataXml(timestamp, img.Width, img.Height, productNumber, surfaceNumber, pointX, pointY, productWidth, productHeight, symmetry));
                Publish(client, TopicLog, BuildLogXml(timestamp, img.Width, img.Height, productNumber, surfaceNumber));

                client.Disconnect();
                ConsoleWrite(string.Format("已推送 配方{0} 面{1} 到 MQTT", productNumber, surfaceNumber));
                return true;
            }
        }
        catch (Exception ex)
        {
            ConsoleWrite("MQTT 推送异常: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 发布 MQTT 消息
    /// </summary>
    void Publish(MqttClient client, string topic, string payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        client.Publish(topic, bytes, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
    }

    /// <summary>
    /// 组装图像 XML
    /// </summary>
    string BuildImageXml(string timestamp, int width, int height, int productNumber, int surfaceNumber, string base64)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<ImageMessage>");
        sb.AppendLine("  <TimeStamp>" + timestamp + "</TimeStamp>");
        sb.AppendLine("  <ImageHeight>" + height + "</ImageHeight>");
        sb.AppendLine("  <ImageWidth>" + width + "</ImageWidth>");
        sb.AppendLine("  <ProductNumber>" + productNumber + "</ProductNumber>");
        sb.AppendLine("  <SurfaceNumber>" + surfaceNumber + "</SurfaceNumber>");
        sb.AppendLine("  <ImageData>" + base64 + "</ImageData>");
        sb.AppendLine("</ImageMessage>");
        return sb.ToString();
    }

    /// <summary>
    /// 组装测量数据 XML
    ///
    /// 以输入点 (cx, cy) 为中心，根据宽/高推算出左右顶点与基线端点，
    /// 与 CameraViewerAutoWeld 的默认脚本/示例脚本坐标系保持一致。
    /// </summary>
    string BuildDataXml(string timestamp, int width, int height, int productNumber, int surfaceNumber,
        float cx, float cy, double w, double h, double symmetry)
    {
        double halfW = w / 2.0;
        double halfH = h / 2.0;

        double leftX = cx - halfW;
        double leftY = cy;
        double rightX = cx + halfW;
        double rightY = cy;
        double topX = cx;
        double topY = cy - halfH;
        double baseLineY = cy + halfH;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<DataMessage>");
        sb.AppendLine("  <TimeStamp>" + timestamp + "</TimeStamp>");
        sb.AppendLine("  <ImageHeight>" + height + "</ImageHeight>");
        sb.AppendLine("  <ImageWidth>" + width + "</ImageWidth>");
        sb.AppendLine("  <ProductNumber>" + productNumber + "</ProductNumber>");
        sb.AppendLine("  <SurfaceNumber>" + surfaceNumber + "</SurfaceNumber>");
        sb.AppendLine("  <BaseLineLeftPointX>" + leftX.ToString("F2") + "</BaseLineLeftPointX>");
        sb.AppendLine("  <BaseLineLeftPointY>" + baseLineY.ToString("F2") + "</BaseLineLeftPointY>");
        sb.AppendLine("  <BaseLineRightPointX>" + rightX.ToString("F2") + "</BaseLineRightPointX>");
        sb.AppendLine("  <BaseLineRightPointY>" + baseLineY.ToString("F2") + "</BaseLineRightPointY>");
        sb.AppendLine("  <LeftPointX>" + leftX.ToString("F2") + "</LeftPointX>");
        sb.AppendLine("  <LeftPointY>" + leftY.ToString("F2") + "</LeftPointY>");
        sb.AppendLine("  <RightPointX>" + rightX.ToString("F2") + "</RightPointX>");
        sb.AppendLine("  <RightPointY>" + rightY.ToString("F2") + "</RightPointY>");
        sb.AppendLine("  <ProductWidth>" + w.ToString("F2") + "</ProductWidth>");
        sb.AppendLine("  <TopPointX>" + topX.ToString("F2") + "</TopPointX>");
        sb.AppendLine("  <TopPointY>" + topY.ToString("F2") + "</TopPointY>");
        sb.AppendLine("  <ProductHeight>" + h.ToString("F4") + "</ProductHeight>");
        sb.AppendLine("  <Symmetry>" + symmetry.ToString("F4") + "</Symmetry>");
        sb.AppendLine("</DataMessage>");
        return sb.ToString();
    }

    /// <summary>
    /// 组装日志 XML
    /// </summary>
    string BuildLogXml(string timestamp, int width, int height, int productNumber, int surfaceNumber)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<LogMessage>");
        sb.AppendLine("  <TimeStamp>" + timestamp + "</TimeStamp>");
        sb.AppendLine("  <ImageHeight>" + height + "</ImageHeight>");
        sb.AppendLine("  <ImageWidth>" + width + "</ImageWidth>");
        sb.AppendLine("  <ProductNumber>" + productNumber + "</ProductNumber>");
        sb.AppendLine("  <SurfaceNumber>" + surfaceNumber + "</SurfaceNumber>");
        sb.AppendLine("  <Message>检测完成</Message>");
        sb.AppendLine("</LogMessage>");
        return sb.ToString();
    }

    /// <summary>
    /// ImageData 转 OpenCvSharp Mat（支持 MONO8 与 RGB24）
    /// </summary>
    /// <param name="img">VM ImageData 对象</param>
    /// <returns>OpenCvSharp Mat；不支持的像素格式返回 null 或空 Mat</returns>
    Mat ImageDataToMat(ImageData img)
    {
        Mat matImage = new Mat();

        if (ImagePixelFormate.MONO8 == img.PixelFormat)
        {
            matImage = Mat.Zeros(img.Height, img.Width, MatType.CV_8UC1);
            Marshal.Copy(img.Buffer, 0, matImage.Ptr(0), img.Buffer.Length);
        }
        else if (ImagePixelFormate.RGB24 == img.PixelFormat)
        {
            matImage = Mat.Zeros(img.Height, img.Width, MatType.CV_8UC3);
            Marshal.Copy(img.Buffer, 0, matImage.Ptr(0), img.Buffer.Length);
            Cv2.CvtColor(matImage, matImage, ColorConversionCodes.RGB2BGR);
        }

        return matImage;
    }
}
