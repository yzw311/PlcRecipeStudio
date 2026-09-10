using PlcRecipe.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>设备 CRUD（工程师以上可写）。</summary>
public class DeviceService(
    IDbContextFactory<AppDbContext> factory,
    ICurrentUserService currentUser,
    IRecipeFileStore fileStore) : IDeviceService
{
    private void GuardEngineer()
    {
        var current = currentUser.Current;
        if (current == null || current.Role < UserRole.Engineer)
            throw new UnauthorizedAccessException("该操作需要工程师及以上权限");
    }

    public async Task<List<PlcDevice>> GetDevicesAsync(bool includeDisabled = true, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.PlcDevices.AsNoTracking();
        if (!includeDisabled) query = query.Where(d => d.Enabled);
        return await query.OrderBy(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PlcDevice> AddDeviceAsync(PlcDevice device, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        GuardEngineer();
        device.Name = device.Name.Trim();
        if (device.Name.Length == 0) throw new ArgumentException("设备名不能为空");
        if (await db.PlcDevices.AnyAsync(d => d.Name == device.Name, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"设备“{device.Name}”已存在");
        ValidateSignalConfig(device);
        device.Id = 0;
        db.PlcDevices.Add(device);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return device;
    }

    public async Task UpdateDeviceAsync(PlcDevice device, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        GuardEngineer();
        var d = await db.PlcDevices.FindAsync([device.Id], ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("设备不存在");
        var oldName = d.Name;
        d.Name = device.Name.Trim();
        if (d.Name.Length == 0) throw new ArgumentException("设备名不能为空");
        d.Brand = device.Brand;
        d.Ip = device.Ip.Trim();
        d.Port = device.Port;
        d.Rack = device.Rack;
        d.Slot = device.Slot;
        d.S7CpuType = device.S7CpuType;
        d.SlaveId = device.SlaveId;
        d.SerialPortName = device.SerialPortName;
        d.BaudRate = device.BaudRate;
        d.Parity = device.Parity;
        d.DataBits = device.DataBits;
        d.StopBits = device.StopBits;
        d.Enabled = device.Enabled;
        d.SignalEnabled = device.SignalEnabled;
        d.DownloadRequestAddress = device.DownloadRequestAddress;
        d.UploadRequestAddress = device.UploadRequestAddress;
        d.RecipeNameAddress = device.RecipeNameAddress;
        d.RecipeNameWords = device.RecipeNameWords;
        d.RecipeTagAddress = device.RecipeTagAddress;
        d.RecipeTagWords = device.RecipeTagWords;
        d.DoneBitAddress = device.DoneBitAddress;
        d.FailBitAddress = device.FailBitAddress;
        d.PollIntervalMs = Math.Clamp(device.PollIntervalMs, 100, 60_000);
        d.Remark = device.Remark;
        ValidateSignalConfig(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        // 设备改名后同步配方文件库（txt 头部 Device= 行），否则该设备配方的归属关系失效。
        // 文件迁移失败不让设备保存回滚（DB 已改名），失败文件下次改名时仍会重试。
        if (!string.Equals(oldName, d.Name, StringComparison.Ordinal))
        {
            await fileStore.UpdateDeviceNameAsync(oldName, d.Name).ConfigureAwait(false);
        }
    }

    public async Task DeleteDeviceAsync(int deviceId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        GuardEngineer();
        var d = await db.PlcDevices.FindAsync([deviceId], ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("设备不存在");
        db.PlcDevices.Remove(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>信号联动地址配置一致性校验。</summary>
    public static void ValidateSignalConfig(PlcDevice d)
    {
        if (!d.SignalEnabled) return;
        if (string.IsNullOrWhiteSpace(d.DownloadRequestAddress) && string.IsNullOrWhiteSpace(d.UploadRequestAddress))
            throw new InvalidOperationException("启用信号联动至少要配置下载或上传请求位地址");
        if (!string.IsNullOrWhiteSpace(d.DownloadRequestAddress) &&
            (string.IsNullOrWhiteSpace(d.DoneBitAddress) || string.IsNullOrWhiteSpace(d.FailBitAddress)))
            throw new InvalidOperationException("下载联动需要同时配置完成位与失败位地址");
        if (d.PollIntervalMs is < 100 or > 60_000)
            throw new InvalidOperationException("轮询间隔必须在 100~60000 毫秒之间");
    }
}
