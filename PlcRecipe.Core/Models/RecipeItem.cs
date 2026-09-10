using System.ComponentModel;
using System.Runtime.CompilerServices;
using PlcRecipe.Core;

namespace PlcRecipe.Core.Models;

/// <summary>
/// 配方数据行：一行 = 一个要上传/下载的参数（定义 + 值一体）。
/// 在配方里直接维护变量名、PLC 地址、类型和当前配方值。
/// Name/Address/Value 实现 INPC：UI 编辑后行级校验（AddressError/ValueError）能即时刷新。
/// </summary>
public class RecipeItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    private string _name = string.Empty;
    /// <summary>变量名，如 "加热温度"</summary>
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnChanged(); } }
    }

    private string _address = string.Empty;
    /// <summary>PLC 地址，语法随设备品牌，如 DB1.DBW0 / D100 / HR40001</summary>
    public string Address
    {
        get => _address;
        set { if (_address != value) { _address = value; OnChanged(); } }
    }

    public PlcDataType DataType { get; set; }
    /// <summary>String 类型占用的字数（每字 2 字符），默认 8 字=16 字符</summary>
    public int StringWords { get; set; } = 8;
    /// <summary>工程单位，如 ℃ / mm / rpm</summary>
    public string? Unit { get; set; }
    public VariableAccess Access { get; set; } = VariableAccess.ReadWrite;
    public int SortOrder { get; set; }
    public string? Remark { get; set; }

    private string _value = string.Empty;
    /// <summary>配方值（字符串存储，按类型解析校验）</summary>
    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; OnChanged(); } }
    }

    /// <summary>参数下限（数值型变量；空 = 不设限）——MES 质量门槛的基础字段</summary>
    public double? LowerLimit { get; set; }
    /// <summary>参数上限（数值型变量；空 = 不设限）</summary>
    public double? UpperLimit { get; set; }

    /// <summary>该类型占用的字数。</summary>
    public int WordCount => DataType switch
    {
        PlcDataType.Bool => 0,
        PlcDataType.Int16 or PlcDataType.UInt16 => 1,
        PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float32 => 2,
        PlcDataType.String => Math.Max(1, StringWords),
        _ => 1
    };
}
