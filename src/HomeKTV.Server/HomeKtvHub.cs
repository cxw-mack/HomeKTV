using Microsoft.AspNetCore.SignalR;

namespace HomeKTV.Server;

public sealed class HomeKtvHub : Hub
{
    public override Task OnConnectedAsync()
    {
        Context.Items["connectedAt"] = DateTimeOffset.UtcNow;
        return base.OnConnectedAsync();
    }
}

