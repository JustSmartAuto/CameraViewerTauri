using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CameraHelper;

public class NativeMode
{
	private static void Send(NetworkStream stream, string data)
	{
		byte[] buff = Encoding.Default.GetBytes(data);
		stream.Write(buff, 0, buff.Length);
		stream.Flush();
		Thread.Sleep(200);
	}

	public static bool SetOnline(InSightConfig config)
	{
		try
		{
			if (!IPAddress.TryParse(config.Address, out var adr))
			{
				return false;
			}
			using (Ping ping = new Ping())
			{
				if (ping.Send(config.Address, 200).Status != IPStatus.Success)
				{
					return false;
				}
			}
			using TcpClient client = new TcpClient();
			int sendTimeout = (client.ReceiveTimeout = 1000);
			client.SendTimeout = sendTimeout;
			client.Connect(adr, 23);
			using NetworkStream stream = client.GetStream();
			Send(stream, "admin\r\n");
			Send(stream, "\r\n");
			Send(stream, "SO1\r\n");
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool Reset(InSightConfig config)
	{
		try
		{
			if (!IPAddress.TryParse(config.Address, out var adr))
			{
				return false;
			}
			using (Ping ping = new Ping())
			{
				if (ping.Send(config.Address, 200).Status != IPStatus.Success)
				{
					return false;
				}
			}
			using TcpClient client = new TcpClient();
			int sendTimeout = (client.ReceiveTimeout = 1000);
			client.SendTimeout = sendTimeout;
			client.Connect(adr, 23);
			using NetworkStream stream = client.GetStream();
			Send(stream, "admin\r\n");
			Send(stream, "\r\n");
			Send(stream, "RT\r\n");
			return true;
		}
		catch
		{
			return false;
		}
	}
}
