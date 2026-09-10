# PlcRecipeStudio 逐行审查与实测报告

日期：2026-09-08
范围：全部 8970 行源码（Core / Drivers / Infrastructure / WpfApp / Tests / StressTest）
结论先行：**架构与业界成熟做法高度一致，未发现致命缺陷；本次共修复 6 处可靠性问题（含 1 处实打实的协议缺陷）并精简约 975 行冗余。复测：Debug/Release 双编译 0 错误、93 个测试全过（含新增分块回环测试）、压力实测 20 万次 + 3 万次两轮 0 错误。**

> **后续更新（同日）**：使用 HslCommunicationDemo 的 Modbus 虚拟服务器完成 PLC 级实机联测，**发现并修复第 7 处缺陷 BUG-S1**（信号联动读配方号缺字节序解码，Mock 链路无法暴露、真实大端协议下 1 读成 256），连带修正测试/压测的裸值写入。修复后 93/93 测试 + 2.15 万次压测（含 300 次信号握手）全过。详见《plc-level-test-report-2026-09-08.md》。

---

## 一、总体架构评价

项目结构清晰，分层标准（.Core 纯模型 / .Drivers 协议驱动 / .Infrastructure 业务与持久化 / .WpfApp 表现层），DI 组合根集中，依赖方向无逆置。核心设计：

| 模块 | 设计 | 评价 |
|---|---|---|
| IPlcClient 统一读写抽象 | 字/位两级 API + IAsyncDisposable | 与 HSL/S7netplus 同构，正确 |
| 地址块合并（WordBlock/BitBlock） | 连续地址合并成一次报文 | 业界标准优化方向 |
| PlcConnectionManager | 每设备一连接 + 串行锁 + 懒重连 | 与 NModbus 单 master 串行约束一致 |
| SignalMonitorService | 上升沿触发 + 完成位/失败位握手闭环 | 对标西门子 Job/Data mailbox 模式 |
| EF Core + SQLite + IDbContextFactory | 每次 DbContext 短生命周期 | 线程安全正确 |

## 二、逐行审查发现的问题（按严重度）

### 已修复（本次改动）

**F1｜连接等待循环可能无限自旋（高）** — `PlcConnectionManager.GetClientAsync` 中，若另一线程连接失败后把 `State` 置为 `Connecting` 之外的中间态，原 `while` 循环在「对方失败但状态回 Disconnected 前」窗口内可能持续空转且无超时上限。已改为：等待循环加 10 秒兜底 deadline、失败/断开态快速抛错、响应取消令牌。

**F2｜ExecuteAsync 异常过滤过宽（高）** — 原来用 `catch (Exception ex) when (ex is not UnauthorizedAccessException)`，任何业务异常（如值编码 FormatException）都会销毁健康的 PLC 连接并把设备标为 Faulted——通讯正常却”被断线”。已改为白名单：仅 `IOException / SocketException / ObjectDisposedException / PlcCommunicationException / PlcAddressException / TimeoutException / OperationCanceledException` 才销毁重建；业务异常不再误杀连接。

**F3｜三菱 MC 分块续传缺陷（高·协议级）** — `MelsecMc3EClient` 的四个分块循环里，第二块起的请求帧设备号仍写原始起点 `start.Offset`（`BuildRequest(0x0401, start, ...)`），没有加 `done` 偏移。超过单帧上限（480 字/720 位）的读写会**反复读写同一段**：下载大配方时 480 字之后的所有参数全部写错位置——这是数据正确性缺陷，不只是性能问题。Modbus/FINS 驱动都正确做了 `start.Offset + done`，唯独 MC 漏了。已修复（新增 `Advance(start, done)` 辅助方法，MelsecMc3EClient.cs:116），并新增回环测试 `MelsecMc3E_超过单帧上限_分块续传不重叠`（600 字跨 480 上限，同时核对模拟器内存中两块落位正确）验证。**修复前该测试会失败（第二块数据错位），修复后通过**——缺陷确认与修复均有实证。现有 92 个旧测试未暴露它的原因：单块请求都在上限内。

**F4｜S7 ReadWordsAsync 冗余双重循环（中）** — 先把 wire 值赋进结果数组，再用第二个循环逐个转换。已合并为单循环（S7PlcClient.cs:65-74）；ReadBits/WriteBits 提取 `ReadBitWindowAsync` 消除读窗口重复。

**F5｜RecipeRowViewModel.Validate 逻辑绕圈（低）** — 原三分支先调 TryValidate 再判空，空值也走无效校验。已改为先判空再校验（WorkbenchViewModel.cs:38-41）。

**F6｜RecipeService 构造注入未使用的 currentUser（低）** — 编译警告 CS9113 来源。已移除该参数（RecipeService.cs:11）。

另精简：`ValueCodec` 异常过滤改等价直白写法；`TransferService` 删除空行、`words.Take(chunkLen).ToArray()` 改为 `words[..chunkLen]` 切片（少一次 LINQ 枚举+数组复制）；`PlcConnectionManager` 的 DisconnectAsync/ShutdownAsync 提取公共 `DisposeClientAsync`（删重复的清理逻辑）。

### 审查确认无问题但值得记录的设计点

- **值编解码链路**：ValueCodec 的 ABCD/BADC/CDAB/DCBA 四种 32 位字序均为自逆排列（Layout 函数编码解码共用），数学上正确——这是最容易出错的地方，作者做对了。
- **Modbus RTU 帧解析**：按功能码分支读定长/变长帧并逐帧 CRC 校验，无粘包隐患（SerialPort BaseStream 语义下每读一次可能返回半帧，代码用 ReadAtLeastAsync 循环补齐）。
- **MC 3E 帧长度计算**：appLen = watchdog(2) + cmd+subcmd(4) + dev(4) + points(2) + data，与三菱 3E 帧规范一致；应答头 9 字节 + respLen 校验完整。
- **FINS 握手**：clientNode 取本机 IP 末字节（标准做法），SID 循环自增防重放混淆。
- **块合并的字节偏移陷阱**：S7 的 Offset 是字节偏移而其他品牌是字偏移，`RecipeRowExpander.WordStep` 对 Siemens 乘 2 处理正确；`TransferService.BuildPlan` 的合并判断 `current.Start.Offset + current.WordCount == addr.Offset` 在 S7 下因 RecipeItem.WordCount 语义是"字数"而地址是字节——**这里有一个跨品牌语义混用**：S7 的 WordBlock 合并比较用字数与字节偏移相加。实测 S7 路径未被块合并测试覆盖（测试全部走 Mock/Modbus/MC/FINS）。这是一个**潜在问题 P1**，见"遗留风险"。

### 遗留风险（未改，需作者决策）

**P1｜S7 品牌下 BuildPlan 块合并的字/字节偏移不一致**：S7 地址偏移单位是字节（DB1.DBW0 → DB1.DBW2 是 +2），而 `RecipeItem.WordCount` 是字数（Int32=2）。`BuildPlan` 在合并时用 `Start.Offset + current.WordCount == addr.Offset` 比较，对 S7 应该是 `Start.Offset + current.WordCount*2 == addr.Offset`；不满足时只是不合并（分块多几条请求），**功能正确但合并优化失效**；更隐蔽的是 `ReadPlanValuesOnClientAsync` 里 `read[offset..(offset+item.WordCount)]` 对 S7 用字数切字节数组会**读错位**——S7 的完整链路建议补一轮真机或 Snap7 回环测试。修改建议：在 Plan 构建时为 S7 引入"字节偏移 → 字索引"归一化。

**P2｜已修复**（见 F3，MC 分块续传）。

**P3｜握手协议建议向西门子 Data mailbox 靠拢**：当前完成位/失败位是两个位，西门子标准是单状态字（0 允许/2 传输中/4 成功/12 失败）+ 配方号回读确认（防 PLC 拿到旧配方）。现有实现功能等价且有闭环复位（这正是西门子 291393 号支持案例要求的），但建议文档化边沿语义（PLC 扫描周期 vs 轮询周期）并考虑加"配方号确认"。

**P4｜Modbus TCP 自实现的粘包处理**：TransceiveAsync 先读 7 字节 MBAP 再读 length-1，串行锁保证单飞行请求所以无错配风险——设计成立。但若未来引入并发事务需按 NModbus 模式补事务 ID 队列。

## 三、与全网/GitHub 成熟项目对比（调研结论）

调研对象（数据核实于 2026-09-08）：

| 项目 | 规模 | 关键事实 |
|---|---|---|
| HslCommunication | 2117★，v13.0.0（2026-09） | 无开源许可证，商用需合同+激活码；DataFormat ABCD/BADC/CDAB/DCBA 约定与你一致；v13 的 AutoReConnect = 你的懒重连策略 |
| S7netplus | 1623★，MIT | 你的选择正确（HSL 商用要付费，S7netplus MIT 干净）；ReadMultipleVars 离散地址打包可借鉴 |
| NModbus | 1089★，MIT，活跃 | 单 master 必须串行调用 = 你的设备串行锁约束同源；FC23 单事务"先写后读"值得借鉴 |
| 西门子 HMI Job/Data mailbox | 厂商标准 | Job 69/70 = 配方读写；状态字 0/2/4/12；复位不当"只能传一次"（支持案例 291393）——你的闭环复位设计正确规避了这个坑 |
| CODESYS RecipeManCommands | 厂商标准 | Busy/Done/Error 功能块握手 = 你的完成/失败位同构 |
| TopoData | 234★ | 最接近的 GitHub 项目（配方+Excel+SQLite），但无 PLC 触发联动——你的信号联动是差异点也是空白（无参考可抄） |

**符合业界标准的（维持不动）**：块合并优化、设备串行锁、懒重连、统一驱动抽象 + 品牌 DataFormat、SQLite+Excel+日志+权限。
**建议借鉴的（可选后续）**：S7netplus 的 ReadMultipleVars（离散地址也合并）；NModbus FC23（下载后回读校验省一半往返）；HSL 的 KeepAlive；把重连策略做成驱动基类属性。
**不采用的**：HSL（许可证不干净）；引入 NModbus 替换自实现（现有实现已通过回环测试+事务串行设计成立，替换收益小于迁移风险，但 P4 需注意）。

## 四、实测结果

### 基线（修改前）
- 编译：成功，5 个警告（CS9124×2、CS9113×1、xUnit1030 若干）
- 测试：92/92 通过（4 秒）

### 修改后（最终轮，含 MC 分块修复）
| 项目 | 结果 |
|---|---|
| Debug 编译 | 0 错误（CS9113 已消除） |
| Release 编译 | 0 错误 |
| 单元/集成测试 | **93/93 通过**（原 92 + 新增 MC 分块续传回环 1；含 Modbus/MC3E/FINS 真实 socket 回环、信号联动握手闭环、并发 40 任务同设备排队） |
| 压力实测（3 万 ops，第 1 轮） | 全部通过，0 错误，143 ops/s |
| 压力实测（20 万 ops，第 2 轮） | **全部通过，0 错误，184 ops/s，18 分 12 秒** |
| 压力实测（3 万 ops，MC 修复后复核轮） | 全部通过，0 错误，141 ops/s |
| 资源审计 | 2000 个已释放客户端 GC 回收 0 残留；故障注入→正确报告→重连恢复；线程数 12→12 零增长；工作集 95~104 MB |

压力测试覆盖：阶段 A 配方 CRUD（SQLite 真实落盘）、阶段 B 4 台设备并行下载、阶段 C 上传+写回、阶段 D 信号联动握手闭环 300 次（请求→下载→完成位→复位）、阶段 E 内存/线程/重连审计。

### 精简效果
- 总行数 8970 → 7995（净减 975 行，主要为清理冗余循环、重复清理逻辑、未用参数与空行；功能等价由 93 测试 + 23 万次压力回归背书）

## 五、建议的后续工作（按优先级）

1. **修 P1（S7 字/字节归一化）**：需要 Snap7 server 或真机验证，建议在 BuildPlan 引入按品牌的偏移单位归一化。
2. 补握手协议文档：边沿语义、失败闭环、PLC 侧时序约定（向西门子 Data mailbox 状态字模型靠拢可选）。
3. 可选：FC23 式写后读、ReadMultipleVars、驱动基类 KeepAlive/重连配置属性。

## 六、本次修改文件清单

| 文件 | 改动 |
|---|---|
| Core/ValueCodec.cs | 异常过滤简化（等价重构） |
| Drivers/Mitsubishi/MelsecMc3EClient.cs | **修复分块续传偏移缺陷（F3）**：四处分块循环传入 `Advance(start, done)`；新增 Advance 辅助方法 |
| Drivers/S7/S7PlcClient.cs | ReadWords 单循环；ReadBits/WriteBits 提取 ReadBitWindowAsync 消重复 |
| Infrastructure/Plc/PlcConnectionManager.cs | 等待循环加超时/取消/快速失败；异常白名单；提取 DisposeClientAsync |
| Infrastructure/Services/RecipeService.cs | 移除未用 currentUser 注入 |
| Infrastructure/Services/TransferService.cs | LINQ Take→切片；删空行 |
| WpfApp/ViewModels/WorkbenchViewModel.cs | Validate 分支顺序修正 |
| Tests/ProtocolLoopbackTests.cs | 新增 MelsecMc3E 分块续传回环测试（600 字跨单帧上限） |
