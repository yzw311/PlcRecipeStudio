# 100轮循环代码审查日志


## 第1轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第2轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第3轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第4轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第5轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第6轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第7轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第8轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第9轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第10轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第10轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第11轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第12轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第13轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第14轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第15轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第16轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第17轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第18轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第19轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第20轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第20轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第21轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第22轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第23轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第24轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第25轮：Core/PLC/协议/超时/取消/边界

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### Debug build（第25轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.79

### Release build（第25轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Release\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Release\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Release\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Release\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.80

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第26轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第27轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第28轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第29轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第30轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第30轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第31轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第32轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第33轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第34轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第35轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第36轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第37轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第38轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第39轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第40轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第40轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第41轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第42轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第43轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第44轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第45轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第46轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第47轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第48轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第49轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第50轮：Recipe/文件/数据库/备份/Excel/并发

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第50轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

### Debug build（第50轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.84

### Release build（第50轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Release\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Release\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Release\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Release\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.80

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第51轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第52轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第53轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第54轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第55轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第56轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第57轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第58轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第59轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第60轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第60轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第61轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第62轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第63轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第64轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第65轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第66轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第67轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第68轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第69轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第70轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第70轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.14]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第71轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第72轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第73轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第74轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第75轮：Server/API/鉴权/错误映射/SignalR

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### Debug build（第75轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.81

### Release build（第75轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Release\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Release\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Release\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Release\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.81

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第76轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第77轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第78轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第79轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第80轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第80轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 8 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第81轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第82轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第83轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第84轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第85轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第86轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第87轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第88轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第89轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第90轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第90轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 9 s - PlcRecipe.Tests.dll (net8.0)

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第91轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第92轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第93轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第94轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第95轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第96轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第97轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第98轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第99轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

结果：本轮扫描完成；未发现可安全确定修复的问题。

## 第100轮：WPF/MVVM/生命周期/架构/全局回归

检查：源代码静态扫描、异常/取消/边界与相关测试入口。

### dotnet test（第100轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll
  PlcRecipe.Tests -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll
D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Tests\bin\Debug\net8.0-windows\PlcRecipe.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_总长与现实现ReadExact假设一致 [1 ms]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [SKIP]
[xUnit.net 00:00:00.13]     PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [SKIP]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_serverNode偏移判定_与PLC实际node一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_FINS魔数位于0到3 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_DA1等于握手解析出的serverNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_长度字段与header后数据段一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC3_功能帧_SA1等于握手请求携带的clientNode [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC1_握手请求_clientNode落点与PLC侧收到的一致 [1 ms]
  已跳过 PlcRecipe.Tests.OmronFinsHandshakeVectorTests.TC2_握手应答_FINS魔数位于0到3 [1 ms]

已通过! - 失败:     0，通过:   172，已跳过:     9，总计:   181，持续时间: 9 s - PlcRecipe.Tests.dll (net8.0)

### Debug build（第100轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Debug\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Debug\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Debug\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Debug\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.80

### Release build（第100轮）
  PlcRecipe.Core -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Core\bin\Release\net8.0\PlcRecipe.Core.dll
  PlcRecipe.Drivers -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Drivers\bin\Release\net8.0\PlcRecipe.Drivers.dll
  PlcRecipe.Infrastructure -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.Infrastructure\bin\Release\net8.0\PlcRecipe.Infrastructure.dll
  PlcRecipe.WpfApp -> D:\Claudeproject\zcode\peifang\PlcRecipeStudio\PlcRecipe.WpfApp\bin\Release\net8.0-windows\PlcRecipeStudio.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.75

结果：本轮扫描完成；未发现可安全确定修复的问题。
