using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.Tests;

/// <summary>
/// DeviceEditViewModel（由 DeviceEditWindow 手写控件搬运改造而来）行为回归测试。
/// 断言口径与改造前 Brand_Changed/SignalEnabled_Changed/Collect() 的行为逐条对齐：
/// 默认值、编辑回填、端口门控、显隐派生、信号联动展开、PlcDevice 组装与解析回退。
/// </summary>
public class DeviceEditViewModelTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public FakeSettings(int pollMs = 1000) => Settings.PollIntervalMs = pollMs;
        public void Load() { }
        public void Save() { }
    }

    private static PlcDevice FullDevice() => new()
    {
        Id = 7,
        Name = "1#机 ",
        Brand = PlcBrand.Siemens,
        Ip = " 192.168.1.10 ",
        Port = 1102,
        Rack = 1,
        Slot = 2,
        S7CpuType = "S71500",
        SlaveId = 3,
        DataFormat = ModbusDataFormat.CDAB,
        SerialPortName = " COM3 ",
        BaudRate = 115200,
        Parity = "Even",
        DataBits = 7,          // UI 未暴露：必须被继承
        StopBits = "Two",      // UI 未暴露：必须被继承
        Enabled = false,
        Remark = " 车间 A ",
        SignalEnabled = true,
        DownloadRequestAddress = " M900 ",
        UploadRequestAddress = "M901",
        RecipeNameAddress = "D920",
        RecipeNameWords = 20,
        DoneBitAddress = "M902",
        FailBitAddress = "M903",
        PollIntervalMs = 500
    };

    // ---------- 新增设备：默认值与原 XAML 控件默认值一致 ----------

    [Fact]
    public void 新增设备_默认值与原XAML一致()
    {
        var vm = new DeviceEditViewModel(null, new FakeSettings());

        Assert.Equal("新增设备", vm.TitleText);
        Assert.False(vm.IsExisting);
        Assert.Equal("", vm.Name);
        Assert.Equal(PlcBrand.ModbusTcp, vm.Brand);
        Assert.Equal("192.168.0.1", vm.Ip);
        Assert.Equal("502", vm.PortText);
        Assert.Equal("1", vm.SlaveText);
        Assert.Equal(ModbusDataFormat.ABCD, vm.DataFormat);
        Assert.Equal("0", vm.RackText);
        Assert.Equal("0", vm.SlotText);
        Assert.Equal("S71200", vm.CpuType);
        Assert.Equal("COM1", vm.SerialPortName);
        Assert.Equal("9600", vm.BaudText);
        Assert.Equal("None", vm.Parity);
        Assert.True(vm.Enabled);
        Assert.False(vm.SignalEnabled);
        Assert.True(vm.SignalExpanderExpanded);
        Assert.Equal("1000", vm.PollText);
        Assert.Equal("8", vm.RecipeNameWordsText);

        var d = vm.BuildDevice();
        Assert.Equal(0, d.Id);
        Assert.Equal("", d.Name);
        Assert.Equal(PlcBrand.ModbusTcp, d.Brand);
        Assert.Equal("192.168.0.1", d.Ip);
        Assert.Equal(502, d.Port);
        Assert.Equal(1, d.SlaveId);
        Assert.Equal(ModbusDataFormat.ABCD, d.DataFormat);
        Assert.Equal(0, d.Rack);
        Assert.Equal(0, d.Slot);
        Assert.Equal("S71200", d.S7CpuType);
        Assert.Equal("COM1", d.SerialPortName);
        Assert.Equal(9600, d.BaudRate);
        Assert.Equal("None", d.Parity);
        Assert.True(d.Enabled);
        Assert.False(d.SignalEnabled);
        Assert.Equal(8, d.RecipeNameWords);
        Assert.Equal(1000, d.PollIntervalMs);
        Assert.Equal(8, d.DataBits);   // 新增设备使用类默认
        Assert.Equal("One", d.StopBits);
    }

    // ---------- 编辑设备：回填与组装逐字段还原 ----------

    [Fact]
    public void 编辑设备_回填不重置端口_BuildDevice逐字段还原()
    {
        var existing = FullDevice();
        var vm = new DeviceEditViewModel(existing, new FakeSettings());

        Assert.Equal("编辑设备", vm.TitleText);
        Assert.True(vm.IsExisting);
        // 回填阶段切品牌不生效（_initialized 门控）：西门子设备端口 1102 必须保留
        Assert.Equal(PlcBrand.Siemens, vm.Brand);
        Assert.Equal("1102", vm.PortText);
        Assert.Equal("S71500", vm.CpuType);
        Assert.True(vm.SignalExpanderExpanded); // 回填 SignalEnabled=true → 展开（同原事件路径）

        var d = vm.BuildDevice();
        Assert.Equal(7, d.Id);
        Assert.Equal("1#机", d.Name);               // Trim
        Assert.Equal(PlcBrand.Siemens, d.Brand);
        Assert.Equal("192.168.1.10", d.Ip);         // Trim
        Assert.Equal(1102, d.Port);
        // Rack/Slot/Serial/Baud/Parity：2026-09 修复后全部回填保留（原实现从不回填、编辑即丢失）
        Assert.Equal(1, d.Rack);
        Assert.Equal(2, d.Slot);
        Assert.Equal("COM3", d.SerialPortName);     // Trim
        Assert.Equal(115200, d.BaudRate);
        Assert.Equal("Even", d.Parity);
        Assert.Equal("S71500", d.S7CpuType);
        Assert.Equal(3, d.SlaveId);
        Assert.Equal(ModbusDataFormat.CDAB, d.DataFormat);
        // Serial/Baud/Parity：2026-09 修复后回填保留（原实现从不回填、编辑即重置为 COM1/9600/None）
        Assert.Equal("COM3", d.SerialPortName);
        Assert.Equal(115200, d.BaudRate);
        Assert.Equal("Even", d.Parity);
        Assert.False(d.Enabled);
        Assert.Equal("车间 A", d.Remark);            // Trim
        Assert.True(d.SignalEnabled);
        Assert.Equal("M900", d.DownloadRequestAddress);
        Assert.Equal("M901", d.UploadRequestAddress);
        Assert.Equal("D920", d.RecipeNameAddress);
        Assert.Equal(20, d.RecipeNameWords);
        Assert.Equal("M902", d.DoneBitAddress);
        Assert.Equal("M903", d.FailBitAddress);
        Assert.Equal(500, d.PollIntervalMs);
        Assert.Equal(7, d.DataBits);                // 未展示字段继承
        Assert.Equal("Two", d.StopBits);
    }

    [Fact]
    public void 编辑设备_用户切品牌_端口重置为品牌默认()
    {
        var vm = new DeviceEditViewModel(FullDevice(), new FakeSettings());
        Assert.Equal("1102", vm.PortText);

        vm.Brand = PlcBrand.ModbusTcp;
        Assert.Equal("502", vm.PortText);           // DefaultPort(ModbusTcp)

        vm.Brand = PlcBrand.Mitsubishi;
        Assert.Equal("6000", vm.PortText);          // DefaultPort(Mitsubishi)

        vm.Brand = PlcBrand.Siemens;
        Assert.Equal("102", vm.PortText);           // DefaultPort(Siemens)
    }

    [Fact]
    public void 信号联动_勾选联动展开_取消折叠()
    {
        var vm = new DeviceEditViewModel(null, new FakeSettings());
        Assert.True(vm.SignalExpanderExpanded);     // 原 XAML IsExpanded="True"

        vm.SignalEnabled = true;
        Assert.True(vm.SignalExpanderExpanded);
        vm.SignalEnabled = false;
        Assert.False(vm.SignalExpanderExpanded);    // 同原 SignalEnabled_Changed

        // 编辑未启用联动的设备：原实现 IsChecked false→false 不触发 Unchecked 事件，
        // 展开状态保持 XAML 初始值 true —— VM 相等性守卫忠实复现该怪癖
        var off = FullDevice();
        off.SignalEnabled = false;
        var vmOff = new DeviceEditViewModel(off, new FakeSettings());
        Assert.False(vmOff.SignalEnabled);
        Assert.True(vmOff.SignalExpanderExpanded);
    }

    [Fact]
    public void 品牌派生显隐_IsModbus_IsS7_IsRtu()
    {
        var vm = new DeviceEditViewModel(null, new FakeSettings());

        vm.Brand = PlcBrand.ModbusTcp;
        Assert.True(vm.IsModbus);
        Assert.False(vm.IsS7);
        Assert.False(vm.IsRtu);

        vm.Brand = PlcBrand.ModbusRtu;
        Assert.True(vm.IsModbus);
        Assert.True(vm.IsRtu);
        Assert.False(vm.IsS7);

        vm.Brand = PlcBrand.Siemens;
        Assert.False(vm.IsModbus);
        Assert.True(vm.IsS7);
        Assert.False(vm.IsRtu);

        vm.Brand = PlcBrand.Mitsubishi;
        Assert.False(vm.IsModbus);
        Assert.False(vm.IsS7);
        Assert.False(vm.IsRtu);
    }

    // ---------- 解析失败回退值与原 Collect() 一致 ----------

    [Fact]
    public void 解析失败_回退值与原Collect一致()
    {
        var existing = FullDevice(); // _defaultPollMs = 500
        var vm = new DeviceEditViewModel(existing, new FakeSettings());

        vm.PortText = "abc";
        vm.SlaveText = "300";        // 超出 byte → 回退 1
        vm.BaudText = "abc";
        vm.RackText = "x";
        vm.SlotText = "y";
        vm.RecipeNameWordsText = "q";
        vm.PollText = "zzz";

        var d = vm.BuildDevice();
        Assert.Equal(PlcDevice.DefaultPort(PlcBrand.Siemens), d.Port); // 102
        Assert.Equal(1, d.SlaveId);
        Assert.Equal(9600, d.BaudRate);
        Assert.Equal(0, d.Rack);
        Assert.Equal(0, d.Slot);
        Assert.Equal(8, d.RecipeNameWords);
        Assert.Equal(500, d.PollIntervalMs);                            // 回退 _defaultPollMs
    }

    [Fact]
    public void 端口非正数_组装时回退默认_与当前行为一致()
    {
        var vm = new DeviceEditViewModel(null, new FakeSettings(1000));
        vm.PortText = "-5";
        Assert.Equal(502, vm.BuildDevice().Port); // 当前行为：Collect 阶段替换为默认端口

        vm.PortText = "99999";
        Assert.Equal(99999, vm.BuildDevice().Port); // 组装不拦，由 Ok_Click 范围校验报错
    }

    [Fact]
    public void 编辑设备_轮询解析失败_回退设备原轮询值()
    {
        // 2026-09 修复后：仅取有效的设备轮询值（>0）作为解析回退
        var existing = FullDevice(); // PollIntervalMs=500
        var vm = new DeviceEditViewModel(existing, new FakeSettings(pollMs: 2000));
        vm.PollText = "abc";
        Assert.Equal(500, vm.BuildDevice().PollIntervalMs);
    }

    [Fact]
    public void 编辑设备_设备轮询值非法时_回退全局设置而非0()
    {
        // 2026-09 修复：原实现无条件覆盖 _defaultPollMs，设备值为 0 时解析失败会回退到 0
        var existing = FullDevice();
        existing.PollIntervalMs = 0;
        var vm = new DeviceEditViewModel(existing, new FakeSettings(pollMs: 2000));
        vm.PollText = "abc";
        Assert.Equal(2000, vm.BuildDevice().PollIntervalMs);
    }

    [Fact]
    public void 编辑TCP设备_串口字段为空_回填保持界面默认值()
    {
        // TCP 类设备 SerialPortName/Parity 为空：切到 RTU 时展示默认 COM1/None（与修复前界面默认一致）
        var existing = FullDevice();
        existing.Brand = PlcBrand.ModbusTcp;
        existing.SerialPortName = null!;
        existing.Parity = null!;
        var vm = new DeviceEditViewModel(existing, new FakeSettings());

        Assert.Equal("COM1", vm.SerialPortName);
        Assert.Equal("None", vm.Parity);
        Assert.Equal("115200", vm.BaudText); // BaudRate 非空，始终回填存储值
    }

    [Fact]
    public void 新增设备_轮询解析失败_回退全局设置()
    {
        var vm = new DeviceEditViewModel(null, new FakeSettings(pollMs: 2000));
        vm.PollText = "abc";
        Assert.Equal(2000, vm.BuildDevice().PollIntervalMs);
    }
}
