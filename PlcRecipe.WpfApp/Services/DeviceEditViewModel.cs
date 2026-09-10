using CommunityToolkit.Mvvm.ComponentModel;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.WpfApp.Services;

/// <summary>
/// 设备新增/编辑对话框 ViewModel（由 DeviceEditWindow 的手写控件搬运改造而来）。
/// 字段回填、品牌切换的端口重置与面板显隐、信号联动展开、PlcDevice 组装，
/// 行为与原 Brand_Changed/SignalEnabled_Changed/Collect() 逐项一致。
/// </summary>
public sealed partial class DeviceEditViewModel : ObservableObject
{
    private readonly PlcDevice? _existing;
    private readonly int _defaultPollMs;
    // false = 回填阶段：品牌变更不重置端口（对应原 _loaded 门控，防止编辑设备丢自定义端口）
    private bool _initialized;

    public string TitleText { get; }

    /// <summary>是否编辑已有设备（保存走 Update；与原 Ok_Click 的 _existing == null 判定一致）。</summary>
    public bool IsExisting => _existing != null;

    // ---- 静态选项（显示名与原 ComboBoxItem 的 Content/Tag 完全一致） ----
    public static KeyValuePair<PlcBrand, string>[] BrandOptions { get; } =
    [
        new(PlcBrand.Siemens, "西门子 S7"),
        new(PlcBrand.Mitsubishi, "三菱 MC"),
        new(PlcBrand.Omron, "欧姆龙 FINS"),
        new(PlcBrand.ModbusTcp, "Modbus TCP"),
        new(PlcBrand.ModbusRtu, "Modbus RTU"),
        new(PlcBrand.Mock, "模拟 PLC")
    ];

    public static KeyValuePair<ModbusDataFormat, string>[] DataFormatOptions { get; } =
    [
        new(ModbusDataFormat.ABCD, "ABCD 大端（多数PLC）"),
        new(ModbusDataFormat.CDAB, "CDAB 字交换"),
        new(ModbusDataFormat.BADC, "BADC 字节交换"),
        new(ModbusDataFormat.DCBA, "DCBA 小端")
    ];

    public static KeyValuePair<string, string>[] CpuTypeOptions { get; } =
    [
        new("S71200", "S7-1200"),
        new("S71500", "S7-1500"),
        new("S7300", "S7-300"),
        new("S7400", "S7-400"),
        new("S7200Smart", "S7-200 Smart")
    ];

    public static string[] ParityOptions { get; } = ["None", "Odd", "Even"];

    // ---- 可编辑状态（初始值 = 原 XAML 控件默认值） ----
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private PlcBrand _brand = PlcBrand.ModbusTcp;

    [ObservableProperty]
    private string _ip = "192.168.0.1";

    [ObservableProperty]
    private string _portText = "502";

    [ObservableProperty]
    private string _slaveText = "1";

    [ObservableProperty]
    private ModbusDataFormat _dataFormat = ModbusDataFormat.ABCD;

    [ObservableProperty]
    private string _rackText = "0";

    [ObservableProperty]
    private string _slotText = "0";

    [ObservableProperty]
    private string _cpuType = "S71200";

    [ObservableProperty]
    private string _serialPortName = "COM1";

    [ObservableProperty]
    private string _baudText = "9600";

    [ObservableProperty]
    private string _parity = "None";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _remark = "";

    [ObservableProperty]
    private bool _signalEnabled;

    [ObservableProperty]
    private bool _signalExpanderExpanded = true;

    [ObservableProperty]
    private string _pollText = "1000";

    [ObservableProperty]
    private string _dlReqText = "";

    [ObservableProperty]
    private string _ulReqText = "";

    [ObservableProperty]
    private string _recipeNameAddressText = "";

    [ObservableProperty]
    private string _recipeNameWordsText = "8";

    [ObservableProperty]
    private string _recipeTagText = "";

    [ObservableProperty]
    private string _recipeTagWordsText = "12";

    [ObservableProperty]
    private string _doneBitText = "";

    [ObservableProperty]
    private string _failBitText = "";

    // ---- 派生显隐/可用（替代原 Brand_Changed 的 Visibility 切换） ----
    public bool IsModbus => Brand is PlcBrand.ModbusTcp or PlcBrand.ModbusRtu;
    public bool IsS7 => Brand == PlcBrand.Siemens;
    public bool IsRtu => Brand == PlcBrand.ModbusRtu;

    public DeviceEditViewModel(PlcDevice? existing, ISettingsService settings)
    {
        _existing = existing;
        _defaultPollMs = existing is { PollIntervalMs: > 0 } ? existing.PollIntervalMs : settings.Settings.PollIntervalMs;
        TitleText = existing == null ? "新增设备" : "编辑设备";
        if (existing != null)
        {
            Name = existing.Name;
            Ip = existing.Ip;
            PortText = existing.Port.ToString();
            SlaveText = existing.SlaveId.ToString();
            Enabled = existing.Enabled;
            Remark = existing.Remark ?? "";
            Brand = existing.Brand;
            CpuType = string.IsNullOrWhiteSpace(existing.S7CpuType) ? "S71200" : existing.S7CpuType;
            DataFormat = existing.DataFormat;
            // 机架/槽号、串口参数回填（2026-09 修复：原实现从未回填，编辑设备会静默丢失这些字段）
            RackText = existing.Rack.ToString();
            SlotText = existing.Slot.ToString();
            SerialPortName = existing.SerialPortName ?? "COM1"; // TCP 类设备无串口：保持原界面默认值
            BaudText = existing.BaudRate.ToString();
            Parity = existing.Parity ?? "None";
            SignalEnabled = existing.SignalEnabled; // 触发 OnSignalEnabledChanged → 展开信号区（同原事件路径）
            DlReqText = existing.DownloadRequestAddress ?? "";
            UlReqText = existing.UploadRequestAddress ?? "";
            RecipeNameAddressText = existing.RecipeNameAddress ?? "";
            RecipeNameWordsText = (existing.RecipeNameWords <= 0 ? 8 : existing.RecipeNameWords).ToString();
            RecipeTagText = existing.RecipeTagAddress ?? "";
            RecipeTagWordsText = (existing.RecipeTagWords <= 0 ? 12 : existing.RecipeTagWords).ToString();
            DoneBitText = existing.DoneBitAddress ?? "";
            FailBitText = existing.FailBitAddress ?? "";
            PollText = existing.PollIntervalMs.ToString();
            // 仅取有效的设备轮询值作解析回退（2026-09 修复：原实现无条件覆盖，设备值为 0 时会回退到 0）
            if (existing.PollIntervalMs > 0)
                _defaultPollMs = existing.PollIntervalMs;
        }
        _initialized = true; // 回填完毕，此后用户主动切品牌才重置端口
    }

    partial void OnBrandChanged(PlcBrand value)
    {
        OnPropertyChanged(nameof(IsModbus));
        OnPropertyChanged(nameof(IsS7));
        OnPropertyChanged(nameof(IsRtu));
        // 仅响应用户主动切换品牌；回填触发的事件不得覆盖已回填的端口（否则编辑设备必丢自定义端口）
        if (_initialized)
            PortText = PlcDevice.DefaultPort(value).ToString();
    }

    partial void OnSignalEnabledChanged(bool value)
    {
        // 勾选信号联动时自动展开信号配置区（同原 SignalEnabled_Changed）
        SignalExpanderExpanded = value;
    }

    /// <summary>组装 PlcDevice（与原 Collect() 逐字段一致：Trim 范围、解析失败回退值、未展示字段继承）。</summary>
    public PlcDevice BuildDevice()
    {
        // 始终构建副本：编辑取消时不能污染列表中的原对象
        var d = _existing == null ? new PlcDevice() : new PlcDevice { Id = _existing.Id };
        if (_existing != null)
        {
            // UI 未暴露的字段一律继承旧值，防止“编辑一次即被默认值覆盖”
            d.DataBits = _existing.DataBits;
            d.StopBits = _existing.StopBits;
        }
        d.Name = Name.Trim();
        d.Brand = Brand;
        d.Ip = Ip.Trim();
        d.Port = int.TryParse(PortText.Trim(), out var p) && p > 0 ? p : PlcDevice.DefaultPort(Brand);
        d.Rack = int.TryParse(RackText, out var r) ? r : 0;
        d.Slot = int.TryParse(SlotText, out var sl) ? sl : 0;
        d.S7CpuType = CpuType;
        d.SlaveId = byte.TryParse(SlaveText.Trim(), out var s) ? s : (byte)1;
        d.DataFormat = DataFormat;
        d.SerialPortName = SerialPortName.Trim();
        d.BaudRate = int.TryParse(BaudText, out var b) ? b : 9600;
        d.Parity = Parity ?? "None";
        d.Enabled = Enabled;
        d.Remark = Remark.Trim();
        d.SignalEnabled = SignalEnabled;
        d.DownloadRequestAddress = DlReqText.Trim();
        d.UploadRequestAddress = UlReqText.Trim();
        d.RecipeNameAddress = RecipeNameAddressText.Trim();
        d.RecipeNameWords = int.TryParse(RecipeNameWordsText.Trim(), out var nw) ? nw : 8;
        d.RecipeTagAddress = RecipeTagText.Trim();
        d.RecipeTagWords = int.TryParse(RecipeTagWordsText.Trim(), out var tw) ? tw : 12;
        d.DoneBitAddress = DoneBitText.Trim();
        d.FailBitAddress = FailBitText.Trim();
        d.PollIntervalMs = int.TryParse(PollText.Trim(), out var pi) ? pi : _defaultPollMs;
        return d;
    }
}
