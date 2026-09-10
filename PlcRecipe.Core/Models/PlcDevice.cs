using PlcRecipe.Core;

namespace PlcRecipe.Core.Models;

/// <summary>PLC 设备：软件的最顶层业务实体，品牌/连接参数/信号联动配置都在设备上。</summary>
public class PlcDevice
{
    public int Id { get; set; }
    /// <summary>设备名，如 "1#机"</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>PLC 品牌/协议（决定地址语法与驱动）</summary>
    public PlcBrand Brand { get; set; }

    /// <summary>IP 地址（TCP 类协议）</summary>
    public string Ip { get; set; } = "192.168.0.1";
    /// <summary>端口：S7=102 / MC=6000 / FINS=9600 / ModbusTcp=502</summary>
    public int Port { get; set; } = 502;
    /// <summary>西门子机架号</summary>
    public int Rack { get; set; } = 0;
    /// <summary>西门子槽号</summary>
    public int Slot { get; set; } = 0;
    /// <summary>S7 型号：S7200Smart / S7300 / S7400 / S71200 / S71500（默认 S71200）</summary>
    public string S7CpuType { get; set; } = "S71200";
    /// <summary>Modbus 从站号</summary>
    public byte SlaveId { get; set; } = 1;
    /// <summary>Modbus 32 位数据字节序（ABCD/BADC/CDAB/DCBA），仅 Modbus 设备使用</summary>
    public ModbusDataFormat DataFormat { get; set; } = ModbusDataFormat.ABCD;
    /// <summary>Modbus RTU 串口号，如 COM3</summary>
    public string? SerialPortName { get; set; }
    public int BaudRate { get; set; } = 9600;
    /// <summary>校验位 None/Odd/Even</summary>
    public string Parity { get; set; } = "None";
    public int DataBits { get; set; } = 8;
    /// <summary>停止位 One/Two</summary>
    public string StopBits { get; set; } = "One";

    public bool Enabled { get; set; } = true;

    // ---- 信号联动配置（PLC 主动发起上传/下载）----
    /// <summary>是否启用信号联动</summary>
    public bool SignalEnabled { get; set; }
    /// <summary>下载请求位地址（PLC 置位发起下载）</summary>
    public string? DownloadRequestAddress { get; set; }
    /// <summary>上传请求位地址（PLC 置位发起上传）</summary>
    public string? UploadRequestAddress { get; set; }
    /// <summary>配方名字符串起始地址（字）：PLC 写入配方名，软件按名字字符匹配配方；上传时同名不存在则自动创建</summary>
    public string? RecipeNameAddress { get; set; }
    /// <summary>配方名字符串字数（每字 2 字节），默认 8 字 = 16 字节</summary>
    public int RecipeNameWords { get; set; } = 8;
    /// <summary>配方标识地址（字）：下载成功后写入"配方名|v版本"，供产线/MES 核对设备在用配方（水印）</summary>
    public string? RecipeTagAddress { get; set; }
    /// <summary>配方标识字数（每字 2 字节），默认 12 字 = 24 字节</summary>
    public int RecipeTagWords { get; set; } = 12;
    /// <summary>完成位地址（软件执行成功后置位）</summary>
    public string? DoneBitAddress { get; set; }
    /// <summary>失败位地址（软件执行失败后置位）</summary>
    public string? FailBitAddress { get; set; }
    /// <summary>信号轮询间隔（毫秒），默认 1000</summary>
    public int PollIntervalMs { get; set; } = 1000;

    public string? Remark { get; set; }

    /// <summary>该品牌默认通讯端口。</summary>
    public static int DefaultPort(PlcBrand brand) => brand switch
    {
        PlcBrand.Siemens => 102,
        PlcBrand.Mitsubishi => 6000,
        PlcBrand.Omron => 9600,
        PlcBrand.ModbusTcp => 502,
        _ => 502
    };
}
