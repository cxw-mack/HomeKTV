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
                .Select(x=>new{Adapter=x,Properties=x.GetIPProperties()})
                .SelectMany(x=>x.Properties.UnicastAddresses
                    .Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address))
                    .Select(a=>new{Address=a.Address.ToString(),HasGateway=x.Properties.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any)),Type=x.Adapter.NetworkInterfaceType}))
                .OrderByDescending(x=>x.HasGateway)
                .ThenBy(x=>x.Address.StartsWith("169.254.",StringComparison.Ordinal)?1:0)
                .ThenBy(x=>x.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211?0:1)
                .Select(x=>x.Address).FirstOrDefault() ?? "127.0.0.1";
        }
        catch (NetworkInformationException) { return "127.0.0.1"; }
    }

    public static int FindAvailablePort(int preferred, int attempts=20,bool anyIp=false)
    {
        var start=Math.Clamp(preferred,1024,65535);
        var count=Math.Clamp(attempts,1,100);
        for(var offset=0;offset<count&&start+offset<=65535;offset++)
        {
            var port=start+offset;
            if(CanBind(port,anyIp))return port;
        }
        throw new IOException("没有可用的手机点歌端口。");
    }

    private static bool CanBind(int port,bool anyIp)
    {
        if(anyIp)
        {
            try
            {
                using var socket=new Socket(AddressFamily.InterNetworkV6,SocketType.Stream,ProtocolType.Tcp){DualMode=true,ExclusiveAddressUse=true};
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any,port));socket.Listen(1);return true;
            }
            catch(SocketException){return false;}
            catch(NotSupportedException){return CanBindIpv4(port,IPAddress.Any);}
        }
        if(!CanBindIpv4(port,IPAddress.Loopback))return false;
        try
        {
            using var socket=new Socket(AddressFamily.InterNetworkV6,SocketType.Stream,ProtocolType.Tcp){ExclusiveAddressUse=true};
            socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback,port));socket.Listen(1);return true;
        }
        catch(NotSupportedException){return true;}
        catch(SocketException){return false;}
    }

    private static bool CanBindIpv4(int port,IPAddress address)
    {
        try{using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp){ExclusiveAddressUse=true};socket.Bind(new IPEndPoint(address,port));socket.Listen(1);return true;}
        catch(SocketException){return false;}
    }
}
