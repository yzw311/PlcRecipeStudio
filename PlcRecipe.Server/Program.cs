using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Infrastructure;
using PlcRecipe.Infrastructure.Data;
using PlcRecipe.Server.Hubs;
using PlcRecipe.Server.Health;

/// <summary>
/// PlcRecipe.Server：配方服务的 REST API 宿主（MES 集成的服务端入口）。
/// 复用与 WPF 完全相同的 AddPlcRecipeInfrastructure 组合根（同一套驱动/服务/数据库）。
///
/// 运行：dotnet run --project PlcRecipe.Server            （默认监听 http://localhost:5000）
/// 数据目录：环境变量 PLCRECIPE_DATA 覆盖（默认与 WPF 相同的 %LOCALAPPDATA%\PlcRecipeStudio）
/// 鉴权：设置文件 ApiKey 非空时，/api/* 必须携带 X-Api-Key 头（/health 与 /hubs 不校验）。
/// 安全默认：ApiKey 为空时 /api/* 一律 503 拒绝（否则服务层权限守卫形同虚设，等于把 PLC 写权限暴露给局域网）；
/// /hubs/plc 连接需携带 ?key=<ApiKey>。
/// 注意：SQLite 模式下请勿与本机 WPF 同时运行（单写者）；多工作站并发请切换 SQL Server。
///
/// 端点一览：
///   GET  /health                                  健康探针（数据库）
///   GET  /api/health                              数据库 + 各设备连接状态
///   GET/POST/PUT/DELETE /api/devices[/{id}]       设备 CRUD
///   GET  /api/devices/{deviceId}/recipes          配方列表
///   POST /api/devices/{deviceId}/recipes          新建配方
///   GET  /api/recipes/{id}                        配方（含数据行）
///   POST /api/recipes/{id}/rows                   保存数据行（全量校验+版本+1+历史快照）
///   DELETE /api/recipes/{id}                      删除配方
///   GET  /api/recipes/{id}/history                版本历史
///   GET  /api/history/{historyId}/rows            取历史快照数据行
///   POST /api/recipes/{id}/rollback               回滚（保存为新版本）
///   POST /api/recipes/{id}/download/{deviceId}    下载到 PLC（含水印/回读校验，按设置）
///   POST /api/recipes/{id}/upload/{deviceId}?apply= 是否把读回值落库
///   GET  /api/logs?from&to&user&page              操作日志（分页）
///   /hubs/plc                                     SignalR：deviceStateChanged 实时推送
/// </summary>

var dataDir = Environment.GetEnvironmentVariable("PLCRECIPE_DATA") ?? ServiceCollectionExtensions.DefaultDataDirectory();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlcRecipeInfrastructure(dataDir);
builder.Services.AddSignalR();
builder.Services.AddSingleton<DatabaseHealthCheck>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
// 枚举同时接受名称与数字（如 Brand: "Mock" 或 3）
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
var app = builder.Build();

// Never expose exception details through the HTTP API.
// service identity is fixed; request DTOs never select the audit user
app.Services.GetRequiredService<ICurrentUserService>().Set(new User { Id = 0, UserName = "api-service", Role = UserRole.Admin });

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem(statusCode: 500, title: "服务器内部错误", extensions: new Dictionary<string, object?> { ["code"] = "internal_error" }).ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs")) { await next(); return; }
    var settings = context.RequestServices.GetRequiredService<ISettingsService>().Settings;
    var configured = settings.ApiKey;
    var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(configured)) { await ApiProblem(context, 503, "api_disabled", "API 未启用"); return; }
    if (!ApiKeyGuard.Equals(provided ?? "", configured)) { await ApiProblem(context, 401, "unauthorized", "未授权"); return; }
    // Optional role mapping: ApiKeyRoles = "reader:key1;engineer:key2;admin:key3". Legacy ApiKey is admin.
    var role = UserRole.Admin;
    var roles = settings.GetType().GetProperty("ApiKeyRoles")?.GetValue(settings)?.ToString();
    if (!string.IsNullOrWhiteSpace(roles)) { role = roles.Split(';').Select(x => x.Split(':', 2)).Where(x => x.Length == 2 && ApiKeyGuard.Equals(provided!, x[1])).Select(x => x[0].ToLowerInvariant()).Select(x => x switch { "reader" => UserRole.Operator, "engineer" => UserRole.Engineer, _ => UserRole.Admin }).FirstOrDefault(UserRole.Admin); }
    context.Items["ApiRole"] = role;
    context.RequestServices.GetRequiredService<ICurrentUserService>().Set(new User { Id = 0, UserName = "api-service", Role = role });
    await next();
});

static async Task ApiProblem(HttpContext c, int status, string code, string title) => await Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(c);

app.Use(async (context, next) => { try { await next(); } catch (Exception) { if (!context.Response.HasStarted) await ApiProblem(context, 400, "request_failed", "请求无法处理"); } });

// 数据库初始化（建库/增量迁移/内置管理员/UTC 迁移）——Server 宿主必须自行完成
await app.Services.InitializeDatabaseAsync();

app.MapHealthChecks("/health");

// ---------- 健康 / 状态 ----------
app.MapGet("/api/health", async (IDbContextFactory<AppDbContext> factory, IPlcConnectionManager connections, IDeviceService devices) =>
{
    bool dbOk;
    try
    {
        await using var db = await factory.CreateDbContextAsync();
        dbOk = await db.Database.CanConnectAsync();
    }
    catch { dbOk = false; }

    var list = await devices.GetDevicesAsync(true);
    return Results.Ok(new
    {
        status = dbOk ? "ok" : "degraded",
        database = dbOk,
        serverTimeUtc = DateTime.UtcNow,
        devices = list.Select(d => new { d.Id, d.Name, state = connections.GetState(d.Id).ToString() })
    });
});

// ---------- 设备 ----------
app.MapGet("/api/devices", async (IDeviceService svc) => Results.Json(await svc.GetDevicesAsync(true)));

app.MapPost("/api/devices", async (PlcDevice device, IDeviceService svc) =>
{
    try { var created = await svc.AddDeviceAsync(device); return Results.Created($"/api/devices/{created.Id}", created); }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapPut("/api/devices/{id:int}", async (int id, PlcDevice device, IDeviceService svc) =>
{
    device.Id = id;
    try { await svc.UpdateDeviceAsync(device); return Results.NoContent(); }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapDelete("/api/devices/{id:int}", async (int id, IDeviceService svc) =>
{
    try { await svc.DeleteDeviceAsync(id); return Results.NoContent(); }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

// ---------- 配方 ----------
app.MapGet("/api/devices/{deviceId:int}/recipes", async (int deviceId, IRecipeService svc) =>
    Results.Json(await svc.GetRecipesAsync(deviceId)));

app.MapGet("/api/recipes/{id:int}", async (int id, IRecipeService svc) =>
{
    try { return Results.Json(await svc.GetRecipeAsync(id)); }
    catch (Exception) { return Results.NotFound(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapPost("/api/devices/{deviceId:int}/recipes", async (int deviceId, RecipeCreateRequest req, IRecipeService svc) =>
{
    try
    {
        var created = await svc.CreateRecipeAsync(deviceId, req.Name, req.Remark, "api");
        return Results.Created($"/api/recipes/{created.Id}", created);
    }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapPost("/api/recipes/{id:int}/rows", async (int id, RecipeSaveRequest req, IRecipeService svc) =>
{
    try
    {
        await svc.SaveRecipeAsync(id, req.Rows, "api", req.ChangeNote);
        return Results.Ok(new { saved = true });
    }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapDelete("/api/recipes/{id:int}", async (int id, IRecipeService svc) =>
{
    try { await svc.DeleteRecipeAsync(id); return Results.NoContent(); }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapGet("/api/recipes/{id:int}/history", async (int id, IRecipeService svc) =>
    Results.Json(await svc.GetVersionHistoryAsync(id)));

app.MapGet("/api/recipes/{id:int}/history/{version:int}/rows", async (int id, int version, IRecipeService svc) =>
{
    try { return Results.Json(await svc.GetVersionSnapshotAsync(id, version)); }
    catch (Exception) { return Results.NotFound(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapPost("/api/recipes/{id:int}/rollback", async (int id, RollbackRequest req, IRecipeService svc, CancellationToken ct) =>
{
    try
    {
        var recipeBefore = await svc.GetRecipeAsync(id);
        if (recipeBefore.Version != req.ExpectedVersion)
            return Results.Conflict(new { error = "配方版本已变化，请刷新后重试", code = "recipe_version_conflict" });
        var snapshot = await svc.GetVersionSnapshotAsync(id, req.Version);
        await svc.SaveRecipeAsync(id, snapshot, "api-service", $"回滚自 v{req.Version}", ct);
        var recipeAfter = await svc.GetRecipeAsync(id);
        return Results.Ok(new { rolledBack = true, version = recipeAfter.Version });
    }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

// ---------- 上传 / 下载 ----------
app.MapPost("/api/recipes/{id:int}/download/{deviceId:int}", async (int id, int deviceId,
    IRecipeService recipes, IDeviceService devices, ITransferService transfers, ISettingsService settings,
    IOpLogService opLogs, CancellationToken ct) =>
{
    try
    {
        var recipe = await recipes.GetRecipeAsync(id);
        var device = (await devices.GetDevicesAsync(true)).FirstOrDefault(d => d.Id == deviceId)
                     ?? throw new InvalidOperationException("设备不存在");
        var result = await transfers.DownloadAsync(device, recipe, null, settings.Settings.VerifyAfterWrite, ct);
        await opLogs.AddAsync("下载配方(API)", $"{device.Name} ← {recipe.Name}", result.Message,
            result.Status == TransferStatus.Success, TransferSource.Manual, result.DurationMs);
        return Results.Json(result);
    }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

app.MapPost("/api/recipes/{id:int}/upload/{deviceId:int}", async (int id, int deviceId, bool apply,
    IRecipeService recipes, IDeviceService devices, ITransferService transfers,
    IOpLogService opLogs, CancellationToken ct) =>
{
    try
    {
        var recipe = await recipes.GetRecipeAsync(id);
        var device = (await devices.GetDevicesAsync(true)).FirstOrDefault(d => d.Id == deviceId)
                     ?? throw new InvalidOperationException("设备不存在");
        var result = await transfers.UploadAsync(device, recipe, null, ct);
        if (apply && result.Status == TransferStatus.Success && result.ReadValues != null)
        {
            await recipes.ApplyReadValuesAsync(recipe.Id, result.ReadValues, "api");
            result.Message += "；已落库";
            var violations = RecipeLimits.Check(recipe.Items, result.ReadValues);
            if (violations.Count > 0)
            {
                result.Message += $"；⚠ 参数越界 {violations.Count} 项";
                await opLogs.AddAsync("参数越界", device.Name, string.Join("；", violations),
                    true, TransferSource.Manual, result.DurationMs);
            }
        }
        await opLogs.AddAsync("上传配方(API)", $"{device.Name} → {recipe.Name}", result.Message,
            result.Status == TransferStatus.Success, TransferSource.Manual, result.DurationMs);
        return Results.Json(result);
    }
    catch (Exception) { return Results.BadRequest(new { error = "请求无法处理", code = "request_failed" }); }
});

// ---------- 操作日志 ----------
app.MapGet("/api/logs", async (DateTime? from, DateTime? to, string? user, int? page, IOpLogService svc) =>
    Results.Json(await svc.QueryAsync(from, to, user, null, null, null, Math.Max(1, page ?? 1), 50)));

// ---------- SignalR 实时推送 ----------
app.MapHub<PlcStateHub>("/hubs/plc");
var connections = app.Services.GetRequiredService<IPlcConnectionManager>();
var hubContext = app.Services.GetRequiredService<IHubContext<PlcStateHub>>();
var broadcastGate = new SemaphoreSlim(1, 1);
connections.StateChanged += (_, e) =>
{
    // fire-and-forget 但必须观察异常：SignalR 关闭/断连瞬间的发送失败不应成为未观察异常
    _ = BroadcastAsync(e);
};

async Task BroadcastAsync(object e)
{
    if (!await broadcastGate.WaitAsync(0)) return;
    try { await hubContext.Clients.All.SendAsync("deviceStateChanged", e); } catch (Exception ex) { Serilog.Log.Debug(ex, "SignalR 设备状态广播失败"); } finally { broadcastGate.Release(); }
}

app.Run();

// ---------- 请求 DTO ----------
public sealed record RecipeCreateRequest(string Name, string? Remark);
public sealed record RecipeSaveRequest(IReadOnlyList<RecipeItem> Rows, string? ChangeNote);
public sealed record RollbackRequest(int Version, int ExpectedVersion);

/// <summary>API 访问密钥校验（/api 中间件与 SignalR Hub 共用）：常量时间比较防时序侧信道。</summary>
public static class ApiKeyGuard
{
    /// <summary>SignalR 连接串携带密钥的查询参数名：/hubs/plc?key=&lt;ApiKey&gt;</summary>
    public const string QueryKeyName = "key";

    public static bool Equals(string provided, string expected)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(provided ?? "");
        var b = System.Text.Encoding.UTF8.GetBytes(expected ?? "");
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

// 供测试宿主（WebApplicationFactory）使用
public partial class Program { }
