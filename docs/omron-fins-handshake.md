# 欧姆龙 FINS/TCP 握手字节偏移验证文档

> 状态：**§6 修复已实施（2026-09）**——`OmronFinsTcpClient` 与 `OmronFinsSimulator` 已按 §5 的
> HSL 13.0.0/规约布局重写（握手 20 字节、应答 24 字节偏移、功能帧 +8 字节命令段），
> 回环测试通过、HSL 参考锁定用例通过。真机抓包（TC-1~TC-4）建议执行作最终确认；
> 向量用例保持 Skip，抓包后按 §7 激活。

## 1. 背景

评审确认：`OmronFinsTcpClient` 握手实现的字节偏移无法静态验证（涉及 FINS/TCP 规约 W505 的
长度字段口径与 node 字段位置两种公开资料口径）。仓库内 `OmronFinsSimulator` 按实现自身的布局
回写应答，回环测试通过不能证明与真实 PLC（NJ/NX1P/CJ/CS 系列 FINS/TCP，默认端口 9600）兼容。

**影响链**：握手 → `_clientNode` / `_serverNode` → `BuildFinsFrame` 中 SA1（frame[6]）/ DA1（frame[4]）
→ 所有读写功能帧。偏移错误在真机上的典型表现为：握手报 `FINS 握手应答异常（缺少 FINS 头）`，
或握手通过但首个功能帧被 PLC 以非零结束码拒绝（`PlcCommunicationException: 欧姆龙 FINS 结束码`）。

## 2. 被验证的现实现（OmronFinsTcpClient.cs，未改动）

```
请求（16 字节，OnConnectedAsync）：
  [0..3]  'F','I','N','S'
  [4..7]  16（Int32 大端）            ← 长度字段
  [8..11] 大端字段，末字节 [11] = clientNode（取本机 IP 末字节，失败回退 1）
  [12..15] 0
发送 16 字节后 ReadExactAsync(16) 收应答。

应答（假设 16 字节）：
  [0..3]  必须为 'F','I','N','S'，否则 IOException("FINS 握手应答异常")
  [8]     → _serverNode（单字节）     ← 争议偏移
  len[4..7] == 2 时按 TCP 错误帧处理（PlcCommunicationException）

后续功能帧（BuildFinsFrame）：
  frame[4] = _serverNode（DA1）
  frame[6] = _clientNode（SA1）
  frame[7] = SID（循环 +1）
```

## 3. 争议点清单（待真机裁决）

| # | 争议点 | 现实现 | 候选解释 | 真机判据 |
|---|---|---|---|---|
| R1 | 请求长度字段口径 | `[4..7]=16`，但数据段仅 8 字节 | 规约常见口径：长度 = header 之后的数据字节数（则应为 12，且总帧 20 字节）；部分资料按含头总长 | TC-1：握手能否建立；抓包看实际发送总长 |
| R2 | 请求 clientNode 位置 | 仅写 `[11]`（若字段为 [8..11] 的大端 4 字节，即取值 ≤255 时等效末字节） | 字段可能是 `[12..15]`（则 [11] 写进了前一字段） | TC-1：对照 PLC 侧收到的 node 值 |
| R3 | 应答 serverNode 偏移 | `resp[8]`（单字节） | 候选 `resp[8]` / `resp[11]` / `resp[15]`，取决于响应数据段是 `cmd(1)+node(1)@8` 还是 `cmd(4)+node(4)@8..11` 等布局 | TC-2：抓应答 + 对照 PLC 的 FINS node 号 |
| R4 | 应答长度假设 | `ReadExactAsync(16)` | 真机应答可能为 24 字节（FINS+len+cmd+err+node+保留）；只读 16 会撕帧，导致**首个功能帧解析错位** | TC-2：看应答实际总长；TC-4：首个读指令是否报帧异常 |

> 注意：R4 即使 R3 正确也会独立致障——16 字节截断会把剩余字节留在 TCP 流里，被当作下一个应答的头部。

## 4. 真机抓包测试用例

**通用准备**：Wireshark 过滤 `tcp.port == 9600`（或实际配置端口）→ Follow TCP Stream（原始十六进制）。
PLC 侧 FINS node 号获取：CX-Programmer → PLC 属性 / 网络参数，或 PLC 前面板显示。

| 用例 | 步骤 | 通过判据 | 落地动作 |
|---|---|---|---|
| TC-1 请求抓包 | 软件连接设备，抓握手请求前 20 字节 | 记录：总长、`[4..7]` 长度字段值、clientNode 实际落点 | 填入 `HandshakeRequestHex`，运行 TC1 用例（裁决 R1/R2） |
| TC-2 应答抓包 | 抓 PLC 返回的握手应答全部字节 | 记录：总长（16/24/…）、FINS 头位置、长度字段值、与 PLC node 号相等的字节偏移 | 填入 `HandshakeResponseHex` + node 号，运行 TC2 用例（裁决 R3/R4） |
| TC-3 功能帧反向验证 | 抓握手后的首个 0x0101 读帧 | FINS 帧内 `frame[4]`(DA1) 等于 TC-2 认定的 serverNode | 填入 `FunctionFrameHex`，运行 TC3 用例 |
| TC-4 失败形态对照 | 故意在设备配置里填错 IP 后重连 | 区分失败形态：握手 IO 异常（R1/R3/R4 类）vs 功能帧结束码（node 错但握手侥幸通过） | 记录现象，辅助决策树分支 |

## 5. HSL 13.0.0 参考实现对照结论（ilspycmd 反编译 `OmronFinsNet`/`OmronFinsNetHelper`）

以行业广泛使用的 HslCommunication 13.0.0 作为独立参考实现，反编译得到的权威布局如下
（源码行号引自 `HslCommunication.Profinet.Omron.OmronFinsNet` / `OmronFinsNetHelper`）：

**握手请求（`handSingle`，20 字节）**：
```
[0..3]   'F','I','N','S'
[4..7]   len = 12（4BE）          ← header 之后的数据段长度
[8..11]  command = 0x00000000     ← 节点分配请求
[12..15] clientNode（4BE，0=由 PLC 分配）
[16..19] 保留 = 0
```

**握手应答（≥24 字节，`InitializationOnConnectAsync`）**：
```
[0..3]   'F','I','N','S'
[4..7]   len
[8..11]  command
[12..15] 错误码（4BE，非 0 = 握手失败，GetStatusDescription）
[16..19] PLC 分配的 clientNode（4BE，HSL 取末字节 [19] → SA1）
[20..23] serverNode（4BE，HSL 取末字节 [23] → DA1）
```

**功能帧（`PackCommand`，26+cmd.Length 字节）**：
```
[0..3]   'F','I','N','S'
[4..7]   len = 总长 - 8
[8..11]  command = 0x00000002（FINS 帧发送）
[12..15] 0
[16..25] FINS 帧 10 字节头：ICF@16 RSV@17 GCT@18 DNA@19 DA1@20 DA2@21 SNA@22 SA1@23 SA2@24 SID@25
[26..]   FINS 指令（MRC SRC + 数据）
```

**功能应答（`ResponseValidAnalysis`）**：≥16 字节；错误码 4BE@[12..15]；剥离前 16 字节后按 FINS
帧解析——结束码在剥离后 [12..13]（即完整响应 [28..29]），数据从剥离后 [14]（完整响应 [30]）起。

### 对照裁决

| # | 争议点 | 现实现 | HSL/规约 | 裁决 |
|---|---|---|---|---|
| R1 | 请求长度字段口径 | 16 字节帧、len=16（数据段仅 8） | 20 字节帧、len=12 | **命中** |
| R2 | clientNode 落点 | `[11]`（落在 command 字段内） | `[12..15]` 4BE | **命中** |
| R3 | 应答 serverNode 偏移 | `resp[8]`、单字节 | `[20..23]` 4BE（取 [23]） | **命中** |
| R4 | 应答长度假设 | `ReadExactAsync(16)` | ≥24 字节，16 截断会撕帧 | **命中** |
| R5 | **功能帧缺命令段（新发现）** | FINS(8) + FINS 帧（ICF@8），无 cmd 段 | FINS(8) + cmd(4)=2 + 保留(4) + FINS 帧（ICF@16）；应答侧 err@12..15、结束码@28..29 | **命中** |

**结论**：本工程 Omron FINS 驱动与真实 PLC 的差异不止握手——**功能帧封装与应答解析整体偏移 8 字节**
（缺 command+保留段），真机上握手与所有读写都无法工作；仓库内模拟器按实现自身布局回写故未暴露。
修复 = 驱动整体按 §5 布局重写（握手 + `PackCommand` 等价封装 + 应答解析）+ `OmronFinsSimulator` 同步 +
激活向量测试，**需单独获批**（超出任务4"不改代码"授权）。

## 6. 修复候选（仅记录，本次未执行，获批后实施）

| 修复 | 涉及 | 内容 |
|---|---|---|
| ① 握手请求 | `OnConnectedAsync` | 按 §5 改为 20 字节帧：len=12、cmd(4)=0、clientNode(4)@12..15 |
| ② 握手应答 | `OnConnectedAsync` | `ReadExactAsync` 改为按 FinsMessage 完整收帧；错误码@[12..15]、SA1=分配的 clientNode@[19]、DA1=serverNode@[23] |
| ③ 应答解析 | `ExchangeAsync` | 端到端偏移 +8：err@[12..15]、FINS 帧@16 起、结束码@[28..29]、数据@30 起 |
| ④ 功能帧封装 | `ExchangeAsync` + `OmronFinsSimulator` 同步 | 按 §5 `PackCommand` 布局加 cmd(4)=2 + 保留(4)，FINS 帧后移至 @16 |
| ⑤ 模拟器 | `OmronFinsSimulator` | 随 ①~④ 同步重写，回环测试才继续有效 |

同步义务：任何修复都必须同步修改 `OmronFinsSimulator`（否则回环测试失去意义），并激活 §7 向量测试。

## 7. 配套测试用例（OmronFinsHandshakeVectorTests.cs）

- 全部用例当前 `Skip`，不参与通过率统计失败；真机报文落地后：把 hex 填入文件顶部的
  `HandshakeRequestHex` / `HandshakeResponseHex` / `FunctionFrameHex` 常量与 PLC node 号，
  按决策树将命中的用例 `Skip` 改为 `null` 即可执行。
- 用例清单：TC1 长度字段口径、TC2 FINS 魔数位置、TC2 长度字段与实际长度一致、
  TC2 serverNode 偏移判定（Theory：8/11/15 三候选）、TC3 功能帧 DA1 反向验证。
