namespace PlcRecipe.Core;

/// <summary>PLC 品牌/协议类型。</summary>
public enum PlcBrand
{
    /// <summary>西门子 S7（S7-200Smart/1200/1500）</summary>
    Siemens = 0,
    /// <summary>三菱 MC 协议 3E 帧（Q/FX5U）</summary>
    Mitsubishi = 1,
    /// <summary>欧姆龙 FINS TCP</summary>
    Omron = 2,
    /// <summary>Modbus TCP</summary>
    ModbusTcp = 3,
    /// <summary>Modbus RTU（串口）</summary>
    ModbusRtu = 4,
    /// <summary>模拟 PLC（无硬件演示/测试）</summary>
    Mock = 99
}

/// <summary>
/// Modbus 32 位数据的字/字节排列顺序（参考 HslCommunication 的 DataFormat 约定）。
/// 设备字节序列 B0 B1 B2 B3（大端）在不同设备上的实际排列：
/// ABCD=大端(默认) / CDAB=字交换 / BADC=字节交换 / DCBA=小端。
/// </summary>
public enum ModbusDataFormat
{
    /// <summary>ABCD 大端（字内大端、高字在前）——多数 PLC 默认</summary>
    ABCD = 0,
    /// <summary>BADC 字内字节交换</summary>
    BADC = 1,
    /// <summary>CDAB 字交换（低字在前）——常见于部分国产仪表/变频器</summary>
    CDAB = 2,
    /// <summary>DCBA 小端</summary>
    DCBA = 3
}

/// <summary>变量数据类型。</summary>
public enum PlcDataType
{
    Bool = 0,
    Int16 = 1,
    UInt16 = 2,
    Int32 = 3,
    UInt32 = 4,
    Float32 = 5,
    /// <summary>字符串，按 StringWords 个字存储，每字 2 个字符</summary>
    String = 6
}

/// <summary>用户角色（数值越大权限越高）。</summary>
public enum UserRole
{
    Operator = 0,
    Engineer = 1,
    Admin = 2
}

/// <summary>传输方向：上传=PLC→软件；下载=软件→PLC。</summary>
public enum TransferDirection
{
    Upload = 0,
    Download = 1
}

/// <summary>传输触发来源。</summary>
public enum TransferSource
{
    /// <summary>软件界面手动操作</summary>
    Manual = 0,
    /// <summary>PLC 信号联动自动触发</summary>
    SignalTrigger = 1
}

/// <summary>变量读写属性。</summary>
public enum VariableAccess
{
    ReadWrite = 0,
    ReadOnly = 1
}

/// <summary>单台设备传输状态。</summary>
public enum TransferStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failed = 3
}

/// <summary>配方对比条目状态。</summary>
public enum CompareState
{
    Same = 0,
    Different = 1,
    ReadFailed = 2
}
