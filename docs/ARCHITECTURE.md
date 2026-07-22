# HomeKTV 架构

## 总览

HomeKTV 采用单进程模块化桌面架构。WPF 是生命周期宿主；ASP.NET Core WebApplication 在后台端口启动；SQLite 保存歌曲库和队列；LibVLC 负责播放并把画面绑定到独立 WPF 大屏；Vue 静态资源由内嵌服务器从便携包 `Web` 目录提供。

```text
WPF 管理台 ─┐
第二屏窗口 ─┼─ 应用服务 / 事件 ─ Core ─ SQLite
Vue 手机端 ─┘        │          │
       SignalR/API ─ Server      ├─ Library / Lyrics
                                 └─ Player / LibVLC
```

## 依赖方向

- `Core` 不依赖 UI、数据库或媒体库。
- `Lyrics` 与 `Library` 仅依赖 `Core`。
- `Infrastructure` 实现 `Core` 定义的持久化和系统接口。
- `Player` 实现播放接口并封装 LibVLCSharp。
- `Server` 组合应用服务并暴露受限 API/SignalR。
- `App` 是唯一组合根，负责启动、停止和全局异常处理。

## 便携根目录

生产环境使用 `PortablePaths.FromBaseDirectory()`，其根为 `AppContext.BaseDirectory`。所有子路径通过该值组合并经过边界校验。数据库保存规范化相对路径，解析时使用当前根，因此目录复制、移动和盘符变化不会破坏媒体引用。

## 数据一致性与可移动存储

SQLite 启用外键和 busy timeout。写入由进程内 `SemaphoreSlim` 串行化；事务尽量短，并在 I/O 异常时快速失败且记录日志。启动执行 `PRAGMA quick_check`。备份先检查点，再使用 SQLite 在线备份 API；恢复前先备份现库，并以临时文件加原子替换降低拔盘风险。

## 播放和队列

队列支持 FIFO 与按点歌人轮转。置顶项目始终优先，同一用户内部保持相对顺序。播放器只发布状态事件，不直接修改数据库；队列编排服务响应完成/错误事件，更新状态并推进下一首，播放错误不会结束 WPF 进程。

## 网络和安全

默认端口 16888，冲突时按顺序探测备用端口。仅本机模式绑定 loopback，局域网模式绑定所有 IPv4 接口。普通会话只能管理自己的待播项目；管理 API 要求本地 PIN。Web 端没有 CDN 或远程启动依赖。防火墙脚本是独立、显式确认的管理员操作。

## 恢复与降级

- 配置 JSON 损坏时改名保存并生成默认配置。
- 数据库检查失败时给出恢复路径并保留损坏文件。
- 缺少 FFmpeg 只禁用检测/转码，不阻止点歌和播放。
- 没有第二显示器时大屏退化为主屏窗口模式。
- 媒体缺失或解码失败时记录失败并推进下一首。

