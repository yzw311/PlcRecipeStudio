using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers.Mitsubishi;
using PlcRecipe.Drivers.Mock;
using PlcRecipe.Drivers.Modbus;
using PlcRecipe.Drivers.Omron;

namespace PlcRecipe.Drivers;

/// <summary>按设备配置创建对应品牌的 PLC 客户端。</summary>
public interface IPlcClientFactory
{
    IPlcClient Create(PlcDevice device);
}

public sealed class PlcClientFactory : IPlcClientFactory
{
    public IPlcClient Create(PlcDevice device) => device.Brand switch
    {
        PlcBrand.Siemens => new S7PlcClient(device.Id, device),
        PlcBrand.Mitsubishi => new MelsecMc3EClient(device.Id, device.Ip, device.Port),
        PlcBrand.Omron => new OmronFinsTcpClient(device.Id, device.Ip, device.Port),
        PlcBrand.ModbusTcp => new ModbusTcpPlcClient(device.Id, device),
        PlcBrand.ModbusRtu => new ModbusRtuPlcClient(device.Id, device),
        PlcBrand.Mock => new MockPlcClient(device.Id, device.Name),
        _ => throw new NotSupportedException($"不支持的品牌 {device.Brand}")
    };
}
