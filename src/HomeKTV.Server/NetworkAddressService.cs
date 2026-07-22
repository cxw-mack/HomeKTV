using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HomeKTV.Server;

public static class NetworkAddressService
{
    public static string GetPreferredLanAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x=>x.OperationalStatus==OperationalStatus.Up && x.NetworkInterfaceType!=NetworkInterfaceType.Loopback && x.NetworkInterfaceType!=NetworkInterfaceType.Tunnel)
                .SelectMany(x=>x.GetIPProperties().UnicastAddresses)
                .Where(x=>x.Address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address))
                .OrderBy(x=>x.Address.ToString().StartsWith("169.254.",StringComparison.Ordinal)?1:0)
                .Select(x=>x.Address.ToString()).FirstOrDefault() ?? "127.0.0.1";
        }
        catch (NetworkInformationException) { return "127.0.0.1"; }
    }

    public static int FindAvailablePort(int preferred, int attempts=20)
    {
        for(var port=Math.Clamp(preferred,1024,65535);port<=65535 && port<preferred+attempts;port++)
        {
            try { var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();listener.Stop();return port; }
            catch(SocketException) { }
        }
        throw new IOException("没有可用的手机点歌端口。\n");
    }
}

