# 第三方组件声明

HomeKTV 源码与便携包使用以下固定正式版本。许可证正文或获取位置记录于 `Licenses`；组件版权归各自作者所有。

| 组件 | 版本 | 许可证 |
|---|---:|---|
| .NET / ASP.NET Core / WPF | 10.0.10 / SDK 10.0.302 | MIT |
| Microsoft.Data.Sqlite | 10.0.10 | MIT |
| SQLitePCLRaw.lib.e_sqlite3 | 3.53.3 | Public Domain（SQLite）/ Apache-2.0（封装） |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| LibVLCSharp.WPF | 3.10.0 | LGPL-2.1-or-later |
| VideoLAN.LibVLC.Windows | 3.0.23.1 | LGPL-2.1-or-later / GPL-2.0-or-later（按模块） |
| NAudio | 2.2.1 | MIT |
| QRCoder | 1.8.0 | MIT |
| ToolGood.Words.Pinyin | 3.1.0.4 | MIT |
| Serilog | 4.4.0 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |
| FFmpeg Windows essentials build | 8.0.1 | GPL-3.0-or-later（此构建启用 GPL 组件） |
| Vue | 3.5.40 | MIT |
| @microsoft/signalr | 10.0.0 | MIT |
| Vite | 8.1.5 | MIT |
| @vitejs/plugin-vue | 6.0.8 | MIT |
| TypeScript | 5.9.3 | Apache-2.0 |
| vue-tsc | 3.3.7 | MIT |
| xUnit | 2.9.3 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT |
| coverlet.collector | 10.0.1 | MIT |

FFmpeg 仅作为本地媒体检查和管理员主动转码工具分发；HomeKTV 不把它用于获取未授权内容。Vue、SignalR 客户端、样式、字体与图标全部从本地 `Web` 提供，不依赖 CDN。
