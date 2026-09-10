using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PlcRecipe.Core.Interfaces;

namespace PlcRecipe.Server.Hubs;

/// <summary>
/// PLC 状态推送 Hub：连接管理器的 StateChanged 事件实时广播到所有客户端。
/// 客户端监听事件名 "deviceStateChanged"，负载为 ConnectionStateChangedEventArgs
/// （DeviceId / DeviceName / State / Message）。
/// 鉴权：连接必须携带与 settings.ApiKey 一致的 ?key= 查询参数（ApiKey 为空时推送同样关闭），
/// 否则任何可达主机都能持续收到设备名/状态/故障信息，为攻击提供侦察。
/// </summary>
public sealed class PlcStateHub : Hub
{
    public async Task SubscribeDevice(int deviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));
    }

    public async Task UnsubscribeDevice(int deviceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));
    }

    public static string DeviceGroup(int deviceId) => $"plc-device-{deviceId}";

    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var apiKey = http?.RequestServices.GetService<ISettingsService>()?.Settings.ApiKey ?? "";
        var provided = http?.Request.Query[ApiKeyGuard.QueryKeyName].ToString() ?? "";
        if (string.IsNullOrEmpty(apiKey) || !ApiKeyGuard.Equals(provided, apiKey))
            throw new HubException("未授权");
        return base.OnConnectedAsync();
    }
}
