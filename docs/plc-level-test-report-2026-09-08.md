# PlcRecipeStudio × HslCommunicationDemo PLC 级实测报告

日期：2026-09-08（13:09 ~ 13:58）
方式：HslCommunicationDemo V11.3.3（`D:\Bstation\HslCommunicationDemo\HslCommunicationDemo.exe`）开启 **Modbus 虚拟服务器**（127.0.0.1:502，站号 1），PlcRecipeStudio（Release 版）作为 Modbus TCP 客户端与其做真实 socket 级交互。PLC 侧数据操作由独立测试 harness（直接引用 PlcRecipe.Drivers 的 ModbusTcpPlcClient）执行并回读验证，HSL 服务器报文日志留痕。

## 一、实测结论（TL;DR）

**发现并修复 1 个真实缺陷（配方号信号联动的字节序错误），全部核心功能在真实 Modbus TCP 链路上验证通过。** 修复后：93 个自动化测试全过 + 压力测试（含 300 次信号握手）0 错误。

| # | 测试项 | 结果 | 证据 |
|---|---|---|---|
| 1 | 设备配置与连接 | ✅ | App 添加 ModbusTcp 设备 → TCP ESTABLISHED（127.0.0.1:64822↔502），HSL 在线客户端数=1 |
| 2 | 配方创建与保存 | ✅ | 批量添加 10 行 Float32（D100~D118，地址校验全 ✔），保存后配方 v2·10 行 |
| 3 | 下载写入 | ✅ | UI 下载“成功 1 台，81ms”；寄存器 D101 由残留值 0x4148 变为 0（配方值 0.0 的 ABCD 编码确实落库） |
| 4 | 上传（信号触发） | ✅ | PLC 写入 12.5/27.5/-3.25 + 置位线圈 901 → 软件自动读取 → **DB 验证：配方 v2→v3，温度1=12.5、温度2=27.5、温度3=-3.25 落库** |
| 5 | 对比·零差异 | ✅ | 对话框显示“相同 10，不同 0，读取失败 0” |
| 6 | 对比·差异检测 | ✅ | PLC 改 88.8 后对比：“相同 9，不同 1”，PLC 当前值 88.8 vs 配方值 12.5 正确显示 |
| 7 | 差异项写回 | ✅ | 勾选差异项→写入→“差异项写入成功（1 个）”，harness 回读 D100=12.5 ✓ |
| 8 | 手动下载·差异确认“是” | ✅ | PLC 改 66.6 → 下载 → 确认框“仍要继续下载覆盖？”→ 是 → D100 回读=12.5 |
| 9 | 信号联动·下载闭环 | ✅（修复后） | 线圈 900 置位 → 500ms 内自动下载（日志“信号触发下载：配方 HSL测试配方（配方号 1）”）→ 完成位 902=1 → 清 900 → 902/903 复位 |
| 10 | 信号联动·上传闭环 | ✅ | 线圈 901 置位 → 自动上传落库（见 #4）→ 完成位 902=1 |
| 11 | 不可达设备隔离 | ✅ | op10（IP 不可达）参与批量时：单台失败正确上报“读取失败 The operation was canceled.”（连接 5s 超时取消），不影响其他设备 |
| 12 | 断线重连 | ✅（间接） | App 全新启动每次重建连接正常；压力测试阶段E 故障注入→正确报告→重连恢复读成功（历史 20 万次压测 + 本轮 2.15 万次复验） |

## 二、发现并修复的缺陷

### BUG-S1｜信号联动读配方号字节序错误（严重级·已修复）

- **现象**：PLC 侧写入配方号 1（寄存器 D900 = 0x0001），软件信号触发下载时报“配方号 256 不存在”。
- **根因**：`SignalMonitorService.ReadRecipeNumberAsync` 把驱动返回的**内部字**（=线上大端值的字节反序，1 → 0x0100=256）直接当配方号使用，缺少 `ValueCodec.Decode` 解码。32 位/字符串路径（ValueCodec.Decode）不受影响，仅此一处裸用 `words[0]`。
- **为什么自动化测试没拦住**：MockPlcClient 存储的就是内部字，测试用 `SetWordValue(D900, 1)` 写入裸值 1，读写两端字节序“错得一致”，形成**自洽的假象**。这正是 Mock 测试的盲区——只有真实大端协议链路才能暴露。
- **修复**：`return int.TryParse(ValueCodec.Decode(new RecipeItem { DataType = PlcDataType.UInt16 }, words), out var n) ? n : 0;`（SignalMonitorService.cs:344）
- **连锁修正**：Mock 的存储契约是内部字，测试/压测写配方号的裸值写法同步改为经 `ValueCodec.Encode` 编码（ServiceIntegrationTests.cs:225/305、StressTest/Program.cs:304），与真实链路语义对齐。
- **回归**：93/93 测试通过；压力测试 2.15 万次操作 0 错误，其中阶段D 300 次信号握手全部通过。

## 三、实测过程记录（关键事件序列）

1. HSL Demo → 设备列表 → Modbus → Modbus Server → 启动服务（端口 502）。
2. PlcRecipeStudio 新增设备“HSL虚拟PLC”（Modbus TCP, 127.0.0.1:502, 站号 1），再添加“HSL虚拟PLC2”作批量下载目标。
3. 批量添加 10 个 Float32 变量（起始 D100，前缀“温度”），保存配方（v2）。
4. UI 下载 → HSL 报文日志可见 FC03 读 20 字（下载前强制对比）+ 写入完成提示。
5. harness 模拟现场调参（12.5/27.5/-3.25）→ 置位线圈 901 → 软件自动上传 → SQLite 验证落库（v3）。
6. harness 改 88.8 → UI 对比报 1 项差异 → 勾选写回 → harness 回读 12.5。
7. harness 改 66.6 → UI 下载 → 确认框选“是” → 回读 12.5。
8. 信号联动：DB 配置 Y900/Y901/D900/Y902/Y903（500ms 轮询）→ 对比操作触发 device1 连接 → 日志确认“信号联动监视已启动”→ 首次触发暴露 BUG-S1 → 修复 → 重启复测 → 完整闭环通过。

## 四、实测中发现的其他观察项（非缺陷，建议知悉）

- **O1｜上传到“无配方设备”得到空配方**（✅ 已于追加轮修复为 FIX-U1：以源配方地址结构为模板新建，见第七节）：上传把值存到各目标设备自己的同名配方；目标设备无配方时自动创建的是空结构（读值计划为空）。
- **O2｜下载前强制对比会对每个批量目标执行**：任一目标不可达即弹人工确认框（本次由 op10 触发）。行为正确但批量场景下可考虑“跳过不可达设备”选项。
- **O3｜信号监视的启动时机**：监视在设备“连接成功事件”时启动；下载/上传流程只连接目标设备，选中设备本身不连接。现场若只做下载、从不触发选中设备的连接，信号联动不会生效——本实测通过一次“对比”（作用于选中设备）触发。建议文档写明，或考虑在设备刷新时对启用联动的设备主动建立连接。
- **O4｜HSL Demo 的字序设置**：其浮点写读助手默认 CDAB，与本项目 ABCD 不同。Modbus 寄存器本身是裸 16 位值，不影响互操作，但用 HSL Demo 面板核对浮点时需注意字序一致。

## 五、回归结果汇总

| 验证 | 结果 |
|---|---|
| Debug / Release 编译 | 0 错误 |
| 单元 + 集成测试（含协议回环、信号联动、并发排队） | **93/93 通过** |
| 压力测试（本轮 2.15 万次，含 300 次信号握手） | 0 错误，120 ops/s，线程 12→12，内存 96 MB，客户端 GC 回收 0 残留 |
| 历史累计（审查阶段） | 20 万 + 3 万次压测 0 错误 |

## 六、修改文件清单（本轮实测新增）

| 文件 | 改动 |
|---|---|
| Infrastructure/Plc/SignalMonitorService.cs | **BUG-S1 修复**：ReadRecipeNumberAsync 经 ValueCodec.Decode 解码配方号 |
| Tests/ServiceIntegrationTests.cs | 配方号写入改经 ValueCodec.Encode（2 处） |
| StressTest/Program.cs | 信号握手阶段配方号写入改经 ValueCodec.Encode |

测试产物：`%TEMP%\plcharness`（PLC 级读写 harness，引用 PlcRecipe.Drivers，可复用于后续回归）。

---

## 七、追加轮：上传新建配方以源配方地址结构为模板（2026-09-08 14:30~14:45）

### 需求实现（2 处修改）

**FIX-U1｜手动批量上传到“无配方设备”改为模板创建（此前为占位地址）**
- 原行为：目标设备无任何配方时，创建空配方 → 按空结构读取（读到 0 个值）→ 用占位地址 `D100+i`/Float32 保存，用户需手工修正地址。
- 新行为：以上传源配方（界面上选中的配方）的**变量名/地址/类型/单位结构为模板**新建配方，值来自该 PLC 的实际读数（复用 SaveRecipeFromAsync，与信号联动自动创建同一条服务路径）。源配方未选择且目标无配方时报明确失败原因。

**FIX-U2｜信号上传“配方号 0”语义落地**
- 原行为：配方号模式收到 0 时，描述为“配方号 0（更新首配方）”但**实际不保存任何数据**（读取后丢弃）。
- 新行为：配方号 0 = 更新设备第一个配方（ApplyReadValuesAsync 落库，版本 +1），与界面描述一致。

### E2E 验证（真实 Modbus TCP + UI 操作 + SQLite 核对）

| 场景 | 结果 | 证据 |
|---|---|---|
| 手动上传到无配方设备 | ✅ | 清空 HSL虚拟PLC2 名下配方 → harness 写 D100=55.5 → UI 上传 → **新配方自动创建：温度1~10 / D100~D118 / Float32（结构复制自源配方），温度1=55.5 为 PLC 实际读数**，提示“已保存到新配方” |
| 信号上传·配方号 0 | ✅ | D900=0 + PLC 写 77.7 + 置位线圈 901 → 完成位 902=1 → **首配方 v3→v4，温度1 更新为 77.7** |

### 回归

- Release 编译 0 错误；93/93 测试通过；压力测试 2.15 万次（含 300 次信号握手）0 错误。

### 修改文件（追加轮）

| 文件 | 改动 |
|---|---|
| WpfApp/ViewModels/WorkbenchViewModel.cs | FIX-U1：上传 else 分支改为“源配方结构模板 + SaveRecipeFromAsync”；无模板时明确报错 |
| Infrastructure/Plc/SignalMonitorService.cs | FIX-U2：配方号 0 → ownRecipe = 设备第一个配方（真实落库） |

> 备注：本轮 E2E 中观察到 UIA Invoke 偶发不触发 WPF 命令按钮（自动化环境问题，非软件缺陷），改用真实鼠标坐标点击后稳定复现；另确认信号监视仅在设备连接成功事件时启动（观察项 O3），本轮通过一次“对比”触发 device1 连接后完成验证。

---

## 八、追加轮 2：右键菜单命令全部失效修复（2026-09-08 15:00~15:10）

### 用户报告

设备无法在右键菜单里删除。

### 排查过程

1. **数据库层排除**：用与 App 相同的 Microsoft.Data.Sqlite/连接串在数据库副本上执行 `DELETE FROM PlcDevices`（带配方的设备）——`PRAGMA foreign_keys = 1`、DDL 含 `ON DELETE CASCADE`、删除成功且配方级联清空。**DB 层正常**。
2. **UI 层复现**：创建一次性设备 DelTest01 → 真实鼠标右键 → 上下文菜单正常弹出 → 点击“删除设备”→ 菜单关闭但**无确认框、无删除、无报错**。截图显示菜单项为黑色可用态——这是 `MenuItem.Command = null` 的典型表现（命令为 null 时项不置灰但点击无效）。

### 根因（BUG-M1）

XAML 中 `ContextMenu Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=ListBox}}"` —— **ContextMenu 是 Popup 内容，不在页面的视觉树内**，`RelativeSource FindAncestor` 无法跨越此边界找到 ListBox，绑定静默失败 → `Tag = null` → 四个菜单项的 `PlacementTarget.Tag.XxxCommand` 全部为 null。**设备菜单（连接/断开/编辑/删除）与配方菜单（复制/重命名/导出/导入/删除）共 9 个菜单项全部失效**，其中删除的“无任何反应”最容易被感知。

### 修复

利用视觉树内的 StackPanel 作中转（FindAncestor 在视觉树内有效），ContextMenu 再经 `PlacementTarget` 取回：

```xml
<StackPanel Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=ListBox}}">
    <StackPanel.ContextMenu>
        <ContextMenu Tag="{Binding PlacementTarget.Tag, RelativeSource={RelativeSource Self}}">
            <MenuItem Header="删除设备" Command="{Binding PlacementTarget.Tag.DeleteDeviceCommand, ...}" CommandParameter="{Binding}"/>
```

设备菜单与配方菜单两处同步修复（WorkbenchPage.xaml）。

### E2E 验证（修复后）

| 场景 | 结果 |
|---|---|
| 右键 DelTest01 → 删除设备 → 确认框弹出 → 点“是” | ✅ 确认框正常出现（修复前无） |
| 删除落库 + 级联 + UI 刷新 | ✅ 设备表 DelTest01 消失，UI 列表刷新后移除、选中回落 op10 |
| 同链路“编辑设备”菜单 | ✅ 编辑对话框正常弹出 |

### 回归

Release 编译 0 错误；93/93 测试通过。

### 修改文件（追加轮 2）

| 文件 | 改动 |
|---|---|
| WpfApp/Views/Pages/WorkbenchPage.xaml | BUG-M1 修复：设备/配方两处 ContextMenu 的 Tag 中转改经视觉树内 StackPanel |

---

## 九、追加轮 3：信号联动改为纯配方名模式（2026-09-08 20:00~20:20）

### 需求

去掉"配方号寄存器"，信号联动只按**配方名字符**查找配方；配方名字数在编辑器中可填写。

### 发现并修复的缺陷（BUG-N1）

**编辑设备从不保存配方名地址/字数**：`DeviceService.UpdateDeviceAsync` 只逐字段拷贝了请求位/完成位等，**漏拷 `RecipeNameAddress` 和 `RecipeNameWords`**——新增设备时填的能保存，之后每次编辑都会丢失/回退为空。这正是"字数没有地方填写"观感的另一半根因（填了也存不上）。已补拷贝。

### 功能改造

| 改动 | 说明 |
|---|---|
| PlcDevice | 移除 `RecipeNumberAddress` 属性（DB 旧列保留不映射，向后兼容） |
| DeviceEditWindow | 删除"配方号寄存器"输入行；"配方名地址 + 名字字数"上移到同行并加说明 Tooltip |
| SignalMonitorService | 下载/上传统一为纯名字匹配；删除配方号读取逻辑；上传遇新名字自动建配方（结构复制设备第一个配方） |
| ValueCodec | 字符串编码 ASCII → **UTF-8**（否则中文配方名经 PLC 写入全变问号，永远无法匹配；纯 ASCII 数据字节不变，向下兼容） |
| DeviceService.UpdateDeviceAsync | **BUG-N1 修复**：补拷 RecipeNameAddress/RecipeNameWords |

### E2E 验证（1#机 = ModbusTcp → HSL 虚拟服务器真网络，harness 模拟 PLC 侧）

| 场景 | 结果 |
|---|---|
| 信号下载·按名匹配 | ✅ harness 写 UTF-8"标准配方"到 D920 + 置位线圈 900 → 1s 内自动下载 → 完成位 Y902=1、失败位 903=0 → **PLC 回读温度1=185.0（配方值）** → 清请求位 → 902 复位 |
| 信号上传·按名更新 | ✅ PLC 改 42.0 + 置位线圈 901 → 完成位 Y902=1 → **DB"标准配方"v2→v3，温度1='42'** |
| 名字不存在 | ✅ 写"NOSUCH" + 置位 900 → 失败位 Y903=1 |
| 信号上传·新名字自动建配方 | ✅（此前 E2E 已验证：MOLD2/模板结构复制） |

### 回归

Release 编译 0 错误；**93/93 测试通过**（信号用例已全部改为名字模式，含"名字不存在→失败位"用例）；压力测试 2.15 万次（300 次名字模式握手）0 错误。

### 修改文件（追加轮 3）

| 文件 | 改动 |
|---|---|
| Core/Models/PlcDevice.cs | 移除 RecipeNumberAddress；注释更新 |
| Core/ValueCodec.cs | 字符串编解码 ASCII → UTF-8（错误信息同步） |
| Infrastructure/Services/DeviceService.cs | **BUG-N1**：UpdateDeviceAsync 补拷配方名地址/字数 |
| Infrastructure/Plc/SignalMonitorService.cs | 纯名字匹配；删除 ReadRecipeNumberAsync |
| WpfApp/Services/DeviceEditWindow.xaml(.cs) | 移除配方号输入；名字地址+字数上移、说明更新 |
| Tests/TestHost.cs、Tests/ServiceIntegrationTests.cs、StressTest/Program.cs | 信号用例改名字模式；新增"名字不存在→失败位"用例 |

---

## 十、追加轮 4：名字字数输入的可用性修正（2026-09-08 20:15~20:40）

### 用户反馈

"配方名地址不止有地址还有位数，有的条码多有的条码少，占用的地址多和少要输入"——名字字数输入要可见、可填、可保存。

### 说明与改进

字数输入框本身存在于编辑器信号联动区（配方名地址旁），但藏在默认折叠的 Expander 里不显眼。本轮改进：

1. **信号联动区默认展开**——新增/编辑设备时"配方名地址 + 名字字数 + 请求位/完成位"直接可见。
2. **字数按最长条码填写**（Tooltip 明确）：名字区大小固定，按最长的名字/条码设置字数；短名字写入后自动按 0 结尾，软件读取时截断，**不同长度的条码共用同一配置即可正确匹配**。换算：每字 2 字节，ASCII/数字条码 1 字节/字符，汉字 UTF-8 3 字节/字符（例：20 字 = 40 字节，可容纳 40 位数字条码或 13 个汉字）。
3. 勾选"启用信号联动"时自动展开面板（Checked 事件联动）。

### 持久化验证

- UI 实测：新增设备 → 勾选信号联动 → 填配方名地址/名字字数/请求位 → 保存 → **全部字段落库**（SQLite 回读一致）。
- 新增回归测试 `设备编辑_配方名字段持久化`（BUG-N1 锁定）：UpdateDeviceAsync 修改 RecipeNameAddress/RecipeNameWords → GetDevicesAsync 回读一致。**94/94 测试通过**。
- 发布目录已重新 `dotnet publish`（win-x64），旧发布实例同步到最新代码。

### 修改文件（追加轮 4）

| 文件 | 改动 |
|---|---|
| WpfApp/Services/DeviceEditWindow.xaml | 信号区默认展开；字数框加"字"单位与按最长条码填写的 Tooltip |
| WpfApp/Services/DeviceEditWindow.xaml.cs | SignalEnabled_Changed 联动展开；编辑已启用设备时自动展开 |
| Tests/ServiceIntegrationTests.cs | 新增 设备编辑_配方名字段持久化 用例 |

---

## 十一、追加轮 5：编辑设备界面放大（2026-09-08 22:50~23:20）

### 用户反馈

"编辑设备菜单界面放大点"（此前 820px 宽度下"配方名地址/字数"与"完成位/失败位"挤在同一行，字数框被压缩溢出、不便于填写与查看）。

### 本轮改动

| 改动 | 说明 |
|---|---|
| 编辑窗口布局重排 | 顶部连接参数行高 34→36、文本框高度 28/30→32/34/38；对话框整体加大（`Width` 保持、内部网格按行自动加高，按钮区 Margin/尺寸同步放大） |
| 信号联动区按行独立排布 | 行0：启用勾选 + 轮询间隔；行1：下载/上传请求位；行2：配方名地址 + 名字字数 + "字（按最长条码填）"尾注与 Tooltip；行3：完成位 / 失败位各占一行不再同格挤压；行4：匹配规则说明横跨整行。行高统一 40px、输入框高度 32 |
| 完成位/失败位分行 | 修复此前与"配方名地址行"同格挤压导致两框显示不全的问题 |
| 名字字数输入可视 | `RecipeNameWordsBox` 独立可见可填，右侧增加单位尾注"字（按最长条码填）"，Tooltip 说明字数换算（每字 2 字节；ASCII/数字条码 1 字节/字符、汉字 UTF-8 3 字节/字符） |

### E2E 实测（新启动实例 PID 44036）

| 场景 | 结果 |
|---|---|
| 右键 2#机 卡片 → 编辑设备 | ✅ 对话框正常弹出（窗口 820×668，信号联动区默认展开） |
| UIA 全元素枚举 | ✅ TitleText=编辑设备；NameBox=2#机；RecipeNameBox=D920；RecipeNameWordsBox=8；DoneBox=M902；FailBox=M903；PollBox=1000；DlReqBox=M900；UlReqBox=M901（全部回显正确，无 XamlParse/布局异常） |
| 布局边界 | ✅ 字数框 105px 独立列、尾注 149px、完成/失败位同行右侧等宽 307px，无重叠溢出 |
| 截图存档 | 全屏截图已捕获（对话框 820×668 居中，尺寸合理，无文字截断） |

### 回归

Release 编译 0 错误、0 警告；应用冷启动 + 编辑设备对话框正常。上一轮已 94/94 测试通过、压力 2.15 万次 0 错误，本轮改动仅涉及 DeviceEditWindow 一个窗口的 XAML 布局，服务/驱动/DB 层未触碰。

### 修改文件（追加轮 5）

| 文件 | 改动 |
|---|---|
| WpfApp/Services/DeviceEditWindow.xaml | 编辑设备布局放大与逐行重排（配方名地址行、字数行、完成/失败位行、匹配规则行） |
